using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO.Compression;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Services;

/// <summary>
/// Implements abbreviation lookup with in-memory caching and optional syncing from FDA sources.
/// </summary>
public class AbbreviationService : IAbbreviationService
{
    private const string DefaultOpenFdaUrl = "https://api.fda.gov/drug/label.json";
    private readonly ApplicationDbContext _context;
    private readonly ILogger<AbbreviationService> _logger;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    // In-memory cache: {shortForm.ToLower() -> [abbreviations]}
    private ConcurrentDictionary<string, List<Abbreviation>> _shortFormCache = 
        new(StringComparer.OrdinalIgnoreCase);

    // In-memory cache: {expandedForm.ToLower() -> [abbreviations]}
    private ConcurrentDictionary<string, List<Abbreviation>> _expandedFormCache = 
        new(StringComparer.OrdinalIgnoreCase);

    public AbbreviationService(
        ApplicationDbContext context,
        ILogger<AbbreviationService> logger,
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _context = context;
        _logger = logger;
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<List<Abbreviation>?> LookupByShortFormAsync(string shortForm)
    {
        if (string.IsNullOrWhiteSpace(shortForm))
            return null;

        var normalized = shortForm.Trim();

        // Check cache first
        if (_shortFormCache.TryGetValue(normalized, out var cached))
            return cached;

        // Query database
        var results = await _context.Abbreviations
            .Where(a => a.ShortForm.ToLower() == normalized.ToLower())
            .ToListAsync();

        return results.Count > 0 ? results : null;
    }

    public async Task<List<Abbreviation>?> LookupByExpandedFormAsync(string expandedForm)
    {
        if (string.IsNullOrWhiteSpace(expandedForm))
            return null;

        var normalized = expandedForm.Trim();

        // Check cache first
        if (_expandedFormCache.TryGetValue(normalized, out var cached))
            return cached;

        // Query database
        var results = await _context.Abbreviations
            .Where(a => a.ExpandedForm.ToLower() == normalized.ToLower())
            .ToListAsync();

        return results.Count > 0 ? results : null;
    }

    public async Task<(List<string> terms, List<Abbreviation> abbreviations)> GetRelatedTermsAsync(string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return (new(), new());

        var normalized = searchTerm.Trim();
        var relatedTerms = new List<string> { normalized };
        var abbreviations = new List<Abbreviation>();

        // Try both directions
        var byShort = await LookupByShortFormAsync(normalized);
        var byExpanded = await LookupByExpandedFormAsync(normalized);

        if (byShort != null)
        {
            abbreviations.AddRange(byShort);
            relatedTerms.AddRange(byShort.Select(a => a.ExpandedForm).Distinct());
        }

        if (byExpanded != null)
        {
            abbreviations.AddRange(byExpanded);
            relatedTerms.AddRange(byExpanded.Select(a => a.ShortForm).Distinct());
        }

        return (relatedTerms.Distinct().ToList(), abbreviations.Distinct().ToList());
    }

    public async Task RefreshCacheAsync()
    {
        try
        {
            _logger.LogInformation("Refreshing abbreviation cache from database...");

            var allAbbreviations = await _context.Abbreviations.ToListAsync();

            _shortFormCache.Clear();
            _expandedFormCache.Clear();

            foreach (var abbr in allAbbreviations)
            {
                var shortKey = abbr.ShortForm.ToLower();
                var expandKey = abbr.ExpandedForm.ToLower();

                _shortFormCache.AddOrUpdate(shortKey, 
                    new List<Abbreviation> { abbr },
                    (key, existing) => { existing.Add(abbr); return existing; });

                _expandedFormCache.AddOrUpdate(expandKey,
                    new List<Abbreviation> { abbr },
                    (key, existing) => { existing.Add(abbr); return existing; });
            }

            _logger.LogInformation("Abbreviation cache refreshed. Total entries: {Count}", allAbbreviations.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh abbreviation cache");
        }
    }

    public async Task<int> SyncFromDailyMedAsync(CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Starting abbreviation sync (openFDA + DailyMed + built-in)...");

            var allAbbreviations = new Dictionary<(string shortForm, string expandedForm), Abbreviation>();

            // First, fetch from openFDA API (configurable)
            if (_configuration.GetValue<bool?>("AbbreviationSync:OpenFda:Enabled") ?? true)
            {
                _logger.LogInformation("Fetching drug label data from openFDA...");
                var fda = await FetchFromOpenFdaAsync(ct);
                foreach (var abbr in fda)
                {
                    var key = (abbr.ShortForm.ToLowerInvariant(), abbr.ExpandedForm.ToLowerInvariant());
                    if (!allAbbreviations.ContainsKey(key))
                        allAbbreviations[key] = abbr;
                }
            }
            else
            {
                _logger.LogInformation("openFDA sync disabled by configuration.");
            }

            // Then fetch from DailyMed bulk (configurable)
            if (_configuration.GetValue<bool?>("AbbreviationSync:DailyMed:Enabled") ?? false)
            {
                _logger.LogInformation("Fetching drug label data from DailyMed (bulk)...");
                var daily = await FetchFromDailyMedAsync(ct);
                foreach (var abbr in daily)
                {
                    var key = (abbr.ShortForm.ToLowerInvariant(), abbr.ExpandedForm.ToLowerInvariant());
                    if (!allAbbreviations.ContainsKey(key))
                        allAbbreviations[key] = abbr;
                }
            }
            else
            {
                _logger.LogInformation("DailyMed sync disabled by configuration.");
            }

            // Add built-in abbreviations (foundational set that won't change)
            var builtIn = GetBuiltInAbbreviations();
            foreach (var abbr in builtIn)
            {
                var key = (abbr.ShortForm.ToLowerInvariant(), abbr.ExpandedForm.ToLowerInvariant());
                if (!allAbbreviations.ContainsKey(key))
                    allAbbreviations[key] = abbr;
            }

            _logger.LogInformation("Processing {Count} abbreviations for database...", allAbbreviations.Count);

            int addedCount = 0;
            foreach (var abbr in allAbbreviations.Values)
            {
                // Check if already exists
                var existing = await _context.Abbreviations
                    .FirstOrDefaultAsync(a => 
                        a.ShortForm.ToLower() == abbr.ShortForm.ToLower() &&
                        a.ExpandedForm.ToLower() == abbr.ExpandedForm.ToLower(),
                        ct);

                if (existing == null)
                {
                    _context.Abbreviations.Add(abbr);
                    addedCount++;
                }
                else
                {
                    existing.UpdatedAt = DateTime.UtcNow;
                    existing.Source = abbr.Source;
                }
            }

            await _context.SaveChangesAsync(ct);
            _logger.LogInformation("FDA/DailyMed sync complete. Added/updated {Count} abbreviations", addedCount);

            // Refresh cache after sync
            await RefreshCacheAsync();

            return addedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync abbreviations from FDA/DailyMed");
            return 0;
        }
    }

    private async Task<List<Abbreviation>> FetchFromOpenFdaAsync(CancellationToken ct)
    {
        var abbreviations = new List<Abbreviation>();

        try
        {
            var baseUrl = _configuration["AbbreviationSync:OpenFda:BaseUrl"];
            if (string.IsNullOrWhiteSpace(baseUrl))
                baseUrl = DefaultOpenFdaUrl;

            var limit = _configuration.GetValue<int?>("AbbreviationSync:OpenFda:Limit") ?? 100;
            if (limit < 1) limit = 1;
            if (limit > 1000) limit = 1000;

            // Query openFDA for recent drug labels with configurable limit
            var apiUrl = $"{baseUrl}?limit={limit}&sort=effective_time:desc";
            _logger.LogInformation("Fetching drug labels from openFDA: {Url}", apiUrl);

            using var response = await _httpClient.GetAsync(apiUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("openFDA API returned {StatusCode}: {Content}", response.StatusCode, await response.Content.ReadAsStringAsync(ct));
                return abbreviations;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = System.Text.Json.JsonDocument.Parse(json);

            var results = doc.RootElement.GetProperty("results");
            foreach (var result in results.EnumerateArray())
            {
                try
                {
                    // Extract abbreviations from various SPL sections
                    var abbrsFromSpl = ExtractAbbreviationsFromSpl(result);
                    abbreviations.AddRange(abbrsFromSpl);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to parse abbreviations from single SPL entry");
                    continue;
                }
            }

            _logger.LogInformation("Extracted {Count} abbreviations from openFDA", abbreviations.Count);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to reach openFDA API (network issue)");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse openFDA response");
        }

        return abbreviations;
    }

    private async Task<List<Abbreviation>> FetchFromDailyMedAsync(CancellationToken ct)
    {
        var abbreviations = new List<Abbreviation>();

        try
        {
            var url = _configuration["AbbreviationSync:DailyMed:BulkUrl"];
            if (string.IsNullOrWhiteSpace(url))
            {
                _logger.LogInformation("No DailyMed bulk URL configured (AbbreviationSync:DailyMed:BulkUrl); skipping DailyMed fetch.");
                return abbreviations;
            }

            var maxXmlFiles = _configuration.GetValue<int?>("AbbreviationSync:DailyMed:MaxXmlFilesPerSync") ?? 200;
            if (maxXmlFiles < 1) maxXmlFiles = 1;

            var xmlProcessed = 0;

            _logger.LogInformation("Downloading DailyMed bulk from {Url}", url);

            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("DailyMed bulk download failed {Status}: {Content}", response.StatusCode, await response.Content.ReadAsStringAsync(ct));
                return abbreviations;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);

            // Expect a ZIP archive containing SPL XML files. If not a zip, attempt to parse single XML stream.
            try
            {
                using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
                foreach (var entry in zip.Entries)
                {
                    if (ct.IsCancellationRequested) break;
                    if (xmlProcessed >= maxXmlFiles) break;

                    if (!entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                        continue;

                    try
                    {
                        using var es = entry.Open();
                        var doc = XDocument.Load(es);
                        var text = string.Join(" ", doc.Descendants().Where(e => !e.HasElements).Select(e => e.Value));
                        var found = ExtractAbbreviationsFromText(text, "DailyMed");
                        abbreviations.AddRange(found);
                        xmlProcessed++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to parse DailyMed entry {Entry}", entry.FullName);
                    }
                }
            }
            catch (InvalidDataException)
            {
                // Not a zip — attempt to parse as a single XML document
                try
                {
                    stream.Seek(0, SeekOrigin.Begin);
                    var doc = XDocument.Load(stream);
                    var text = string.Join(" ", doc.Descendants().Where(e => !e.HasElements).Select(e => e.Value));
                    var found = ExtractAbbreviationsFromText(text, "DailyMed");
                    abbreviations.AddRange(found);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse DailyMed bulk as XML");
                }
            }

            _logger.LogInformation("Extracted {Count} abbreviations from DailyMed bulk", abbreviations.Count);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to reach DailyMed bulk URL (network issue)");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse DailyMed bulk response");
        }

        return abbreviations;
    }

    private List<Abbreviation> ExtractAbbreviationsFromText(string text, string source)
    {
        var abbreviations = new List<Abbreviation>();

        if (string.IsNullOrWhiteSpace(text)) return abbreviations;

        var abbrThenExpanded = new Regex(@"\b(?<abbr>[A-Z0-9\-]{2,10})\s*\((?<expanded>[^)]{3,200})\)", RegexOptions.IgnoreCase);
        var expandedThenAbbr = new Regex(@"(?<expanded>[A-Za-z][A-Za-z\s,\-]{3,200})\s*\((?<abbr>[A-Z0-9\-]{2,10})\)", RegexOptions.IgnoreCase);

        foreach (var match in abbrThenExpanded.Matches(text).Cast<Match>())
        {
            TryAddExtractedPair(abbreviations, match.Groups["abbr"].Value, match.Groups["expanded"].Value, source);
        }

        foreach (var match in expandedThenAbbr.Matches(text).Cast<Match>())
        {
            TryAddExtractedPair(abbreviations, match.Groups["abbr"].Value, match.Groups["expanded"].Value, source);
        }

        return abbreviations;
    }

    private static void TryAddExtractedPair(List<Abbreviation> abbreviations, string shortFormRaw, string expandedFormRaw, string source)
    {
        try
        {
            var shortForm = shortFormRaw.Trim();
            var expandedForm = expandedFormRaw.Trim();

            if (shortForm.Length < 2 || shortForm.Length > 40) return;
            if (expandedForm.Length < 3 || expandedForm.Length > 500) return;

            if (!IsLikelyUsefulAbbreviation(shortForm, expandedForm)) return;

            if (!IsDuplicate(abbreviations, shortForm, expandedForm))
            {
                abbreviations.Add(new Abbreviation
                {
                    Id = Guid.NewGuid(),
                    ShortForm = shortForm,
                    ExpandedForm = expandedForm,
                    Category = null,
                    Source = source,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }
        }
        catch
        {
            // Ignore individual extraction errors.
        }
    }

    private static bool IsLikelyUsefulAbbreviation(string shortForm, string expandedForm)
    {
        if (shortForm.Contains(' ')) return false;
        if (expandedForm.IndexOf(shortForm, StringComparison.OrdinalIgnoreCase) >= 0) return false;

        // Ignore obvious section numbering and noise tokens
        if (Regex.IsMatch(shortForm, @"^\d+[A-Z]?$")) return false;
        if (Regex.IsMatch(shortForm, @"^(AND|THE|FOR|WITH|FROM)$", RegexOptions.IgnoreCase)) return false;

        return true;
    }

    private List<Abbreviation> ExtractAbbreviationsFromSpl(System.Text.Json.JsonElement spl)
    {
        var abbreviations = new List<Abbreviation>();

        // Common sections in SPL that contain abbreviations
        var sections = new[] 
        { 
            "indications_and_usage", 
            "dosage_and_administration",
            "contraindications",
            "warnings",
            "precautions",
            "adverse_reactions",
            "interactions"
        };

        foreach (var section in sections)
        {
            if (spl.TryGetProperty(section, out var sectionText))
            {
                var text = sectionText.GetString() ?? string.Empty;
                
                // Look for common patterns: "XX (Expanded Form)" or "Expanded Form (XX)"
                var patterns = new[]
                {
                    // Pattern: "ABBR (Expanded Form)"
                    new System.Text.RegularExpressions.Regex(@"\b([A-Z]+)\s*\(([^)]{10,100})\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
                    // Pattern: "Expanded Form (ABBR)" 
                    new System.Text.RegularExpressions.Regex(@"\(([A-Z]+)\)\s+([A-Za-z\s]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                };

                foreach (var pattern in patterns)
                {
                    var matches = pattern.Matches(text);
                    foreach (System.Text.RegularExpressions.Match match in matches)
                    {
                        if (match.Groups.Count >= 3)
                        {
                            var shortForm = match.Groups[1].Value.Trim();
                            var expandedForm = match.Groups[2].Value.Trim();

                            // Filter: abbreviations should be 2-10 chars, expanded should be 5-200 chars
                            if (shortForm.Length >= 2 && shortForm.Length <= 10 &&
                                expandedForm.Length >= 5 && expandedForm.Length <= 200 &&
                                !IsDuplicate(abbreviations, shortForm, expandedForm))
                            {
                                abbreviations.Add(new Abbreviation
                                {
                                    Id = Guid.NewGuid(),
                                    ShortForm = shortForm,
                                    ExpandedForm = expandedForm,
                                    Category = section.Replace("_", " "),
                                    Source = "openFDA",
                                    CreatedAt = DateTime.UtcNow,
                                    UpdatedAt = DateTime.UtcNow
                                });
                            }
                        }
                    }
                }
            }
        }

        return abbreviations;
    }

    private static bool IsDuplicate(List<Abbreviation> list, string shortForm, string expandedForm)
    {
        return list.Any(a =>
            a.ShortForm.Equals(shortForm, StringComparison.OrdinalIgnoreCase) &&
            a.ExpandedForm.Equals(expandedForm, StringComparison.OrdinalIgnoreCase));
    }

    private List<Abbreviation> GetBuiltInAbbreviations()
    {
        return new()
        {
            // Dosage frequency
            new() { ShortForm = "QD", ExpandedForm = "once daily", Category = "frequency", Source = "built-in" },
            new() { ShortForm = "BID", ExpandedForm = "twice daily", Category = "frequency", Source = "built-in" },
            new() { ShortForm = "TID", ExpandedForm = "three times daily", Category = "frequency", Source = "built-in" },
            new() { ShortForm = "QID", ExpandedForm = "four times daily", Category = "frequency", Source = "built-in" },
            new() { ShortForm = "Q4H", ExpandedForm = "every 4 hours", Category = "frequency", Source = "built-in" },

            // Routes of administration
            new() { ShortForm = "IV", ExpandedForm = "intravenous", Category = "route", Source = "built-in" },
            new() { ShortForm = "IM", ExpandedForm = "intramuscular", Category = "route", Source = "built-in" },
            new() { ShortForm = "PO", ExpandedForm = "by mouth", Category = "route", Source = "built-in" },
            new() { ShortForm = "SC", ExpandedForm = "subcutaneous", Category = "route", Source = "built-in" },
            new() { ShortForm = "SQ", ExpandedForm = "subcutaneous", Category = "route", Source = "built-in" },
            new() { ShortForm = "PR", ExpandedForm = "rectal", Category = "route", Source = "built-in" },

            // Common medical abbreviations
            new() { ShortForm = "ACE", ExpandedForm = "Angiotensin Converting Enzyme", Category = "medical_term", Source = "built-in" },
            new() { ShortForm = "BP", ExpandedForm = "blood pressure", Category = "medical_term", Source = "built-in" },
            new() { ShortForm = "HR", ExpandedForm = "heart rate", Category = "medical_term", Source = "built-in" },
            new() { ShortForm = "RR", ExpandedForm = "respiratory rate", Category = "medical_term", Source = "built-in" },
            new() { ShortForm = "GI", ExpandedForm = "gastrointestinal", Category = "medical_term", Source = "built-in" },
            new() { ShortForm = "CNS", ExpandedForm = "central nervous system", Category = "medical_term", Source = "built-in" },
            new() { ShortForm = "COPD", ExpandedForm = "chronic obstructive pulmonary disease", Category = "medical_term", Source = "built-in" },
            new() { ShortForm = "DM", ExpandedForm = "diabetes mellitus", Category = "medical_term", Source = "built-in" },
            new() { ShortForm = "HTN", ExpandedForm = "hypertension", Category = "medical_term", Source = "built-in" },
            new() { ShortForm = "MI", ExpandedForm = "myocardial infarction", Category = "medical_term", Source = "built-in" },
            new() { ShortForm = "CAD", ExpandedForm = "coronary artery disease", Category = "medical_term", Source = "built-in" },
            new() { ShortForm = "CHF", ExpandedForm = "congestive heart failure", Category = "medical_term", Source = "built-in" },

            // Lab values
            new() { ShortForm = "CBC", ExpandedForm = "complete blood count", Category = "lab", Source = "built-in" },
            new() { ShortForm = "CMP", ExpandedForm = "comprehensive metabolic panel", Category = "lab", Source = "built-in" },
            new() { ShortForm = "LFT", ExpandedForm = "liver function tests", Category = "lab", Source = "built-in" },
            new() { ShortForm = "BUN", ExpandedForm = "blood urea nitrogen", Category = "lab", Source = "built-in" },
            new() { ShortForm = "Cr", ExpandedForm = "creatinine", Category = "lab", Source = "built-in" },
            new() { ShortForm = "eGFR", ExpandedForm = "estimated glomerular filtration rate", Category = "lab", Source = "built-in" },

            // Common drugs
            new() { ShortForm = "NSAID", ExpandedForm = "nonsteroidal anti-inflammatory drug", Category = "drug", Source = "built-in" },
            new() { ShortForm = "SSRI", ExpandedForm = "selective serotonin reuptake inhibitor", Category = "drug", Source = "built-in" },
            new() { ShortForm = "ACE inhibitor", ExpandedForm = "angiotensin-converting enzyme inhibitor", Category = "drug", Source = "built-in" },
            new() { ShortForm = "ARB", ExpandedForm = "angiotensin receptor blocker", Category = "drug", Source = "built-in" },
            new() { ShortForm = "PPI", ExpandedForm = "proton pump inhibitor", Category = "drug", Source = "built-in" },
            new() { ShortForm = "TNF", ExpandedForm = "tumor necrosis factor", Category = "drug", Source = "built-in" },

            // Time/units
            new() { ShortForm = "mg", ExpandedForm = "milligram", Category = "unit", Source = "built-in" },
            new() { ShortForm = "mL", ExpandedForm = "milliliter", Category = "unit", Source = "built-in" },
            new() { ShortForm = "mcg", ExpandedForm = "microgram", Category = "unit", Source = "built-in" },
            new() { ShortForm = "g", ExpandedForm = "gram", Category = "unit", Source = "built-in" },
        };
    }
}
