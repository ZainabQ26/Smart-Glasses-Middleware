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
    Task<List<ChatMessage>> SearchRelevantMessagesAsync(Guid conversationId, string query, int maxResults = 5);
    Task<(bool topicChanged, Conversation? newConversation)> DetectTopicChangeAsync(Guid conversationId, string newMessage);
    Task<Conversation> SplitConversationAsync(Guid originalConversationId, string newTopic, List<ChatMessage> messagesToMove);
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
        var conversation = await _context.Conversations
            .Include(c => c.ResourceGroup)
            .FirstOrDefaultAsync(c => c.Id == conversationId);

        if (conversation == null)
            return null;

        // Load and order messages explicitly to ensure they're populated and sorted
        conversation.Messages = await _context.ChatMessages
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.Timestamp)
            .ToListAsync();

        return conversation;
    }

    public async Task<List<Conversation>> GetActiveConversationsAsync(Guid? groupId = null)
    {
        var query = _context.Conversations
            .Where(c => c.IsActive);

        if (groupId.HasValue)
        {
            query = query.Where(c => c.ResourceGroupId == groupId.Value);
        }

        var conversations = await query
            .OrderByDescending(c => c.UpdatedAt)
            .ToListAsync();

        if (conversations.Count == 0)
            return conversations;

        // Load messages for all returned conversations in a single query
        var convoIds = conversations.Select(c => c.Id).ToList();
        var messages = await _context.ChatMessages
            .Where(m => convoIds.Contains(m.ConversationId))
            .OrderBy(m => m.Timestamp)
            .ToListAsync();

        var grouped = messages.GroupBy(m => m.ConversationId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var c in conversations)
        {
            grouped.TryGetValue(c.Id, out var list);
            c.Messages = list ?? new List<ChatMessage>();
        }

        return conversations;
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

        Guid? effectiveGroupId = groupId;
        if (effectiveGroupId.HasValue)
        {
            var existingGroup = await _context.ResourceGroups.FindAsync(effectiveGroupId.Value);
            if (existingGroup == null)
            {
                var newGroup = new ResourceGroup
                {
                    Id = effectiveGroupId.Value,
                    Name = fallbackTitle,
                    CreatedAt = DateTime.UtcNow
                };
                _context.ResourceGroups.Add(newGroup);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Created ResourceGroup {GroupId} for fallback conversation", newGroup.Id);
            }
        }
        else
        {
            var newGroup = new ResourceGroup
            {
                Id = Guid.NewGuid(),
                Name = fallbackTitle,
                CreatedAt = DateTime.UtcNow
            };
            _context.ResourceGroups.Add(newGroup);
            await _context.SaveChangesAsync();
            effectiveGroupId = newGroup.Id;
            _logger.LogInformation("Created ResourceGroup {GroupId} for fallback conversation", newGroup.Id);
        }

        _logger.LogInformation("Creating new conversation based on user message (fallback)");
        return await CreateConversationAsync(fallbackTitle, effectiveGroupId);
    }

    public async Task<string> SummarizeConversationAsync(Guid conversationId)
    {
        var conversation = await GetConversationAsync(conversationId);
        if (conversation == null)
        {
            throw new ArgumentException($"Conversation {conversationId} not found");
        }

        var messages = conversation.Messages.OrderBy(m => m.Timestamp).ToList();
        if (messages.Count <= 5)
        {
            return "Conversation too short to summarize";
        }

        try
        {
            // Try to use AI for summarization
            var aiService = _serviceProvider.GetService(typeof(IAIService)) as IAIService;
            if (aiService != null)
            {
                var conversationText = string.Join("\n", messages.Select(m => $"{m.Role}: {m.Content}"));
                var prompt = $@"Summarize the following conversation into a concise summary (3-5 sentences max) covering the main topics and key points:

{conversationText}

Summary:";

                var (_, aiSummary, _) = await aiService.ProcessRequestAsync(prompt);
                if (!string.IsNullOrWhiteSpace(aiSummary))
                {
                    var summary = $"Conversation Summary ({messages.Count} messages):\n{aiSummary}";
                    conversation.Summary = summary;
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("AI-summarized conversation {ConversationId}", conversationId);
                    return summary;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate AI summary, falling back to basic summary");
        }

        // Fallback: Simple summarization with first and last few messages
        var fallbackSummary = $"Conversation started: {conversation.CreatedAt}\n";
        fallbackSummary += $"Topic: {conversation.Title}\n";
        fallbackSummary += $"Messages: {messages.Count}\n";
        fallbackSummary += "Key points:\n";

        // Include first 2 and last 2 messages
        var keyMessages = messages.Take(2).Concat(messages.TakeLast(2)).Distinct();
        foreach (var msg in keyMessages)
        {
            var preview = msg.Content.Length > 100 ? msg.Content.Substring(0, 100) + "..." : msg.Content;
            fallbackSummary += $"- [{msg.Role}]: {preview}\n";
        }

        conversation.Summary = fallbackSummary;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Summarized conversation {ConversationId}", conversationId);
        return fallbackSummary;
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

    public async Task<List<ChatMessage>> SearchRelevantMessagesAsync(Guid conversationId, string query, int maxResults = 5)
    {
        try
        {
            // Get recent messages from this conversation
            // Note: Full semantic search would require storing embeddings for each message
            // and performing vector similarity search. This is a simplified implementation.
            const int searchPoolSize = 50; // Limit to recent 50 messages for performance
            
            var messages = await _context.ChatMessages
                .Where(m => m.ConversationId == conversationId)
                .OrderByDescending(m => m.Timestamp)
                .Take(searchPoolSize)
                .ToListAsync();

            // For now, return most recent messages as this is a simplified implementation
            // A full implementation would:
            // 1. Generate embeddings for each message during AddMessageAsync
            // 2. Store embeddings in the ChatMessage model
            // 3. Use RAG service to find semantically similar messages via vector search
            _logger.LogDebug("Semantic search for conversation messages is a simplified implementation, returning recent messages");
            return messages
                .OrderByDescending(m => m.Timestamp)
                .Take(maxResults)
                .OrderBy(m => m.Timestamp)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching relevant messages, falling back to recent messages");
            return await GetRecentMessagesAsync(conversationId, maxResults);
        }
    }

    private int EstimateTokens(string text)
    {
        return (int)(text.Length * _tokensPerChar);
    }

    public async Task<(bool topicChanged, Conversation? newConversation)> DetectTopicChangeAsync(Guid conversationId, string newMessage)
    {
        var conversation = await GetConversationAsync(conversationId);
        if (conversation == null)
        {
            throw new ArgumentException($"Conversation {conversationId} not found");
        }

        // Need at least a few messages to detect topic change
        var recentMessages = conversation.Messages
            .OrderByDescending(m => m.Timestamp)
            .Take(5)
            .OrderBy(m => m.Timestamp)
            .ToList();

        if (recentMessages.Count < 2)
        {
            return (false, null);
        }

        try
        {
            var ai = _serviceProvider.GetService(typeof(IAIService)) as IAIService;
            if (ai != null)
            {
                // Build context from recent messages
                var recentContext = string.Join("\n", recentMessages.Select(m => $"{m.Role}: {m.Content}"));
                
                // Ask AI to detect topic change
                var prompt = $@"Analyze if the new message represents a significant topic change from the recent conversation.
Recent conversation:
{recentContext}

New message: {newMessage}

Does this new message change the topic significantly? Respond with 'YES' or 'NO', followed by a brief topic label (max 3 words) if YES.
Format: YES|topic label OR NO";

                var (_, aiResponse, _) = await ai.ProcessRequestAsync(prompt);
                if (!string.IsNullOrWhiteSpace(aiResponse))
                {
                    var response = aiResponse.Trim();
                    if (response.StartsWith("YES", StringComparison.OrdinalIgnoreCase))
                    {
                        // Extract topic label
                        var parts = response.Split('|');
                        var newTopic = parts.Length > 1 ? parts[1].Trim() : newMessage.Substring(0, Math.Min(50, newMessage.Length));
                        
                        _logger.LogInformation("Topic change detected in conversation {ConversationId}. New topic: {Topic}", conversationId, newTopic);

                        // Find or create a ResourceGroup for the new topic
                        var existingGroup = await _context.ResourceGroups.FirstOrDefaultAsync(r => r.Name == newTopic);
                        if (existingGroup == null)
                        {
                            existingGroup = new ResourceGroup 
                            { 
                                Id = Guid.NewGuid(), 
                                Name = newTopic, 
                                CreatedAt = DateTime.UtcNow 
                            };
                            _context.ResourceGroups.Add(existingGroup);
                            await _context.SaveChangesAsync();
                        }

                        // Create new conversation in the new topic group
                        var newConversation = await CreateConversationAsync(newTopic, existingGroup.Id);
                        
                        return (true, newConversation);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Topic change detection failed for conversation {ConversationId}", conversationId);
        }

        return (false, null);
    }

    public async Task<Conversation> SplitConversationAsync(Guid originalConversationId, string newTopic, List<ChatMessage> messagesToMove)
    {
        var originalConversation = await GetConversationAsync(originalConversationId);
        if (originalConversation == null)
        {
            throw new ArgumentException($"Original conversation {originalConversationId} not found");
        }

        // Create new conversation for the split
        var newConversation = await CreateConversationAsync(newTopic);

        // Move messages to new conversation
        foreach (var message in messagesToMove)
        {
            var existingMessage = await _context.ChatMessages.FindAsync(message.Id);
            if (existingMessage != null)
            {
                existingMessage.ConversationId = newConversation.Id;
            }
        }

        // Update original conversation timestamp
        originalConversation.UpdatedAt = DateTime.UtcNow;
        
        await _context.SaveChangesAsync();

        _logger.LogInformation("Split conversation {OriginalId} into {NewId} with topic '{Topic}'", 
            originalConversationId, newConversation.Id, newTopic);

        return newConversation;
    }
}
