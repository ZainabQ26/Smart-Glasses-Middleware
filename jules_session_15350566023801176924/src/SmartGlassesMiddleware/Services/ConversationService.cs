using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Services;

public interface IConversationService
{
    Task<Conversation> CreateConversationAsync(string? title = null, Guid? groupId = null);
    Task<Conversation?> GetConversationAsync(Guid conversationId);
    Task<(List<Conversation> Items, int TotalCount)> GetActiveConversationsAsync(Guid? groupId = null, int page = 1, int pageSize = 20);
    Task<ChatMessage> AddMessageAsync(Guid conversationId, string role, string content, string? model = null, List<Guid>? documentIds = null);
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
    private readonly ICacheService _cacheService;
    private readonly double _tokensPerChar;
    private const int MaxTopicLength = 50;
    private const int MaxFallbackTitleLength = 50;
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    public ConversationService(
        ApplicationDbContext context,
        ILogger<ConversationService> logger,
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        ICacheService cacheService)
    {
        _context = context;
        _logger = logger;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _cacheService = cacheService;
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

        {
            try
            {
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to broadcast conversationsChanged after create");
            }
        }
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

    public async Task<(List<Conversation> Items, int TotalCount)> GetActiveConversationsAsync(Guid? groupId = null, int page = 1, int pageSize = 20)
    {
        if (pageSize < 1 || pageSize > MaxPageSize) pageSize = DefaultPageSize;
        if (page < 1) page = 1;

        var query = _context.Conversations
            .Where(c => c.IsActive);

        if (groupId.HasValue)
        {
            query = query.Where(c => c.ResourceGroupId == groupId.Value);
        }

        var totalCount = await query.CountAsync();

        var conversations = await query
            .OrderByDescending(c => c.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        if (conversations.Count == 0)
            return (conversations, totalCount);

        // Load messages for all returned conversations in a single query
        var convoIds = conversations.Select(c => c.Id).ToList();
        var messages = await _context.ChatMessages
            .Where(m => convoIds.Contains(m.ConversationId))
            .Include(m => m.Attachments)
            .OrderBy(m => m.Timestamp)
            .ToListAsync();

        var grouped = messages.GroupBy(m => m.ConversationId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var c in conversations)
        {
            grouped.TryGetValue(c.Id, out var list);
            c.Messages = list ?? new List<ChatMessage>();
        }

        return (conversations, totalCount);
    }

    public async Task<ChatMessage> AddMessageAsync(Guid conversationId, string role, string content, string? model = null, List<Guid>? documentIds = null)
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

        if (documentIds != null && documentIds.Count > 0)
        {
            foreach (var docId in documentIds.Distinct())
            {
                var attachment = new ChatMessageAttachment
                {
                    ChatMessageId = message.Id,
                    DocumentMetadataId = docId
                };
                _context.ChatMessageAttachments.Add(attachment);
            }
        }

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
                    // Normalize the topic string (case-insensitive, length limit)
                    topic = topic.Length > MaxTopicLength ? topic.Substring(0, MaxTopicLength) : topic;
                    
                    // Always create a new group for a new conversation topic
                    var newGroup = new ResourceGroup { Id = Guid.NewGuid(), Name = topic, CreatedAt = DateTime.UtcNow };
                    _context.ResourceGroups.Add(newGroup);
                    await _context.SaveChangesAsync();
                    await InvalidateGroupsCache();
                    await NotifyGroupsChangedAsync();
                    _logger.LogInformation("Created new ResourceGroup for topic '{Topic}'", topic);

                    // Create a new conversation with the topic-based title and attach the group
                    var title = topic.Length > MaxFallbackTitleLength ? topic.Substring(0, MaxFallbackTitleLength) + "..." : topic;
                    _logger.LogInformation("Creating new conversation with topic '{Topic}'", topic);
                    return await CreateConversationAsync(title, newGroup.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Topic detection failed, falling back to time-window heuristic");
        }

        // Fallback: no topic detected or AI unavailable — create new conversation from message excerpt
        var fallbackTitle = userMessage.Length > MaxFallbackTitleLength
            ? userMessage.Substring(0, MaxFallbackTitleLength) + "..."
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
                await InvalidateGroupsCache();
                await NotifyGroupsChangedAsync();
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
            await InvalidateGroupsCache();
            await NotifyGroupsChangedAsync();
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
Do not use generic labels like 'topic shift' or 'topic change'—use a concise label derived from the new message.
Format: YES|topic label OR NO";

                var (_, aiResponse, _) = await ai.ProcessRequestAsync(prompt);
                if (!string.IsNullOrWhiteSpace(aiResponse))
                {
                    var response = aiResponse.Trim();
                    if (response.StartsWith("YES", StringComparison.OrdinalIgnoreCase))
                    {
                        // Extract topic label
                        var parts = response.Split('|');
                        var newTopic = parts.Length > 1 ? parts[1].Trim() : newMessage.Substring(0, Math.Min(MaxFallbackTitleLength, newMessage.Length));
                        var normalizedTopic = newTopic.Trim().ToLowerInvariant();
                        var isGenericTopic = normalizedTopic == "topic shift"
                            || normalizedTopic == "topic change"
                            || normalizedTopic == "topic switch"
                            || normalizedTopic == "new topic";
                        if (isGenericTopic)
                        {
                            newTopic = newMessage.Substring(0, Math.Min(MaxFallbackTitleLength, newMessage.Length));
                        }
                        
                        // Normalize topic length
                        newTopic = newTopic.Length > MaxTopicLength ? newTopic.Substring(0, MaxTopicLength) : newTopic;
                        
                        _logger.LogInformation("Topic change detected in conversation {ConversationId}. New topic: {Topic}", conversationId, newTopic);

                        // Always create a new ResourceGroup for a topic change
                        var newGroup = new ResourceGroup
                        {
                            Id = Guid.NewGuid(),
                            Name = newTopic,
                            CreatedAt = DateTime.UtcNow
                        };
                        _context.ResourceGroups.Add(newGroup);
                        await _context.SaveChangesAsync();
                        await InvalidateGroupsCache();
                        await NotifyGroupsChangedAsync();


                            {
                                try
                                {
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "Failed to broadcast conversationsChanged after message");
                                }
                            }
                        // Create new conversation in the new topic group
                        var newConversation = await CreateConversationAsync(newTopic, newGroup.Id);
                        
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

    private Task NotifyGroupsChangedAsync()
    {
        return Task.CompletedTask;
    }

    private async Task InvalidateGroupsCache()
    {
        try
        {
            var totalCount = await _context.ResourceGroups.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)DefaultPageSize);

            var tasks = new List<Task>();
            for (int page = 1; page <= totalPages + 1; page++)
            {
                tasks.Add(_cacheService.RemoveAsync($"admin:groups:page:{page}:size:{DefaultPageSize}"));
                tasks.Add(_cacheService.RemoveAsync($"admin:groups:page:{page}:size:{MaxPageSize}"));
            }
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error invalidating groups cache");
        }
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
        var messageIds = messagesToMove.Select(m => m.Id).ToList();
        var existingMessages = await _context.ChatMessages
            .Where(m => messageIds.Contains(m.Id))
            .ToListAsync();

        foreach (var existingMessage in existingMessages)
        {
            existingMessage.ConversationId = newConversation.Id;
        }

        // Update original conversation timestamp
        originalConversation.UpdatedAt = DateTime.UtcNow;
        
        await _context.SaveChangesAsync();

        _logger.LogInformation("Split conversation {OriginalId} into {NewId} with topic '{Topic}'", 
            originalConversationId, newConversation.Id, newTopic);

        return newConversation;
    }
}
