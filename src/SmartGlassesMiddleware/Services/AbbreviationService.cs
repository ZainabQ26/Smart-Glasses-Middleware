using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO.Compression;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Services;

/// <summary>
/// Implements abbreviation lookup with in-memory caching and optional syncing from FDA sources.
/// </summary>
public class AbbreviationService : IAbbreviationService
{
    private const string DefaultOpenFdaUrl = "https://api.fda.gov/drug/label.json";
    private const string DefaultRxNavAllConceptsUrl = "https://rxnav.nlm.nih.gov/REST/allconcepts.json";
    private const string DefaultRxTermsSearchUrl = "https://clinicaltables.nlm.nih.gov/api/rxterms/v3/search";
    private const string DefaultRxTermsSeedTerms = "a,b,c,d,e,f,g,h,i,j,k,l,m,n,o,p,q,r,s,t,u,v,w,x,y,z,0,1,2,3,4,5,6,7,8,9";
    private readonly ApplicationDbContext _context;
    private readonly ILogger<AbbreviationService> _logger;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    private static readonly Dictionary<string, string> KnownMedicationAbbreviations =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["ER"] = "extended release",
            ["XR"] = "extended release",
            ["SR"] = "sustained release",
            ["CR"] = "controlled release",
            ["DR"] = "delayed release",
            ["IR"] = "immediate release",
            ["EC"] = "enteric coated",
            ["ODT"] = "orally disintegrating tablet",
            ["HCl"] = "hydrochloride",
            ["HBr"] = "hydrobromide",
            ["Na"] = "sodium",
            ["K"] = "potassium",
            ["Ca"] = "calcium",
            ["MG"] = "milligram",
            ["MCG"] = "microgram",
            ["ML"] = "milliliter",
            ["IU"] = "international units",
            ["MEQ"] = "milliequivalent",
            ["TAB"] = "tablet",
            ["TABS"] = "tablets",
            ["CAP"] = "capsule",
            ["CAPS"] = "capsules",
            ["SOLN"] = "solution",
            ["SUSP"] = "suspension",
            ["SYR"] = "syrup",
            ["INJ"] = "injection",
            ["OINT"] = "ointment",
            ["CRM"] = "cream",
            ["CREAM"] = "cream",
            ["GEL"] = "gel",
            ["PKT"] = "packet",
            ["PACK"] = "packet"
        };

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

        var normalized = shortForm.Trim(); // Normalizing short form

        // Check cache first
        if (_shortFormCache.TryGetValue(normalized, out var cached))
            return cached;

        try
        {
            // Query database
            var results = await _context.Abbreviations
                .Where(a => a.ShortForm.ToLower() == normalized.ToLower())
                .ToListAsync();

            if (results.Count > 0)
            {
                _shortFormCache[normalized] = results;
                return results;
            }
        }
        catch (Exception ex) when (IsMissingAbbreviationsTableException(ex))
        {
            _logger.LogWarning(ex,
                "Abbreviations table is not available yet. Using built-in short-form fallback for '{ShortForm}'.",
                normalized);
        }

        var fallback = GetBuiltInAbbreviations()
            .Where(a => a.ShortForm.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (fallback.Count > 0)
        {
            _shortFormCache[normalized] = fallback;
            return fallback;
        }

        return null;
    }

    public async Task<List<Abbreviation>?> LookupByExpandedFormAsync(string expandedForm)
    {
        if (string.IsNullOrWhiteSpace(expandedForm))
            return null;

        var normalized = expandedForm.Trim(); // Normalizing expanded form

        // Check cache first
        if (_expandedFormCache.TryGetValue(normalized, out var cached))
            return cached;

        try
        {
            // Query database
            var results = await _context.Abbreviations
                .Where(a => a.ExpandedForm.ToLower() == normalized.ToLower())
                .ToListAsync();

            if (results.Count > 0)
            {
                _expandedFormCache[normalized] = results;
                return results;
            }
        }
        catch (Exception ex) when (IsMissingAbbreviationsTableException(ex))
        {
            _logger.LogWarning(ex,
                "Abbreviations table is not available yet. Using built-in expanded-form fallback for '{ExpandedForm}'.",
                normalized);
        }

        var fallback = GetBuiltInAbbreviations()
            .Where(a => a.ExpandedForm.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (fallback.Count > 0)
        {
            _expandedFormCache[normalized] = fallback;
            return fallback;
        }

        return null;
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
        catch (Exception ex) when (IsMissingAbbreviationsTableException(ex))
        {
            _logger.LogWarning(ex, "Abbreviations table is not available yet. Seeding cache with built-in abbreviations.");
            SeedCacheWithBuiltIns();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh abbreviation cache");
            SeedCacheWithBuiltIns();
        }
    }

    public async Task<int> SyncFromDailyMedAsync(CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Starting abbreviation sync (openFDA + DailyMed + RxNav + RxTerms + built-in)...");

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

            // Then fetch from RxNav all-concepts feed (free, no license)
            if (_configuration.GetValue<bool?>("AbbreviationSync:RxNav:Enabled") ?? true)
            {
                _logger.LogInformation("Fetching drug concept data from RxNav...");
                var rxnav = await FetchFromRxNavAsync(ct);
                foreach (var abbr in rxnav)
                {
                    var key = (abbr.ShortForm.ToLowerInvariant(), abbr.ExpandedForm.ToLowerInvariant());
                    if (!allAbbreviations.ContainsKey(key))
                        allAbbreviations[key] = abbr;
                }
            }
            else
            {
                _logger.LogInformation("RxNav sync disabled by configuration.");
            }

            // Then fetch from NLM Clinical Tables RxTerms search API (free, no license)
            if (_configuration.GetValue<bool?>("AbbreviationSync:RxTerms:Enabled") ?? true)
            {
                _logger.LogInformation("Fetching medication terminology data from RxTerms...");
                var rxTerms = await FetchFromRxTermsAsync(ct);
                foreach (var abbr in rxTerms)
                {
                    var key = (abbr.ShortForm.ToLowerInvariant(), abbr.ExpandedForm.ToLowerInvariant());
                    if (!allAbbreviations.ContainsKey(key))
                        allAbbreviations[key] = abbr;
                }
            }
            else
            {
                _logger.LogInformation("RxTerms sync disabled by configuration.");
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

            // Pull in pages so a single sync can process much more than one response page.
            var maxRecords = _configuration.GetValue<int?>("AbbreviationSync:OpenFda:MaxRecords") ?? 0;
            var unlimited = maxRecords <= 0;
            if (!unlimited && maxRecords < limit) maxRecords = limit;

            var skip = 0;
            var processedRecords = 0;

            while (unlimited || processedRecords < maxRecords)
            {
                var pageSize = unlimited ? limit : Math.Min(limit, maxRecords - processedRecords);
                var apiUrl = $"{baseUrl}?limit={pageSize}&skip={skip}&sort=effective_time:desc";
                _logger.LogInformation("Fetching drug labels from openFDA: {Url}", apiUrl);

                using var response = await _httpClient.GetAsync(apiUrl, ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("openFDA API returned {StatusCode}: {Content}", response.StatusCode, await response.Content.ReadAsStringAsync(ct));
                    return abbreviations;
                }

                var json = await response.Content.ReadAsStringAsync(ct);
                using var doc = System.Text.Json.JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                {
                    break;
                }

                var pageCount = 0;
                foreach (var result in results.EnumerateArray())
                {
                    pageCount++;
                    try
                    {
                        // Extract abbreviations from various SPL sections
                        var abbrsFromSpl = ExtractAbbreviationsFromSpl(result);
                        abbreviations.AddRange(abbrsFromSpl);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to parse abbreviations from single SPL entry");
                    }
                }

                if (pageCount == 0)
                {
                    break;
                }

                processedRecords += pageCount;
                skip += pageCount;

                // Last page reached.
                if (pageCount < pageSize)
                {
                    break;
                }
            }

            _logger.LogInformation("Extracted {Count} abbreviations from openFDA across {ProcessedRecords} records", abbreviations.Count, processedRecords);
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

            var maxXmlFiles = _configuration.GetValue<int?>("AbbreviationSync:DailyMed:MaxXmlFilesPerSync") ?? 0;

            var xmlProcessed = 0;

            _logger.LogInformation("Downloading DailyMed bulk from {Url}", url);

            if (url.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                return await FetchFromDailyMedJsonIndexAsync(url, maxXmlFiles, ct);
            }

            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("DailyMed bulk download failed {Status}: {Content}", response.StatusCode, await response.Content.ReadAsStringAsync(ct));
                return abbreviations;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                var tmpUrl = response.RequestMessage?.RequestUri?.ToString() ?? url;
                return await FetchFromDailyMedJsonIndexAsync(tmpUrl, maxXmlFiles, ct, preloadedJson: body);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);

            // Expect a ZIP archive containing SPL XML files. If not a zip, attempt to parse single XML stream.
            try
            {
                using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
                foreach (var entry in zip.Entries)
                {
                    if (ct.IsCancellationRequested) break;
                    if (maxXmlFiles > 0 && xmlProcessed >= maxXmlFiles) break;

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
                // Not a zip; newer DailyMed endpoints provide JSON index data.
                if (url.Contains("spls.zip", StringComparison.OrdinalIgnoreCase))
                {
                    var jsonIndexUrl = url.Replace("spls.zip", "spls.json", StringComparison.OrdinalIgnoreCase);
                    return await FetchFromDailyMedJsonIndexAsync(jsonIndexUrl, maxXmlFiles, ct);
                }

                // Attempt parse as single XML stream only when URL points directly to an XML SPL.
                if (url.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var doc = XDocument.Load(stream);
                        var text = string.Join(" ", doc.Descendants().Where(e => !e.HasElements).Select(e => e.Value));
                        var found = ExtractAbbreviationsFromText(text, "DailyMed");
                        abbreviations.AddRange(found);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse DailyMed XML stream");
                    }
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

    private async Task<List<Abbreviation>> FetchFromDailyMedJsonIndexAsync(string indexUrl, int maxXmlFiles, CancellationToken ct, string? preloadedJson = null)
    {
        var abbreviations = new List<Abbreviation>();
        var xmlProcessed = 0;
        string? nextUrl = indexUrl;
        var firstPageJson = preloadedJson;

        while (!string.IsNullOrWhiteSpace(nextUrl) && (maxXmlFiles <= 0 || xmlProcessed < maxXmlFiles))
        {
            string json;
            if (firstPageJson != null)
            {
                json = firstPageJson;
                firstPageJson = null;
            }
            else
            {
                using var response = await _httpClient.GetAsync(nextUrl, ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("DailyMed index request failed {Status}: {Url}", response.StatusCode, nextUrl);
                    break;
                }

                json = await response.Content.ReadAsStringAsync(ct);
            }

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                break;

            foreach (var item in data.EnumerateArray())
            {
                if (maxXmlFiles > 0 && xmlProcessed >= maxXmlFiles)
                    break;

                if (!item.TryGetProperty("setid", out var setidElement) || setidElement.ValueKind != JsonValueKind.String)
                    continue;

                var setid = setidElement.GetString();
                if (string.IsNullOrWhiteSpace(setid))
                    continue;

                var xmlUrl = $"https://dailymed.nlm.nih.gov/dailymed/services/v2/spls/{setid}.xml";
                try
                {
                    using var xmlResponse = await _httpClient.GetAsync(xmlUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (!xmlResponse.IsSuccessStatusCode)
                    {
                        continue;
                    }

                    await using var xmlStream = await xmlResponse.Content.ReadAsStreamAsync(ct);
                    var xdoc = XDocument.Load(xmlStream);
                    var text = string.Join(" ", xdoc.Descendants().Where(e => !e.HasElements).Select(e => e.Value));
                    abbreviations.AddRange(ExtractAbbreviationsFromText(text, "DailyMed"));
                    xmlProcessed++;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to parse DailyMed SPL xml for setid {SetId}", setid);
                }
            }

            nextUrl = null;
            if (doc.RootElement.TryGetProperty("metadata", out var metadata) &&
                metadata.TryGetProperty("next_page_url", out var nextPageUrl) &&
                nextPageUrl.ValueKind == JsonValueKind.String)
            {
                var candidate = nextPageUrl.GetString();
                if (!string.IsNullOrWhiteSpace(candidate) &&
                    !candidate.Equals("null", StringComparison.OrdinalIgnoreCase))
                {
                    nextUrl = candidate;
                }
            }
        }

        _logger.LogInformation("Extracted {Count} abbreviations from DailyMed JSON/XML flow across {Processed} SPL XML files", abbreviations.Count, xmlProcessed);
        return abbreviations;
    }

    private async Task<List<Abbreviation>> FetchFromRxNavAsync(CancellationToken ct)
    {
        var abbreviations = new List<Abbreviation>();

        try
        {
            var baseUrl = _configuration["AbbreviationSync:RxNav:BaseUrl"];
            if (string.IsNullOrWhiteSpace(baseUrl))
                baseUrl = DefaultRxNavAllConceptsUrl;

            var conceptTypes = _configuration["AbbreviationSync:RxNav:ConceptTypes"] ?? "IN+BN+PIN+SCD+SBD+GPCK+BPCK";
            var maxConcepts = _configuration.GetValue<int?>("AbbreviationSync:RxNav:MaxConcepts") ?? 0;
            var unlimited = maxConcepts <= 0;

            var escapedConceptTypes = Uri.EscapeDataString(conceptTypes).Replace("%2B", "+", StringComparison.OrdinalIgnoreCase);
            var apiUrl = $"{baseUrl}?tty={escapedConceptTypes}";
            using var response = await _httpClient.GetAsync(apiUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("RxNav API returned {StatusCode}: {Content}", response.StatusCode, await response.Content.ReadAsStringAsync(ct));
                return abbreviations;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            var processed = 0;
            if (doc.RootElement.TryGetProperty("minConceptGroup", out var minConceptGroup))
            {
                // allconcepts may return either:
                // 1) minConceptGroup.minConcept[]
                // 2) minConceptGroup.conceptGroup[].conceptProperties[]
                if (minConceptGroup.TryGetProperty("minConcept", out var minConcepts) && minConcepts.ValueKind == JsonValueKind.Array)
                {
                    foreach (var concept in minConcepts.EnumerateArray())
                    {
                        if (!unlimited && processed >= maxConcepts)
                            break;

                        if (concept.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                        {
                            var value = name.GetString() ?? string.Empty;
                            abbreviations.AddRange(ExtractAbbreviationsFromText(value, "RxNav"));
                            abbreviations.AddRange(ExtractAbbreviationsFromMedicationName(value, "RxNav"));
                        }

                        if (concept.TryGetProperty("synonym", out var synonym) && synonym.ValueKind == JsonValueKind.String)
                        {
                            var value = synonym.GetString() ?? string.Empty;
                            abbreviations.AddRange(ExtractAbbreviationsFromText(value, "RxNav"));
                            abbreviations.AddRange(ExtractAbbreviationsFromMedicationName(value, "RxNav"));
                        }

                        processed++;
                    }
                }

                if ((unlimited || processed < maxConcepts) &&
                    minConceptGroup.TryGetProperty("conceptGroup", out var groups) &&
                    groups.ValueKind == JsonValueKind.Array)
                {
                    foreach (var group in groups.EnumerateArray())
                    {
                        if (!unlimited && processed >= maxConcepts)
                            break;

                        if (!group.TryGetProperty("conceptProperties", out var concepts) || concepts.ValueKind != JsonValueKind.Array)
                            continue;

                        foreach (var concept in concepts.EnumerateArray())
                        {
                            if (!unlimited && processed >= maxConcepts)
                                break;

                            if (concept.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                            {
                                var value = name.GetString() ?? string.Empty;
                                abbreviations.AddRange(ExtractAbbreviationsFromText(value, "RxNav"));
                                abbreviations.AddRange(ExtractAbbreviationsFromMedicationName(value, "RxNav"));
                            }

                            if (concept.TryGetProperty("synonym", out var synonym) && synonym.ValueKind == JsonValueKind.String)
                            {
                                var value = synonym.GetString() ?? string.Empty;
                                abbreviations.AddRange(ExtractAbbreviationsFromText(value, "RxNav"));
                                abbreviations.AddRange(ExtractAbbreviationsFromMedicationName(value, "RxNav"));
                            }

                            processed++;
                        }
                    }
                }
            }

            _logger.LogInformation("Extracted {Count} abbreviations from RxNav across {Processed} concepts", abbreviations.Count, processed);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to reach RxNav API (network issue)");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse RxNav response");
        }

        return abbreviations;
    }

    private async Task<List<Abbreviation>> FetchFromRxTermsAsync(CancellationToken ct)
    {
        var abbreviations = new List<Abbreviation>();

        try
        {
            var baseUrl = _configuration["AbbreviationSync:RxTerms:BaseUrl"];
            if (string.IsNullOrWhiteSpace(baseUrl))
                baseUrl = DefaultRxTermsSearchUrl;

            var queryTermsConfig = _configuration["AbbreviationSync:RxTerms:SeedTerms"] ?? DefaultRxTermsSeedTerms;
            var queryTerms = queryTermsConfig
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (queryTerms.Count == 0)
            {
                queryTerms.Add("a");
            }

            var count = _configuration.GetValue<int?>("AbbreviationSync:RxTerms:PageSize") ?? 500;
            var maxRecords = _configuration.GetValue<int?>("AbbreviationSync:RxTerms:MaxRecords") ?? 0;
            var unlimited = maxRecords <= 0;

            if (count < 1) count = 1;
            if (count > 500) count = 500;

            foreach (var queryTerm in queryTerms)
            {
                var offset = 0;
                while (unlimited || offset < maxRecords)
                {
                    var pageSize = unlimited ? count : Math.Min(count, maxRecords - offset);
                    var apiUrl = $"{baseUrl}?terms={Uri.EscapeDataString(queryTerm)}&offset={offset}&count={pageSize}";

                    using var response = await _httpClient.GetAsync(apiUrl, ct);
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("RxTerms API returned {StatusCode}: {Content}", response.StatusCode, await response.Content.ReadAsStringAsync(ct));
                        break;
                    }

                    var json = await response.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() < 4)
                    {
                        break;
                    }

                    // Response shape: [total, [codes], {extra?}, [display names]]
                    var displayArray = doc.RootElement[3];
                    if (displayArray.ValueKind != JsonValueKind.Array)
                    {
                        break;
                    }

                    var pageCount = 0;
                    foreach (var display in displayArray.EnumerateArray())
                    {
                        if (display.ValueKind != JsonValueKind.String)
                            continue;

                        var value = display.GetString() ?? string.Empty;
                        abbreviations.AddRange(ExtractAbbreviationsFromText(value, "RxTerms"));
                        abbreviations.AddRange(ExtractAbbreviationsFromMedicationName(value, "RxTerms"));
                        abbreviations.AddRange(ExtractRxTermsStructuredAbbreviations(value));
                        pageCount++;
                    }

                    if (pageCount == 0)
                    {
                        break;
                    }

                    offset += pageCount;
                    if (pageCount < pageSize)
                    {
                        break;
                    }
                }
            }

            _logger.LogInformation("Extracted {Count} abbreviations from RxTerms", abbreviations.Count);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to reach RxTerms API (network issue)");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse RxTerms response");
        }

        return abbreviations;
    }

    private List<Abbreviation> ExtractAbbreviationsFromText(string text, string source)
    {
        var abbreviations = new List<Abbreviation>();

        if (string.IsNullOrWhiteSpace(text)) return abbreviations;

        var abbrThenExpanded = new Regex(@"\b(?<abbr>[A-Z][A-Z0-9\-]{1,11})\s*\((?<expanded>[^)]{3,200})\)");
        var expandedThenAbbr = new Regex(@"(?<expanded>[A-Za-z][A-Za-z\s,\-]{3,200})\s*\((?<abbr>[A-Z][A-Z0-9\-]{1,11})\)");

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

    private List<Abbreviation> ExtractAbbreviationsFromMedicationName(string text, string source)
    {
        var abbreviations = new List<Abbreviation>();

        if (string.IsNullOrWhiteSpace(text))
            return abbreviations;

        // Tokenize simple medication names and capture known shorthand tokens used in Rx data.
        foreach (Match match in Regex.Matches(text, @"\b[A-Za-z][A-Za-z0-9]{0,6}\b"))
        {
            var token = match.Value;
            if (KnownMedicationAbbreviations.TryGetValue(token, out var expanded))
            {
                TryAddExtractedPair(abbreviations, token, expanded, source, "drug name normalization");
            }
        }

        return abbreviations;
    }

    private List<Abbreviation> ExtractRxTermsStructuredAbbreviations(string text)
    {
        var abbreviations = new List<Abbreviation>();

        if (string.IsNullOrWhiteSpace(text))
            return abbreviations;

        // RxTerms display strings often include compact dosage/form tokens without
        // acronym-style parenthetical patterns (e.g. ER, ODT, TAB, SOLN, HCl).
        var tokenPattern = new Regex(@"\b(ER|XR|SR|CR|DR|IR|EC|ODT|HCl|HBr|Na|K|Ca|MG|MCG|ML|IU|MEQ|TABS?|CAPS?|SOLN|SUSP|SYR|INJ|OINT|CRM|CREAM|GEL|PKT|PACK)\b", RegexOptions.IgnoreCase);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in tokenPattern.Matches(text))
        {
            if (!match.Success)
                continue;

            var raw = match.Value.Trim();
            if (!seen.Add(raw))
                continue;

            var normalized = NormalizeRxTermsToken(raw);
            if (KnownMedicationAbbreviations.TryGetValue(normalized, out var expanded))
            {
                TryAddExtractedPair(abbreviations, normalized, expanded, "RxTerms", "drug form normalization");
            }
        }

        return abbreviations;
    }

    private static string NormalizeRxTermsToken(string token)
    {
        var t = token.Trim();
        if (t.Equals("tabs", StringComparison.OrdinalIgnoreCase)) return "TABS";
        if (t.Equals("tab", StringComparison.OrdinalIgnoreCase)) return "TAB";
        if (t.Equals("caps", StringComparison.OrdinalIgnoreCase)) return "CAPS";
        if (t.Equals("cap", StringComparison.OrdinalIgnoreCase)) return "CAP";
        if (t.Equals("cream", StringComparison.OrdinalIgnoreCase)) return "CREAM";
        if (t.Equals("hcl", StringComparison.OrdinalIgnoreCase)) return "HCl";
        if (t.Equals("hbr", StringComparison.OrdinalIgnoreCase)) return "HBr";
        return t.ToUpperInvariant();
    }

    private static void TryAddExtractedPair(List<Abbreviation> abbreviations, string shortFormRaw, string expandedFormRaw, string source, string? category = null)
    {
        try
        {
            var shortForm = shortFormRaw.Trim();
            var expandedForm = NormalizeExpandedForm(expandedFormRaw.Trim(), source);

            if (shortForm.Length < 2 || shortForm.Length > 40) return;
            if (expandedForm.Length < 3 || expandedForm.Length > 500) return;

            if (!IsLikelyUsefulAbbreviation(shortForm, expandedForm, source)) return;

            if (!IsDuplicate(abbreviations, shortForm, expandedForm))
            {
                abbreviations.Add(new Abbreviation
                {
                    Id = Guid.NewGuid(),
                    ShortForm = shortForm,
                    ExpandedForm = expandedForm,
                    Category = category,
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

    private static string NormalizeExpandedForm(string expandedForm, string source)
    {
        if (string.IsNullOrWhiteSpace(expandedForm))
            return expandedForm;

        // Normalize whitespace and common punctuation spacing first.
        var normalized = Regex.Replace(expandedForm, @"\s+", " ").Trim();
        normalized = Regex.Replace(normalized, @"\s+-\s*(?=[A-Za-z])", " ");

        if (!source.Equals("built-in", StringComparison.OrdinalIgnoreCase))
        {
            // Canonical typo/variant cleanup for externally extracted labels.
            normalized = Regex.Replace(normalized, @"\bHypothalmic\b", "Hypothalamic", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\bClostridium\s+difficile\b", "Clostridioides difficile", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\bdrug\s+rash\s+with\s+eosinophilia\s+and\s+systemic\s+symptoms\b", "drug reaction with eosinophilia and systemic symptoms", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\bOpioid-Induced Hyperalgesia and Allodynia\s+Opioid-Induced Hyperalgesia\b", "Opioid-Induced Hyperalgesia and Allodynia", RegexOptions.IgnoreCase);
        }

        return normalized;
    }

    private static bool IsLikelyUsefulAbbreviation(string shortForm, string expandedForm, string source)
    {
        if (IsKnownPharmaceuticalPair(shortForm, expandedForm))
            return true;

        if (shortForm.Contains(' ')) return false;
        if (shortForm.Length < 2 || shortForm.Length > 12) return false;
        if (expandedForm.IndexOf(shortForm, StringComparison.OrdinalIgnoreCase) >= 0) return false;

        // Abbreviations should contain letters and avoid pure numeric/phone fragments.
        var letterCount = shortForm.Count(char.IsLetter);
        var digitCount = shortForm.Count(char.IsDigit);
        if (letterCount < 2) return false;
        if (digitCount > 0 && letterCount <= 1) return false;
        if (Regex.IsMatch(shortForm, @"^\d+$")) return false;
        if (Regex.IsMatch(shortForm, @"^\d{1,4}([\-/]\d{1,4})+$")) return false;
        if (Regex.IsMatch(shortForm, @"^\d{1,3}-\d{3}-\d{4}$")) return false;

        // Ignore lowercase prose words accidentally captured as abbreviations.
        if (shortForm.Length >= 5 && shortForm.All(char.IsLetter) && shortForm.ToLowerInvariant() == shortForm)
            return false;

        // External sources should look like acronym tokens, not prose nouns.
        if (!source.Equals("built-in", StringComparison.OrdinalIgnoreCase))
        {
            if (shortForm.Any(char.IsLower)) return false;
            if (!Regex.IsMatch(shortForm, @"^[A-Z][A-Z0-9\-]{1,11}$")) return false;

            var wordCount = Regex.Matches(expandedForm, @"[A-Za-z0-9]+").Count;
            if (wordCount < 2 || wordCount > 8) return false;
            if (expandedForm.Length > 90) return false;
            if (Regex.IsMatch(expandedForm, @"[;:]")) return false;
            if (Regex.IsMatch(expandedForm, @"\b(is|are|was|were|indicated|including|treated|contains|offered|recommended|patients?)\b", RegexOptions.IgnoreCase))
                return false;
        }

        // Ignore obvious section numbering and noise tokens
        if (Regex.IsMatch(shortForm, @"^\d+[A-Z]?$")) return false;
        if (Regex.IsMatch(shortForm, @"^(AND|THE|FOR|WITH|FROM|OF|SEE|THAN|THEN|THUS|WHEN)$", RegexOptions.IgnoreCase)) return false;

        // Reject noisy expanded forms that look like prose/stat lines instead of definitions.
        if (Regex.IsMatch(expandedForm, @"^(a|an|and|in|or|with|for|from|of|the|see)\b", RegexOptions.IgnoreCase)) return false;
        if (expandedForm.Contains("95%") || expandedForm.Contains("95 percent", StringComparison.OrdinalIgnoreCase)) return false;
        if (expandedForm.Contains("confidence interval", StringComparison.OrdinalIgnoreCase)) return false;
        if (expandedForm.Contains("Poison Control Center", StringComparison.OrdinalIgnoreCase)) return false;

        var expandedLetters = expandedForm.Count(char.IsLetter);
        var expandedDigits = expandedForm.Count(char.IsDigit);
        if (expandedLetters < 3) return false;
        if (expandedDigits > expandedLetters / 2) return false;
        if (expandedForm.Length > 220) return false;

        // For externally extracted rows, require abbreviation and expansion to align.
        if (!source.Equals("built-in", StringComparison.OrdinalIgnoreCase) &&
            !LooksLikeAcronymExpansion(shortForm, expandedForm))
            return false;

        return true;
    }

    private static bool IsKnownPharmaceuticalPair(string shortForm, string expandedForm)
    {
        if (!KnownMedicationAbbreviations.TryGetValue(shortForm, out var expectedExpanded))
            return false;

        return expectedExpanded.Equals(expandedForm, StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeAcronymExpansion(string shortForm, string expandedForm)
    {
        var normalizedShort = new string(shortForm.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (normalizedShort.Length < 2) return false;

        var tokens = Regex.Split(expandedForm, @"[^A-Za-z0-9]+")
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();
        if (tokens.Count < 2 || tokens.Count > 14) return false;

        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "a", "an", "and", "or", "the", "of", "for", "to", "with", "in", "on", "at", "by"
        };

        var initials = new string(tokens
            .Where(t => !stopWords.Contains(t))
            .Select(t => char.ToUpperInvariant(t[0]))
            .ToArray());

        if (initials.Length < 2) return false;

        // Strong prefix check to reject loose letter matches in long prose.
        if (normalizedShort.Length >= 2)
        {
            if (normalizedShort[0] != initials[0] || normalizedShort[1] != initials[1])
                return false;
        }

        // Ordered subsequence match: requires at least 3 matched chars (or full short form when shorter).
        var requiredMatches = Math.Min(normalizedShort.Length, 3);
        var shortIndex = 0;
        foreach (var ch in initials)
        {
            if (shortIndex < normalizedShort.Length && normalizedShort[shortIndex] == ch)
            {
                shortIndex++;
                if (shortIndex >= requiredMatches)
                    return true;
            }
        }

        return false;
    }

    private List<Abbreviation> ExtractAbbreviationsFromSpl(System.Text.Json.JsonElement spl)
    {
        var abbreviations = new List<Abbreviation>();

        foreach (var sectionProperty in spl.EnumerateObject())
        {
            var section = sectionProperty.Name;
            var sectionText = sectionProperty.Value;

            if (sectionText.ValueKind is JsonValueKind.String or JsonValueKind.Array or JsonValueKind.Object)
            {
                foreach (var text in ExtractSectionTexts(sectionText))
                {
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    // Look for common patterns: "XX (Expanded Form)" or "Expanded Form (XX)"
                    var patterns = new[]
                    {
                        // Pattern: "ABBR (Expanded Form)"
                        new System.Text.RegularExpressions.Regex(@"\b([A-Z][A-Z0-9\-]{1,11})\s*\(([^)]{5,200})\)"),
                        // Pattern: "Expanded Form (ABBR)"
                        new System.Text.RegularExpressions.Regex(@"([A-Za-z][A-Za-z\s,\-]{5,200})\s*\(([A-Z][A-Z0-9\-]{1,11})\)")
                    };

                    foreach (var pattern in patterns)
                    {
                        var matches = pattern.Matches(text);
                        foreach (System.Text.RegularExpressions.Match match in matches)
                        {
                            if (match.Groups.Count >= 3)
                            {
                                var shortForm = pattern == patterns[0]
                                    ? match.Groups[1].Value.Trim()
                                    : match.Groups[2].Value.Trim();
                                var expandedForm = pattern == patterns[0]
                                    ? match.Groups[2].Value.Trim()
                                    : match.Groups[1].Value.Trim();

                                if (expandedForm.Length >= 5 && expandedForm.Length <= 200)
                                    TryAddExtractedPair(abbreviations, shortForm, expandedForm, "openFDA", section.Replace("_", " "));
                            }
                        }
                    }
                }
            }
        }

        return abbreviations;
    }

    private static IEnumerable<string> ExtractSectionTexts(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    yield return text;
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                {
                    foreach (var nested in ExtractSectionTexts(item))
                    {
                        yield return nested;
                    }
                }
                break;

            case JsonValueKind.Object:
                foreach (var property in value.EnumerateObject())
                {
                    foreach (var nested in ExtractSectionTexts(property.Value))
                    {
                        yield return nested;
                    }
                }
                break;
        }
    }

    private static bool IsDuplicate(List<Abbreviation> list, string shortForm, string expandedForm)
    {
        return list.Any(a =>
            a.ShortForm.Equals(shortForm, StringComparison.OrdinalIgnoreCase) &&
            a.ExpandedForm.Equals(expandedForm, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsMissingAbbreviationsTableException(Exception ex)
    {
        Exception? current = ex;
        while (current != null)
        {
            if (current is PostgresException pg && pg.SqlState == "42P01")
            {
                return true;
            }

            current = current.InnerException;
        }

        return false;
    }

    private void SeedCacheWithBuiltIns()
    {
        var builtIns = GetBuiltInAbbreviations();

        _shortFormCache.Clear();
        _expandedFormCache.Clear();

        foreach (var abbr in builtIns)
        {
            var shortKey = abbr.ShortForm.ToLowerInvariant();
            var expandedKey = abbr.ExpandedForm.ToLowerInvariant();

            _shortFormCache.AddOrUpdate(shortKey,
                new List<Abbreviation> { abbr },
                (_, existing) =>
                {
                    existing.Add(abbr);
                    return existing;
                });

            _expandedFormCache.AddOrUpdate(expandedKey,
                new List<Abbreviation> { abbr },
                (_, existing) =>
                {
                    existing.Add(abbr);
                    return existing;
                });
        }

        _logger.LogInformation("Abbreviation cache seeded from built-ins. Total entries: {Count}", builtIns.Count);
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
