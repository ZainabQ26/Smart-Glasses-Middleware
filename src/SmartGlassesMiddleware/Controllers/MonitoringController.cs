using Microsoft.AspNetCore.Mvc;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MonitoringController : ControllerBase
{
    private readonly IRequestMonitoringService _monitoringService;
    private readonly ILogger<MonitoringController> _logger;

    public MonitoringController(IRequestMonitoringService monitoringService, ILogger<MonitoringController> logger)
    {
        _monitoringService = monitoringService;
        _logger = logger;
    }

    /// <summary>
    /// Get recent requests with their status
    /// </summary>
    [HttpGet("requests")]
    [ProducesResponseType(typeof(List<RequestStatus>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRecentRequests([FromQuery] int count = 50)
    {
        try
        {
            var requests = await _monitoringService.GetRecentRequests(count);
            return Ok(requests);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting recent requests");
            return StatusCode(500, new { error = "Failed to get requests", details = ex.Message });
        }
    }
}
