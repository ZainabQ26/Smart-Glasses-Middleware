using Microsoft.AspNetCore.Mvc;
using SmartGlassesMiddleware.Services;
using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Controllers;

[ApiController]
[Route("api/conversations/mapping")]
    public class GroupConversationMappingsController : ControllerBase
{
    private readonly IGroupConversationMappingService _mappingService;

    public GroupConversationMappingsController(IGroupConversationMappingService mappingService)
    {
        _mappingService = mappingService;
    }

    // GET /api/conversations/mapping?groupId={groupId}
    [HttpGet]
    public async Task<IActionResult> GetMapping([FromQuery] Guid groupId)
    {
        if (groupId == Guid.Empty) return BadRequest("groupId is required");
        var conv = await _mappingService.GetConversationIdForGroupAsync(groupId);
        if (!conv.HasValue) return NotFound();
        return Ok(new { ResourceGroupId = groupId, ConversationId = conv.Value });
    }

    // PUT /api/conversations/mapping
    [HttpPut]
    public async Task<IActionResult> UpsertMapping([FromBody] GroupConversationMapping dto)
    {
        if (dto == null || dto.ResourceGroupId == Guid.Empty || dto.ConversationId == Guid.Empty)
            return BadRequest("ResourceGroupId and ConversationId are required");

        await _mappingService.UpsertMappingAsync(dto.ResourceGroupId, dto.ConversationId);
        return NoContent();
    }

    // DELETE /api/conversations/mapping?groupId={groupId}
    [HttpDelete]
    public async Task<IActionResult> DeleteMapping([FromQuery] Guid groupId)
    {
        if (groupId == Guid.Empty) return BadRequest("groupId is required");
        await _mappingService.DeleteMappingAsync(groupId);
        return NoContent();
    }
}
