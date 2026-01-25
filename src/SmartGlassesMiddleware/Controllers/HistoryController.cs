using Microsoft.AspNetCore.Mvc;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HistoryController : ControllerBase
{
    private readonly IHistoryService _historyService;
    private readonly ILogger<HistoryController> _logger;

    public HistoryController(IHistoryService historyService, ILogger<HistoryController> logger)
    {
        _historyService = historyService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<RequestHistory>>> GetRecentHistory([FromQuery] int count = 10)
    {
        try
        {
            var history = await _historyService.GetRecentHistoryAsync(count);
            return Ok(history);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting recent history");
            return StatusCode(500, "An error occurred retrieving history");
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<RequestHistory>> GetHistoryById(Guid id)
    {
        try
        {
            var history = await _historyService.GetHistoryByIdAsync(id);
            if (history == null)
            {
                return NotFound();
            }
            return Ok(history);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting history by ID: {Id}", id);
            return StatusCode(500, "An error occurred retrieving history");
        }
    }
}
