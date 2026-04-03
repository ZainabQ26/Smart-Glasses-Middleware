using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Services;

/// <summary>
/// Service for managing abbreviation-to-full-form mappings sourced from FDA SPLs (DailyMed, RxNorm, etc.).
/// Enables intelligent document highlighting where both the abbreviation and expanded form
/// are highlighted together in the same color for better readability.
/// </summary>
public interface IAbbreviationService
{
    /// <summary>
    /// Look up the expanded form(s) for a given abbreviation.
    /// Returns null if not found.
    /// </summary>
    Task<List<Abbreviation>?> LookupByShortFormAsync(string shortForm);

    /// <summary>
    /// Look up the abbreviation(s) for a given expanded form.
    /// Returns null if not found.
    /// </summary>
    Task<List<Abbreviation>?> LookupByExpandedFormAsync(string expandedForm);

    /// <summary>
    /// Given a search term (could be abbrev or expanded), return both directions
    /// so the UI can highlight both the searched term and its counterpart(s).
    /// </summary>
    Task<(List<string> terms, List<Abbreviation> abbreviations)> GetRelatedTermsAsync(string searchTerm);

    /// <summary>
    /// Refresh the in-memory cache from the database or external source (DailyMed, openFDA).
    /// Called periodically or on-demand.
    /// </summary>
    Task RefreshCacheAsync();

    /// <summary>
    /// Bulk-load abbreviations from DailyMed or openFDA into the database.
    /// Returns the count of newly added/updated abbreviations.
    /// </summary>
    Task<int> SyncFromDailyMedAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves all abbreviations currently stored in the cache.
    /// </summary>
    IEnumerable<Abbreviation> GetAllCachedAbbreviations();
}
