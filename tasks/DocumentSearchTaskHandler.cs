using Microsoft.EntityFrameworkCore;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Services;
using System.Text.Json;

namespace SmartGlassesMiddleware.Services;

/// <summary>
/// Local task handler for finding text in documents and returning location information.
/// Supports FDA SPL (Structured Product Labeling) abbreviation expansion so that
/// searching for an abbreviation (e.g. "BP") also matches the full form ("blood pressure")
/// and vice versa.
/// </summary>
public class DocumentSearchTaskHandler : ITaskHandler
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<DocumentSearchTaskHandler> _logger;

    // Typed result to avoid runtime-typed dynamic collections.
    private sealed record ChunkMatch(
        Guid DocumentId,
        string FileName,
        int PageNumber,
        int ChunkIndex,
        string Context,
        double? X,
        double? Y
    );

    public string Name => "document-search";

    public IReadOnlyList<string> Keywords => new[]
    {
        "search for",
        "buscar",
        "search document",
        "buscar documento",
        "find in document",
        "encontrar en documento",
        "locate",
        "localizar",
        "search in document",
        "buscar en documento",
        "look for"
    };

    /// <summary>
    /// Bidirectional FDA SPL abbreviation map.
    /// Key = abbreviation (uppercase), Value = full term.
    /// Expansion is applied in both directions: searching for the abbreviation also
    /// searches for the full term, and vice versa.
    /// </summary>
    private static readonly Dictionary<string, string> FdaSplAbbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BP"]    = "blood pressure",
        ["HR"]    = "heart rate",
        ["HTN"]   = "hypertension",
        ["DM"]    = "diabetes mellitus",
        ["COPD"]  = "chronic obstructive pulmonary disease",
        ["MI"]    = "myocardial infarction",
        ["CHF"]   = "congestive heart failure",
        ["CAD"]   = "coronary artery disease",
        ["AFib"]  = "atrial fibrillation",
        ["CKD"]   = "chronic kidney disease",
        ["UTI"]   = "urinary tract infection",
        ["URI"]   = "upper respiratory infection",
        ["GERD"]  = "gastroesophageal reflux disease",
        ["NSAID"] = "nonsteroidal anti-inflammatory drug",
        ["OTC"]   = "over the counter",
        ["Rx"]    = "prescription",
        ["Sig"]   = "directions for use",
        ["QD"]    = "once daily",
        ["BID"]   = "twice daily",
        ["TID"]   = "three times daily",
        ["QID"]   = "four times daily",
        ["PRN"]   = "as needed",
        ["AC"]    = "before meals",
        ["PC"]    = "after meals",
        ["HS"]    = "at bedtime",
        ["PO"]    = "by mouth",
        ["SL"]    = "sublingual",
        ["IV"]    = "intravenous",
        ["IM"]    = "intramuscular",
        ["SC"]    = "subcutaneous",
        ["mg"]    = "milligrams",
        ["mL"]    = "milliliters",
        ["mcg"]   = "micrograms",
        ["g"]     = "grams",
        ["kg"]    = "kilograms",
        ["mEq"]   = "milliequivalents",
        ["IU"]    = "international units",
        ["SPF"]   = "sun protection factor",
        ["NDA"]   = "new drug application",
        ["ANDA"]  = "abbreviated new drug application",
        ["BLA"]   = "biologics license application",
        ["NDC"]   = "national drug code",
        ["DEA"]   = "drug enforcement administration",
        ["FDA"]   = "food and drug administration",
        ["USP"]   = "united states pharmacopeia",
        ["NF"]    = "national formulary",
        ["API"]   = "active pharmaceutical ingredient",
        ["AE"]    = "adverse event",
        ["SAE"]   = "serious adverse event",
        ["ADR"]   = "adverse drug reaction",
        ["PK"]    = "pharmacokinetics",
        ["PD"]    = "pharmacodynamics",
        ["CI"]    = "confidence interval",
        ["RCT"]   = "randomized controlled trial",
    };

    public DocumentSearchTaskHandler(ApplicationDbContext context, ILogger<DocumentSearchTaskHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<string> HandleAsync(string input)
    {
        try
        {
            var searchText = ExtractSearchTerm(input);
            string? documentFilter = null;

            if (input.ToLower().Contains(" in document "))
            {
                var parts = input.Split(" in document ", StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    searchText = ExtractSearchTerm(parts[0]);
                    documentFilter = parts[1].Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(searchText))
            {
                return "❌ No search term provided. Please say what you want to find.";
            }

            // Expand FDA SPL abbreviations to widen the search.
            var searchTerms = BuildSearchTerms(searchText);

            _logger.LogInformation("Searching for '{SearchText}' (terms: {Terms}) in documents (filter: {Filter})",
                searchText, string.Join(", ", searchTerms), documentFilter);

            var allMatches = new List<ChunkMatch>();

            foreach (var term in searchTerms)
            {
                var capturedTerm = term;
                var query = _context.DocumentChunks
                    .Include(c => c.DocumentMetadata)
                    .Where(c => c.ChunkText.ToLower().Contains(capturedTerm.ToLower()));

                if (!string.IsNullOrEmpty(documentFilter))
                {
                    query = query.Where(c => c.DocumentMetadata.FileName.Contains(documentFilter) ||
                                            c.DocumentMetadata.Id.ToString() == documentFilter);
                }

                var termMatches = await query
                    .OrderBy(c => c.DocumentMetadata.FileName)
                    .ThenBy(c => c.PageNumber)
                    .Take(10)
                    .Select(c => new ChunkMatch(
                        c.DocumentMetadataId,
                        c.DocumentMetadata.FileName,
                        c.PageNumber,
                        c.ChunkIndex,
                        c.ChunkText.Length > 200 ? c.ChunkText.Substring(0, 200) + "..." : c.ChunkText,
                        c.X,
                        c.Y
                    ))
                    .ToListAsync();

                allMatches.AddRange(termMatches);
            }

            // De-duplicate by (DocumentId, PageNumber, ChunkIndex)
            var matches = allMatches
                .GroupBy(m => $"{m.DocumentId}_{m.PageNumber}_{m.ChunkIndex}")
                .Select(g => g.First())
                .Take(10)
                .ToList();

            if (!matches.Any())
            {
                var filterDesc = documentFilter != null ? $" in document '{documentFilter}'" : "";
                return $"❌ '{searchText}' was not found{filterDesc}. The word or phrase does not appear in any indexed documents.";
            }

            var expandedNote = searchTerms.Count > 1
                ? $" (searched: {string.Join(", ", searchTerms.Select(t => $"\"{t}\""))})"
                : "";

            var response = $"Found {matches.Count} match(es) for '{searchText}'{expandedNote}:\n\n";
            foreach (var match in matches)
            {
                response += $"📄 **{match.FileName}** (Page {match.PageNumber})\n";
                response += $"   Context: {match.Context}\n\n";
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching documents");
            return $"❌ Error searching documents: {ex.Message}";
        }
    }

    private static string ExtractSearchTerm(string input)
    {
        var trimmed = input.Trim();
        string[] prefixes =
        [
            "search for the phrase ",
            "search for the word ",
            "search for the ",
            "search for ",
            "search the word ",
            "search the phrase ",
            "search document for ",
            "search in document for ",
            "search ",
            "find the word ",
            "find the phrase ",
            "find the ",
            "find ",
            "locate the ",
            "locate ",
            "look for the ",
            "look for ",
        ];

        foreach (var prefix in prefixes)
        {
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return trimmed[prefix.Length..].Trim();
        }

        return trimmed;
    }

    /// <summary>
    /// Build a list of search terms by expanding FDA SPL abbreviations bidirectionally.
    /// Always includes the original term, plus any matched abbreviation/expansion.
    /// </summary>
    private static List<string> BuildSearchTerms(string searchText)
    {
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { searchText };

        // Check if the search text matches an abbreviation → add full form
        if (FdaSplAbbreviations.TryGetValue(searchText.Trim(), out var fullForm))
        {
            terms.Add(fullForm);
        }

        // Check if the search text matches a full form → add abbreviation
        foreach (var kvp in FdaSplAbbreviations)
        {
            if (string.Equals(kvp.Value, searchText.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                terms.Add(kvp.Key);
                break;
            }
        }

        return terms.ToList();
    }
}
