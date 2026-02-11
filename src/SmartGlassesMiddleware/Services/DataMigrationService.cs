using Microsoft.EntityFrameworkCore;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;
using System.Text.Json;

namespace SmartGlassesMiddleware.Services;

public class DataMigrationService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<DataMigrationService> _logger;

    public DataMigrationService(ApplicationDbContext context, ILogger<DataMigrationService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Migrate existing ChatMessage.Content JSON payloads that embedded attachments into the new join table.
    /// This finds messages whose content parses as JSON and contains a `files` array, then creates ChatMessageFile
    /// mappings and replaces the message content with the original `text` value.
    /// </summary>
    public async Task MigrateMessageAttachmentsAsync(CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Starting migration of message attachments (JSON -> ChatMessageFile join table)");

            // Try to determine the active EF provider. In test scenarios EF may register multiple
            // providers which causes accessing ProviderName to throw; treat that as a signal to skip.
            string providerName;
            try
            {
                providerName = _context.Database.ProviderName ?? string.Empty;
            }
            catch (InvalidOperationException)
            {
                _logger.LogInformation("Multiple EF providers registered (likely testing). Skipping message attachments migration.");
                return;
            }

            // If the configured provider is InMemory (typically in unit tests), skip this migration
            if (providerName.Contains("InMemory", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Skipping message attachments migration for InMemory provider ({Provider})", providerName);
                return;
            }

            // Find candidate messages where content looks like a JSON object containing "files"
            var candidates = await _context.ChatMessages
                .Where(m => m.Content.StartsWith("{") && m.Content.Contains("\"files\""))
                .ToListAsync(ct);

            if (candidates.Count == 0)
            {
                _logger.LogInformation("No message-attachment JSON payloads found to migrate.");
                return;
            }

            _logger.LogInformation("Found {Count} messages to examine for migration", candidates.Count);

            foreach (var msg in candidates)
            {
                try
                {
                    using var doc = JsonDocument.Parse(msg.Content);
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("files", out var filesEl) || filesEl.ValueKind != JsonValueKind.Array)
                        continue;

                    var text = root.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String
                        ? textEl.GetString() ?? string.Empty
                        : msg.Content;

                    var fileIds = new List<Guid>();
                    foreach (var el in filesEl.EnumerateArray())
                    {
                        if (el.ValueKind == JsonValueKind.String && Guid.TryParse(el.GetString(), out var g))
                        {
                            fileIds.Add(g);
                        }
                        else if (el.ValueKind == JsonValueKind.Number && el.TryGetGuid(out var g2))
                        {
                            fileIds.Add(g2);
                        }
                    }

                    if (fileIds.Count == 0)
                        continue;

                    // Update message content to plain text
                    msg.Content = text;

                    // create join rows if missing
                    foreach (var fid in fileIds)
                    {
                        var existing = await _context.Set<ChatMessageFile>().FindAsync(new object[] { msg.Id, fid }, ct);
                        if (existing != null) continue;

                        var mf = await _context.ManagedFiles.FindAsync(new object[] { fid }, ct);
                        if (mf == null) continue; // skip missing files

                        _context.Add(new ChatMessageFile { ChatMessageId = msg.Id, ManagedFileId = fid });

                        // If conversation has a resource group, ensure ManagedFileGroup mapping exists
                        var conv = await _context.Conversations.FindAsync(new object[] { msg.ConversationId }, ct);
                        if (conv != null && conv.ResourceGroupId.HasValue)
                        {
                            var existingGroup = await _context.Set<ManagedFileGroup>().FindAsync(new object[] { fid, conv.ResourceGroupId.Value }, ct);
                            if (existingGroup == null)
                            {
                                _context.Add(new ManagedFileGroup { ManagedFileId = fid, ResourceGroupId = conv.ResourceGroupId.Value });
                            }
                        }
                    }

                    await _context.SaveChangesAsync(ct);
                    _logger.LogInformation("Migrated attachments for message {MessageId}", msg.Id);
                }
                catch (JsonException)
                {
                    // skip messages with invalid JSON
                    continue;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to migrate attachments for message {MessageId}", msg.Id);
                }
            }

            _logger.LogInformation("Message attachments migration completed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run message attachments migration");
        }
    }
}
