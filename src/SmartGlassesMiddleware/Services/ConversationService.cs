using Microsoft.EntityFrameworkCore;
using SmartGlassesMiddleware.Hubs;
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
    private readonly IHubContext<SmartGlassesMiddleware.Hubs.ConversationHub>? _conversationHub;
    private readonly IHubContext<SmartGlassesMiddleware.Hubs.ResourceHub>? _resourceHub;
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
        ICacheService cacheService,
        IHubContext<SmartGlassesMiddleware.Hubs.ConversationHub>? conversationHub = null,
        IHubContext<SmartGlassesMiddleware.Hubs.ResourceHub>? resourceHub = null)
    {
        _context = context;
        _logger = logger;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _cacheService = cacheService;
        _conversationHub = conversationHub;
        _resourceHub = resourceHub;
        _tokensPerChar = configuration.GetValue<double?>("RAG:ApproximateTokensPerChar") ?? 0.25;
    }

    public Task<Conversation> CreateConversationAsync(string? title = null, Guid? groupId = null)
    {
        return Task.FromResult(new Conversation
        {
            Id = Guid.NewGuid(),
            Title = title ?? $"Conversation {DateTime.UtcNow:yyyy-MM-dd HH:mm}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true,
            ResourceGroupId = groupId
        });
    }
    public Task<Conversation?> GetConversationAsync(Guid conversationId)
    {
        return Task.FromResult<Conversation?>(null);
    }
    public Task<(List<Conversation> Items, int TotalCount)> GetActiveConversationsAsync(Guid? groupId = null, int page = 1, int pageSize = 20)
    {
        return Task.FromResult((new List<Conversation>(), 0));
    }
    public Task<ChatMessage> AddMessageAsync(Guid conversationId, string role, string content, string? model = null, List<Guid>? documentIds = null)
    {
        return Task.FromResult(new ChatMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = role,
            Content = content,
            Model = model,
            Timestamp = DateTime.UtcNow,
            TokenCount = EstimateTokens(content)
        });
    }
    public Task<List<ChatMessage>> GetRecentMessagesAsync(Guid conversationId, int count = 10)
    {
        return Task.FromResult(new List<ChatMessage>());
    }
    public Task<Conversation> DetectOrCreateConversationAsync(string userMessage, Guid? groupId = null)
    {
        var title = userMessage.Length > MaxFallbackTitleLength
            ? userMessage.Substring(0, MaxFallbackTitleLength) + "..."
            : userMessage;
        return CreateConversationAsync(title, groupId);
    }
    public Task<string> SummarizeConversationAsync(Guid conversationId)
    {
        return Task.FromResult(string.Empty);
    }
    public Task ArchiveConversationAsync(Guid conversationId)
    {
        return Task.CompletedTask;
    }
    public Task<List<ChatMessage>> SearchRelevantMessagesAsync(Guid conversationId, string query, int maxResults = 5)
    {
        return Task.FromResult(new List<ChatMessage>());
    }
    private int EstimateTokens(string text)
    {
        return (int)(text.Length * _tokensPerChar);
    }

    public Task<(bool topicChanged, Conversation? newConversation)> DetectTopicChangeAsync(Guid conversationId, string newMessage)
    {
        return Task.FromResult<(bool, Conversation?)>((false, null));
    }
    private Task NotifyGroupsChangedAsync()
    {
        return _resourceHub != null
            ? _resourceHub.Clients.All.SendAsync("groupsChanged")
            : Task.CompletedTask;
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
