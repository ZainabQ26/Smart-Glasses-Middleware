namespace SmartGlassesMiddleware.Models;

/// <summary>
/// Represents a bidirectional mapping between an abbreviation and its expanded form,
/// typically sourced from FDA SPL/DailyMed data.
/// Used for intelligent document highlighting — when a user highlights one form,
/// the related counterpart form is also highlighted in the same color.
/// </summary>
public class Abbreviation
{
    /// <summary>
    /// Unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The short form (e.g., "ACE", "BID", "IV"). Case-insensitive.
    /// </summary>
    public string ShortForm { get; set; } = string.Empty;

    /// <summary>
    /// The expanded form (e.g., "Angiotensin Converting Enzyme", "Twice Daily", "Intravenous").
    /// </summary>
    public string ExpandedForm { get; set; } = string.Empty;

    /// <summary>
    /// Context or category (e.g., "drug", "dosage", "route", "frequency", "medical_term").
    /// Helps distinguish homonyms.
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Source of this abbreviation (e.g., "DailyMed", "RxNorm", "USAN", "built-in").
    /// Useful for tracking data lineage and refresh frequency.
    /// </summary>
    public string Source { get; set; } = "built-in";

    /// <summary>
    /// When this abbreviation was last updated or verified.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the row was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
