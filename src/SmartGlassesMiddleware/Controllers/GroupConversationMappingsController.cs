using Microsoft.AspNetCore.Mvc;
using SmartGlassesMiddleware.Services;
using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Controllers;

/// <summary>
/// Manages the mapping between a resource group and its associated conversation thread.
/// Each resource group can be linked to exactly one conversation, allowing all files
/// and prompts in that group to share a persistent conversation context.
/// </summary>
[ApiController]
[Route("api/conversations/mapping")]
    public class GroupConversationMappingsController : ControllerBase
{
    private readonly IGroupConversationMappingService _mappingService;

    public GroupConversationMappingsController(IGroupConversationMappingService mappingService)
    {
        _mappingService = mappingService;
    }

    /// <summary>
    /// Get the conversation linked to a resource group.
    /// </summary>
    /// <param name="groupId">The resource group ID.</param>
    /// <returns>The group ID and its linked conversation ID.</returns>
    // GET /api/conversations/mapping?groupId={groupId}
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetMapping([FromQuery] Guid groupId)
    {
        if (groupId == Guid.Empty) return BadRequest("groupId is required");
        try
        {
            var conv = await _mappingService.GetConversationIdForGroupAsync(groupId);
            if (!conv.HasValue) return NotFound();
            return Ok(new { ResourceGroupId = groupId, ConversationId = conv.Value });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to retrieve mapping", details = ex.Message });
        }
    }

    /// <summary>
    /// Create or update the mapping between a resource group and a conversation.
    /// </summary>
    /// <param name="dto">The mapping to upsert.</param>
    // PUT /api/conversations/mapping
    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UpsertMapping([FromBody] GroupConversationMapping dto)
    {
        if (dto == null || dto.ResourceGroupId == Guid.Empty || dto.ConversationId == Guid.Empty)
            return BadRequest("ResourceGroupId and ConversationId are required");
        try
        {
            await _mappingService.UpsertMappingAsync(dto.ResourceGroupId, dto.ConversationId);
            return NoContent();
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to upsert mapping", details = ex.Message });
        }
    }

    /// <summary>
    /// Remove the conversation mapping for a resource group.
    /// </summary>
    /// <param name="groupId">The resource group ID whose mapping should be deleted.</param>
    // DELETE /api/conversations/mapping?groupId={groupId}
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteMapping([FromQuery] Guid groupId)
    {
        if (groupId == Guid.Empty) return BadRequest("groupId is required");
        try
        {
            await _mappingService.DeleteMappingAsync(groupId);
            return NoContent();
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to delete mapping", details = ex.Message });
        }
    }
}
