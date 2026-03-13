using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Controllers;

[ApiController]
[Route("api/ai-interactions")]
public class AIInteractionController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<AIInteractionController> _logger;

    public AIInteractionController(ApplicationDbContext context, ILogger<AIInteractionController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get AI interaction logs with optional filtering
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> GetInteractionLogs(
        [FromQuery] Guid? groupId = null,
        [FromQuery] string? model = null,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0)
    {
        try
        {
            var query = _context.AIInteractionLogs
                .Include(log => log.ResourceGroup)
                .Include(log => log.Conversation)
                .AsQueryable();

            if (groupId.HasValue)
            {
                query = query.Where(log => log.ResourceGroupId == groupId.Value);
            }

            if (!string.IsNullOrEmpty(model))
            {
                query = query.Where(log => log.ModelUsed == model);
            }

            var logs = await query
                .OrderByDescending(log => log.Timestamp)
                .Skip(offset)
                .Take(limit)
                .Select(log => new
                {
                    log.Id,
                    log.ModelUsed,
                    log.DelegationReason,
                    log.InputTokens,
                    log.OutputTokens,
                    log.TotalTokens,
                    log.EstimatedCost,
                    log.Timestamp,
                    log.ResponseTimeMs,
                    log.Success,
                    log.ErrorMessage,
                    ResourceGroupName = log.ResourceGroup != null ? log.ResourceGroup.Name : null,
                    ConversationTitle = log.Conversation != null ? log.Conversation.Title : null,
                    // Truncate input/output for list view
                    InputPreview = string.IsNullOrEmpty(log.Input) ? string.Empty : (log.Input.Length > 200 ? log.Input.Substring(0, 200) + "..." : log.Input),
                    OutputPreview = string.IsNullOrEmpty(log.Output) ? string.Empty : (log.Output.Length > 200 ? log.Output.Substring(0, 200) + "..." : log.Output)
                })
                .ToListAsync();

            return Ok(logs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving AI interaction logs");
            return StatusCode(500, new { error = "Failed to retrieve interaction logs" });
        }
    }

    /// <summary>
    /// Get detailed information about a specific AI interaction
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<AIInteractionLog>> GetInteractionLog(Guid id)
    {
        try
        {
            var log = await _context.AIInteractionLogs
                .Include(l => l.ResourceGroup)
                .Include(l => l.Conversation)
                .FirstOrDefaultAsync(l => l.Id == id);

            if (log == null)
            {
                return NotFound(new { error = "Interaction log not found" });
            }

            return Ok(log);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving AI interaction log {Id}", id);
            return StatusCode(500, new { error = "Failed to retrieve interaction log" });
        }
    }

    /// <summary>
    /// Get statistics about AI model usage
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult<object>> GetStats(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] Guid? groupId = null)
    {
        try
        {
            var query = _context.AIInteractionLogs.AsQueryable();

            if (startDate.HasValue)
            {
                query = query.Where(log => log.Timestamp >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                query = query.Where(log => log.Timestamp <= endDate.Value);
            }

            if (groupId.HasValue)
            {
                query = query.Where(log => log.ResourceGroupId == groupId.Value);
            }

            var stats = await query.GroupBy(log => log.ModelUsed)
                .Select(g => new
                {
                    Model = g.Key,
                    Count = g.Count(),
                    TotalTokens = g.Sum(log => log.TotalTokens),
                    AvgResponseTimeMs = g.Average(log => log.ResponseTimeMs),
                    TotalCost = g.Sum(log => log.EstimatedCost),
                    SuccessRate = (double)g.Count(log => log.Success) / g.Count() * 100
                })
                .ToListAsync();

            var delegationStats = await query
                .Where(log => !string.IsNullOrEmpty(log.DelegationReason))
                .GroupBy(log => log.DelegationReason)
                .Select(g => new
                {
                    Reason = g.Key,
                    Count = g.Count()
                })
                .ToListAsync();

            return Ok(new
            {
                modelStats = stats,
                delegationReasons = delegationStats,
                totalInteractions = await query.CountAsync()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating AI interaction stats");
            return StatusCode(500, new { error = "Failed to calculate stats" });
        }
    }
}
