namespace SmartGlassesMiddleware.Models;

public class RequestHistory
{
    public Guid Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string RequestType { get; set; } = string.Empty;
    public string InputText { get; set; } = string.Empty;
    public string? DetectedTask { get; set; }
    public string? Response { get; set; }
    public bool UsedAdvancedModel { get; set; }
    public string? MediaStorageKey { get; set; }
}
