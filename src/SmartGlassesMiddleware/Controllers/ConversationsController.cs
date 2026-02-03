using Microsoft.AspNetCore.Mvc;
using SmartGlassesMiddleware.Services;
using SmartGlassesMiddleware.Models;
using Microsoft.EntityFrameworkCore;

namespace SmartGlassesMiddleware.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConversationsController : ControllerBase
{
    private readonly ILogger<ConversationsController> _logger;
    private readonly IConversationService _conversationService;
    private readonly IRAGService _ragService;

    public ConversationsController(
        ILogger<ConversationsController> logger,
        IConversationService conversationService,
        IRAGService ragService)
    {
        _logger = logger;
        _conversationService = conversationService;
        _ragService = ragService;
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
        var conversation = await _conversationService.GetConversationAsync(id);
        
        if (conversation == null)
            return NotFound();

        return Ok(conversation);
    }

    /// <summary>
    /// Get all active conversations
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<Conversation>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConversations([FromQuery] Guid? groupId = null)
    {
        var conversations = await _conversationService.GetActiveConversationsAsync(groupId);
        return Ok(conversations);
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
                request.Model);
            
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
            // Add user message to conversation
            await _conversationService.AddMessageAsync(id, "user", request.Query);

            // Query with conversation context
            var result = await _ragService.QueryDocumentsWithConversationAsync(
                request.Query, 
                id, 
                request.TopK ?? 5, 
                request.GroupIds);

            // Add assistant response to conversation
            await _conversationService.AddMessageAsync(
                id, 
                "assistant", 
                result.Answer, 
                request.Model ?? "default");

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying with conversation context");
            return StatusCode(500, new { error = "Failed to query with conversation", details = ex.Message });
        }
    }

    /// <summary>
    /// Summarize a conversation
    /// </summary>
    [HttpPost("{id}/summarize")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SummarizeConversation(Guid id)
    {
        try
        {
            var summary = await _conversationService.SummarizeConversationAsync(id);
            return Ok(new { summary });
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
        await _conversationService.ArchiveConversationAsync(id);
        return NoContent();
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
}

public class ConversationQueryRequest
{
    public string Query { get; set; } = string.Empty;
    public int? TopK { get; set; }
    public List<Guid>? GroupIds { get; set; }
    public string? Model { get; set; }
}
