using Microsoft.AspNetCore.Mvc;
using SmartGlassesMiddleware.Services;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Data;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using Microsoft.AspNetCore.SignalR;
using SmartGlassesMiddleware.Hubs;

namespace SmartGlassesMiddleware.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConversationsController : ControllerBase
{
    private readonly ILogger<ConversationsController> _logger;
    private readonly IConversationService _conversationService;
    private readonly IRAGService _ragService;
    private readonly ApplicationDbContext _context;
    private readonly ICacheService _cacheService;
    private readonly IHubContext<ResourceHub>? _resourceHub;
    private static readonly TimeSpan InitEndpointTimeout = TimeSpan.FromSeconds(8);
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    public ConversationsController(
        ILogger<ConversationsController> logger,
        IConversationService conversationService,
        IRAGService ragService,
        ApplicationDbContext context,
        ICacheService cacheService,
        IHubContext<ResourceHub>? resourceHub = null)
    {
        _logger = logger;
        _conversationService = conversationService;
        _ragService = ragService;
        _context = context;
        _cacheService = cacheService;
        _resourceHub = resourceHub;
    }

    /// <summary>
    /// Create a new conversation
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(Conversation), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateConversation([FromBody] CreateConversationRequest request)
    {
        try
        {
            var conversation = await _conversationService.CreateConversationAsync(
                request.Title,
                request.ResourceGroupId);

            return CreatedAtAction(nameof(GetConversation), new { id = conversation.Id }, conversation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating conversation");
            return StatusCode(500, new { error = "Failed to create conversation", details = ex.Message });
        }
    }

    /// <summary>
    /// Get a specific conversation with its messages
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(Conversation), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetConversation(Guid id)
    {
        try
        {
            var conversation = await _context.Conversations
                .Include(c => c.ResourceGroup)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (conversation == null)
                return NotFound();

            var messages = await _context.ChatMessages
                .Where(m => m.ConversationId == id)
                .OrderBy(m => m.Timestamp)
                .Include(m => m.Attachments)
                    .ThenInclude(a => a.Document)
                .ToListAsync();

            var response = new
            {
                conversation.Id,
                conversation.Title,
                conversation.CreatedAt,
                conversation.UpdatedAt,
                conversation.IsActive,
                conversation.ResourceGroupId,
                ResourceGroup = conversation.ResourceGroup == null ? null : new
                {
                    conversation.ResourceGroup.Id,
                    conversation.ResourceGroup.Name,
                    conversation.ResourceGroup.Description,
                    conversation.ResourceGroup.CreatedAt
                },
                Messages = messages.Select(m => new
                {
                    m.Id,
                    m.ConversationId,
                    m.Role,
                    m.Content,
                    m.Model,
                    m.Timestamp,
                    m.TokenCount,
                    Attachments = m.Attachments
                        .Where(a => a.Document != null)
                        .Select(a => new
                        {
                            Id = a.DocumentMetadataId,
                            FileName = a.Document!.FileName,
                            ContentType = a.Document.ContentType,
                            OriginalFileName = a.Document.OriginalFileName
                        }).ToList()
                }).ToList(),
                conversation.Summary,
                conversation.SummaryEmbedding,
                conversation.SummaryEmbeddingDimension
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving conversation {ConversationId}", id);
            return StatusCode(500, new { error = "Failed to retrieve conversation", details = ex.Message });
        }
    }

    /// <summary>
    /// Get active conversations with pagination
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConversations(
        [FromQuery] Guid? groupId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > MaxPageSize) pageSize = DefaultPageSize;

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
            timeoutCts.CancelAfter(InitEndpointTimeout);
            var ct = timeoutCts.Token;

            var query = _context.Conversations
                .AsNoTracking()
                .Where(c => c.IsActive);

            if (groupId.HasValue)
            {
                query = query.Where(c => c.ResourceGroupId == groupId.Value);
            }

            var totalCount = await query.CountAsync(ct);

            var items = await query
                .OrderByDescending(c => c.UpdatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => new
                {
                    c.Id,
                    c.Title,
                    c.CreatedAt,
                    c.UpdatedAt,
                    c.IsActive,
                    c.ResourceGroupId
                })
                .ToListAsync(ct);

            var conversationIds = items.Select(i => i.Id).ToList();

            var messageCounts = conversationIds.Count == 0
                ? new Dictionary<Guid, int>()
                : await _context.ChatMessages
                    .AsNoTracking()
                    .Where(m => conversationIds.Contains(m.ConversationId))
                    .GroupBy(m => m.ConversationId)
                    .Select(g => new { ConversationId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.ConversationId, x => x.Count, ct);

            var attachmentCounts = conversationIds.Count == 0
                ? new Dictionary<Guid, int>()
                : await _context.ChatMessageAttachments
                    .AsNoTracking()
                    .Where(a => conversationIds.Contains(a.ChatMessage.ConversationId))
                    .GroupBy(a => a.ChatMessage.ConversationId)
                    .Select(g => new { ConversationId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.ConversationId, x => x.Count, ct);

            var lastMessages = conversationIds.Count == 0
                ? new Dictionary<Guid, string?>()
                : await _context.ChatMessages
                    .AsNoTracking()
                    .Where(m => conversationIds.Contains(m.ConversationId))
                    .GroupBy(m => m.ConversationId)
                    .Select(g => new
                    {
                        ConversationId = g.Key,
                        LastMessage = g
                            .OrderByDescending(m => m.Timestamp)
                            .Select(m => m.Content)
                            .FirstOrDefault()
                    })
                    .ToDictionaryAsync(x => x.ConversationId, x => x.LastMessage, ct);

            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            return Ok(new
            {
                data = items.Select(i => new
                {
                    i.Id,
                    i.Title,
                    i.CreatedAt,
                    i.UpdatedAt,
                    i.IsActive,
                    i.ResourceGroupId,
                    MessageCount = messageCounts.TryGetValue(i.Id, out var messageCount) ? messageCount : 0,
                    AttachmentCount = attachmentCounts.TryGetValue(i.Id, out var attachmentCount) ? attachmentCount : 0,
                    LastMessage = !lastMessages.TryGetValue(i.Id, out var rawLastMessage) || string.IsNullOrWhiteSpace(rawLastMessage)
                        ? string.Empty
                        : (rawLastMessage.Length > 220 ? rawLastMessage.Substring(0, 220) : rawLastMessage),
                    Snippet = !lastMessages.TryGetValue(i.Id, out rawLastMessage) || string.IsNullOrWhiteSpace(rawLastMessage)
                        ? string.Empty
                        : (rawLastMessage.Length > 220 ? rawLastMessage.Substring(0, 220) : rawLastMessage)
                }),
                pagination = new
                {
                    currentPage = page,
                    pageSize,
                    totalCount,
                    totalPages,
                    hasNext = page < totalPages,
                    hasPrevious = page > 1
                }
            });
        }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogWarning("Timed out retrieving conversations for page {Page} size {PageSize} group {GroupId}", page, pageSize, groupId);
            return Ok(new
            {
                data = Array.Empty<object>(),
                pagination = new
                {
                    currentPage = page,
                    pageSize,
                    totalCount = 0,
                    totalPages = 0,
                    hasNext = false,
                    hasPrevious = page > 1
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving conversations");
            return Ok(new
            {
                data = Array.Empty<object>(),
                pagination = new
                {
                    currentPage = page,
                    pageSize,
                    totalCount = 0,
                    totalPages = 0,
                    hasNext = false,
                    hasPrevious = page > 1
                }
            });
        }
    }

    /// <summary>
    /// Add a message to a conversation
    /// </summary>
    [HttpPost("{id}/messages")]
    [ProducesResponseType(typeof(ChatMessage), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddMessage(Guid id, [FromBody] AddMessageRequest request)
    {
        try
        {
            var message = await _conversationService.AddMessageAsync(
                id,
                request.Role,
                request.Content,
                request.Model,
                request.DocumentIds);

            return CreatedAtAction(nameof(GetConversation), new { id }, message);
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding message to conversation {ConversationId}", id);
            return StatusCode(500, new { error = "Failed to add message", details = ex.Message });
        }
    }

    /// <summary>
    /// Update conversation metadata (title)
    /// </summary>
    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateConversation(Guid id, [FromBody] UpdateConversationRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { error = "Title is required" });

        try
        {
            var conversation = await _context.Conversations.FindAsync(id);
            if (conversation == null)
                return NotFound(new { error = "Conversation not found" });

            conversation.Title = request.Title.Trim();
            conversation.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await InvalidateGroupsCache();
            await NotifyGroupsChangedAsync();
            await NotifyConversationsChangedAsync();

            return Ok(new { conversation.Id, conversation.Title, conversation.UpdatedAt });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating conversation {ConversationId}", id);
            return StatusCode(500, new { error = "Failed to update conversation", details = ex.Message });
        }
    }

    /// <summary>
    /// Query documents with conversation context
    /// </summary>
    [HttpPost("{id}/query")]
    [ProducesResponseType(typeof(QueryResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> QueryWithConversation(Guid id, [FromBody] ConversationQueryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return BadRequest("Query cannot be empty");

        try
        {
            // Detect topic change before adding message
            var (topicChanged, newConversation) = await _conversationService.DetectTopicChangeAsync(id, request.Query);
            
            Guid targetConversationId = id;
            if (topicChanged && newConversation != null)
            {
                targetConversationId = newConversation.Id;
                _logger.LogInformation("Topic changed, switching to new conversation {NewId}", targetConversationId);
            }

            // Add user message to conversation (original or new)
            await _conversationService.AddMessageAsync(targetConversationId, "user", request.Query);

            // Query with conversation context
            var result = await _ragService.QueryDocumentsWithConversationAsync(
                request.Query,
                targetConversationId,
                request.TopK ?? 5,
                request.GroupIds,
                null);

            // Add assistant response to conversation
            await _conversationService.AddMessageAsync(
                targetConversationId,
                "assistant",
                result.Answer,
                request.Model ?? "default");

            result.ConversationId = targetConversationId;
            
            // Include info about topic change in the response
            if (topicChanged && newConversation != null)
            {
                result.TopicChanged = true;
                result.NewConversationId = newConversation.Id;
            }
            
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying with conversation context");
            return StatusCode(500, new { error = "Failed to query with conversation", details = ex.Message });
        }
    }

    /// <summary>
    /// Summarize a conversation and optionally generate embedding
    /// </summary>
    [HttpPost("{id}/summarize")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SummarizeConversation(Guid id, [FromQuery] bool generateEmbedding = false)
    {
        try
        {
            var summary = await _conversationService.SummarizeConversationAsync(id);

            // Optionally generate embedding for the summary
            if (generateEmbedding && !string.IsNullOrEmpty(summary))
            {
                var conversation = await _context.Conversations.FindAsync(id);
                if (conversation != null)
                {
                    var (embeddingJson, dimension) = await _ragService.GenerateTextEmbeddingJsonAsync(summary);
                    conversation.SummaryEmbedding = embeddingJson;
                    conversation.SummaryEmbeddingDimension = dimension;
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Generated embedding for conversation {ConversationId} summary", id);
                }
            }

            return Ok(new { summary, embeddingGenerated = generateEmbedding });
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error summarizing conversation {ConversationId}", id);
            return StatusCode(500, new { error = "Failed to summarize conversation", details = ex.Message });
        }
    }

    /// <summary>
    /// Archive a conversation
    /// </summary>
    [HttpPost("{id}/archive")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ArchiveConversation(Guid id)
    {
        try
        {
            await _conversationService.ArchiveConversationAsync(id);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error archiving conversation {ConversationId}", id);
            return StatusCode(500, new { error = "Failed to archive conversation", details = ex.Message });
        }
    }

    /// <summary>
    /// Update the resource group associated with a conversation
    /// </summary>
    [HttpPut("{id}/group")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateConversationGroup(Guid id, [FromBody] UpdateConversationGroupRequest request)
    {
        if (request == null || !request.ResourceGroupId.HasValue || request.ResourceGroupId.Value == Guid.Empty)
            return BadRequest(new { error = "ResourceGroupId is required" });

        try
        {
            var conversation = await _context.Conversations.FindAsync(id);
            if (conversation == null)
                return NotFound(new { error = "Conversation not found" });

            var group = await _context.ResourceGroups.FindAsync(request.ResourceGroupId.Value);
            if (group == null)
                return NotFound(new { error = "Resource group not found" });

            conversation.ResourceGroupId = request.ResourceGroupId.Value;
            conversation.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { conversation.Id, conversation.ResourceGroupId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating group for conversation {ConversationId}", id);
            return StatusCode(500, new { error = "Failed to update conversation group", details = ex.Message });
        }
    }

    /// <summary>
    /// Permanently delete a conversation and its messages from the database.
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteConversation(Guid id)
    {
        try
        {
            var conversation = await _context.Conversations.FindAsync(id);
            if (conversation == null)
                return NoContent();

            // Remove associated messages then the conversation row
            var messages = _context.ChatMessages.Where(m => m.ConversationId == id);
            _context.ChatMessages.RemoveRange(messages);
            _context.Conversations.Remove(conversation);

            await _context.SaveChangesAsync();

            await InvalidateGroupsCache();
            await NotifyGroupsChangedAsync();
            await NotifyConversationsChangedAsync();

            _logger.LogInformation("Permanently deleted conversation {ConversationId}", id);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting conversation {ConversationId}", id);
            return StatusCode(500, new { error = "Failed to delete conversation", details = ex.Message });
        }
    }

    private Task NotifyGroupsChangedAsync()
    {
        return _resourceHub != null
            ? _resourceHub.Clients.All.SendAsync("groupsChanged")
            : Task.CompletedTask;
    }

    private Task NotifyConversationsChangedAsync()
    {
        return _resourceHub != null
            ? _resourceHub.Clients.All.SendAsync("conversationsChanged")
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
}

public class CreateConversationRequest
{
    public string? Title { get; set; }
    public Guid? ResourceGroupId { get; set; }
}

public class AddMessageRequest
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Model { get; set; }
    public List<Guid>? DocumentIds { get; set; }
}

public class ConversationQueryRequest
{
    public string Query { get; set; } = string.Empty;
    public int? TopK { get; set; }
    public List<Guid>? GroupIds { get; set; }
    public string? Model { get; set; }
}

public class UpdateConversationRequest
{
    public string? Title { get; set; }
}

public class UpdateConversationGroupRequest
{
    public Guid? ResourceGroupId { get; set; }
}
