using Microsoft.EntityFrameworkCore;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Services;

public interface IConversationService
{
    Task<Conversation> CreateConversationAsync(string? title = null, Guid? groupId = null);
    Task<Conversation?> GetConversationAsync(Guid conversationId);
    Task<List<Conversation>> GetActiveConversationsAsync(Guid? groupId = null);
    Task<ChatMessage> AddMessageAsync(Guid conversationId, string role, string content, string? model = null);
    Task<List<ChatMessage>> GetRecentMessagesAsync(Guid conversationId, int count = 10);
    Task<Conversation> DetectOrCreateConversationAsync(string userMessage, Guid? groupId = null);
    Task<string> SummarizeConversationAsync(Guid conversationId);
    Task ArchiveConversationAsync(Guid conversationId);
}

public class ConversationService : IConversationService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ConversationService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly double _tokensPerChar;

    public ConversationService(
        ApplicationDbContext context,
        ILogger<ConversationService> logger,
        IConfiguration configuration,
        IServiceProvider serviceProvider)
    {
        _context = context;
        _logger = logger;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _tokensPerChar = configuration.GetValue<double?>("RAG:ApproximateTokensPerChar") ?? 0.25;
    }

    public async Task<Conversation> CreateConversationAsync(string? title = null, Guid? groupId = null)
    {
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            Title = title ?? $"Conversation {DateTime.UtcNow:yyyy-MM-dd HH:mm}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true,
            ResourceGroupId = groupId
        };

        _context.Conversations.Add(conversation);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created conversation {ConversationId} with title: {Title}", conversation.Id, conversation.Title);
        return conversation;
    }

    public async Task<Conversation?> GetConversationAsync(Guid conversationId)
    {
        return await _context.Conversations
            .Include(c => c.Messages.OrderBy(m => m.Timestamp))
            .Include(c => c.ResourceGroup)
            .FirstOrDefaultAsync(c => c.Id == conversationId);
    }

    public async Task<List<Conversation>> GetActiveConversationsAsync(Guid? groupId = null)
    {
        var query = _context.Conversations
            .Include(c => c.Messages)
            .Where(c => c.IsActive);

        if (groupId.HasValue)
        {
            query = query.Where(c => c.ResourceGroupId == groupId.Value);
        }

        return await query
            .OrderByDescending(c => c.UpdatedAt)
            .ToListAsync();
    }

    public async Task<ChatMessage> AddMessageAsync(Guid conversationId, string role, string content, string? model = null)
    {
        var conversation = await _context.Conversations.FindAsync(conversationId);
        if (conversation == null)
        {
            throw new ArgumentException($"Conversation {conversationId} not found");
        }

        var message = new ChatMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = role,
            Content = content,
            Model = model,
            Timestamp = DateTime.UtcNow,
            TokenCount = EstimateTokens(content)
        };

        _context.ChatMessages.Add(message);

        // Update conversation timestamp
        conversation.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogDebug("Added {Role} message to conversation {ConversationId}", role, conversationId);
        return message;
    }

    public async Task<List<ChatMessage>> GetRecentMessagesAsync(Guid conversationId, int count = 10)
    {
        return await _context.ChatMessages
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.Timestamp)
            .Take(count)
            .OrderBy(m => m.Timestamp) // Return in chronological order
            .ToListAsync();
    }

    public async Task<Conversation> DetectOrCreateConversationAsync(string userMessage, Guid? groupId = null)
    {
        // First, if a groupId was provided, try to continue an active conversation in that group within the last hour
        var recentConversation = await _context.Conversations
            .Where(c => c.IsActive &&
                        c.UpdatedAt > DateTime.UtcNow.AddHours(-1) &&
                        (!groupId.HasValue || c.ResourceGroupId == groupId))
            .OrderByDescending(c => c.UpdatedAt)
            .FirstOrDefaultAsync();

        if (recentConversation != null)
        {
            _logger.LogInformation("Continuing existing conversation {ConversationId}", recentConversation.Id);
            return recentConversation;
        }

        // Attempt semantic topic detection to auto-group conversations
        try
        {
            var ai = _serviceProvider.GetService(typeof(IAIService)) as IAIService;
            if (ai != null)
            {
                // Ask the AI for a short topic label (keep it brief)
                var prompt = $"Provide a single short topic label (3 words max) that best describes the following user message. Respond with just the label and nothing else.\n\nMessage: {userMessage}";
                var (detectedTask, detectedLabel, _) = await ai.ProcessRequestAsync(prompt);
                var topic = (detectedLabel ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(topic))
                {
                    // Normalize the topic string
                    if (topic.Length > 100) topic = topic.Substring(0, 100);
                    // Try to find or create a ResourceGroup with this topic name
                    var existingGroup = await _context.ResourceGroups.FirstOrDefaultAsync(r => r.Name == topic);
                    if (existingGroup == null)
                    {
                        existingGroup = new ResourceGroup { Id = Guid.NewGuid(), Name = topic, CreatedAt = DateTime.UtcNow };
                        _context.ResourceGroups.Add(existingGroup);
                        await _context.SaveChangesAsync();
                        _logger.LogInformation("Created ResourceGroup for topic '{Topic}'", topic);
                    }

                    // Now look for an active recent conversation in this group
                    var recentInGroup = await _context.Conversations
                        .Where(c => c.IsActive && c.ResourceGroupId == existingGroup.Id && c.UpdatedAt > DateTime.UtcNow.AddHours(-1))
                        .OrderByDescending(c => c.UpdatedAt)
                        .FirstOrDefaultAsync();

                    if (recentInGroup != null)
                    {
                        _logger.LogInformation("Joining recent conversation {ConversationId} in topic group '{Topic}'", recentInGroup.Id, topic);
                        return recentInGroup;
                    }

                    // Create a new conversation with the topic-based title and attach the group
                    var title = topic.Length > 50 ? topic.Substring(0, 50) + "..." : topic;
                    _logger.LogInformation("Creating new conversation with topic '{Topic}'", topic);
                    return await CreateConversationAsync(title, existingGroup.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Topic detection failed, falling back to time-window heuristic");
        }

        // Fallback: no topic detected or AI unavailable — create new conversation from message excerpt
        var fallbackTitle = userMessage.Length > 50
            ? userMessage.Substring(0, 50) + "..."
            : userMessage;

        _logger.LogInformation("Creating new conversation based on user message (fallback)");
        return await CreateConversationAsync(fallbackTitle, groupId);
    }

    public async Task<string> SummarizeConversationAsync(Guid conversationId)
    {
        var conversation = await GetConversationAsync(conversationId);
        if (conversation == null)
        {
            throw new ArgumentException($"Conversation {conversationId} not found");
        }

        // Simple summarization: take first and last few messages
        var messages = conversation.Messages.OrderBy(m => m.Timestamp).ToList();
        if (messages.Count <= 5)
        {
            return "Conversation too short to summarize";
        }

        var summary = $"Conversation started: {conversation.CreatedAt}\n";
        summary += $"Topic: {conversation.Title}\n";
        summary += $"Messages: {messages.Count}\n";
        summary += "Key points:\n";

        // Include first 2 and last 2 messages
        var keyMessages = messages.Take(2).Concat(messages.TakeLast(2)).Distinct();
        foreach (var msg in keyMessages)
        {
            var preview = msg.Content.Length > 100 ? msg.Content.Substring(0, 100) + "..." : msg.Content;
            summary += $"- [{msg.Role}]: {preview}\n";
        }

        conversation.Summary = summary;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Summarized conversation {ConversationId}", conversationId);
        return summary;
    }

    public async Task ArchiveConversationAsync(Guid conversationId)
    {
        var conversation = await _context.Conversations.FindAsync(conversationId);
        if (conversation != null)
        {
            conversation.IsActive = false;
            conversation.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Archived conversation {ConversationId}", conversationId);
        }
    }

    private int EstimateTokens(string text)
    {
        return (int)(text.Length * _tokensPerChar);
    }
}
