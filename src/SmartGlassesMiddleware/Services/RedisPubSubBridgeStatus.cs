namespace SmartGlassesMiddleware.Services;

public class RedisPubSubBridgeStatus
{
    public bool IsSubscribed { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastUpdatedUtc { get; set; }
}
