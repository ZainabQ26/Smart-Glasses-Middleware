namespace SmartGlassesMiddleware.Models;

/// <summary>
/// Join table linking ChatMessage to ManagedFile for per-message attachments
/// </summary>
public class ChatMessageFile
{
    public Guid ChatMessageId { get; set; }
    public ChatMessage ChatMessage { get; set; } = null!;

    public Guid ManagedFileId { get; set; }
    public ManagedFile ManagedFile { get; set; } = null!;
}
