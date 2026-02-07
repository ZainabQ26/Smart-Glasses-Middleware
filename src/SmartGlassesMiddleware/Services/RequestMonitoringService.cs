using Microsoft.AspNetCore.SignalR;
using SmartGlassesMiddleware.Hubs;
using System.Collections.Concurrent;

namespace SmartGlassesMiddleware.Services;

public interface IRequestMonitoringService
{
    Task<string> StartRequest(string requestType, string description);
    Task UpdateRequestStatus(string requestId, string status, string? details = null);
    Task CompleteRequest(string requestId, bool success, string? result = null, string? error = null);
    Task<List<RequestStatus>> GetRecentRequests(int count = 50);
}

public class RequestMonitoringService : IRequestMonitoringService
{
    private readonly ConcurrentDictionary<string, RequestStatus> _activeRequests = new();
    private readonly ConcurrentQueue<RequestStatus> _recentRequests = new();
    private readonly IHubContext<TaskHub> _hubContext;
    private readonly ILogger<RequestMonitoringService> _logger;
    private const int MaxRecentRequests = 100;

    public RequestMonitoringService(IHubContext<TaskHub> hubContext, ILogger<RequestMonitoringService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task<string> StartRequest(string requestType, string description)
    {
        var requestId = Guid.NewGuid().ToString();
        var request = new RequestStatus
        {
            RequestId = requestId,
            RequestType = requestType,
            Description = description,
            Status = "Started",
            StartTime = DateTime.UtcNow
        };

        _activeRequests[requestId] = request;
        _recentRequests.Enqueue(request);

        // Trim old requests
        while (_recentRequests.Count > MaxRecentRequests)
        {
            _recentRequests.TryDequeue(out _);
        }

        await BroadcastUpdate(request);
        _logger.LogInformation("Started request {RequestId}: {Description}", requestId, description);
        return requestId;
    }

    public async Task UpdateRequestStatus(string requestId, string status, string? details = null)
    {
        if (_activeRequests.TryGetValue(requestId, out var request))
        {
            request.Status = status;
            if (details != null)
            {
                request.Details = details;
            }
            request.LastUpdateTime = DateTime.UtcNow;
            await BroadcastUpdate(request);
            _logger.LogInformation("Updated request {RequestId}: {Status}", requestId, status);
        }
    }

    public async Task CompleteRequest(string requestId, bool success, string? result = null, string? error = null)
    {
        if (_activeRequests.TryRemove(requestId, out var request))
        {
            request.Status = success ? "Completed" : "Failed";
            request.Success = success;
            request.Result = result;
            request.Error = error;
            request.EndTime = DateTime.UtcNow;
            request.DurationMs = (long)(request.EndTime.Value - request.StartTime).TotalMilliseconds;

            await BroadcastUpdate(request);
            _logger.LogInformation("Completed request {RequestId}: Success={Success}, Duration={Duration}ms", 
                requestId, success, request.DurationMs);
        }
    }

    public Task<List<RequestStatus>> GetRecentRequests(int count = 50)
    {
        var requests = _recentRequests
            .Reverse()
            .Take(count)
            .ToList();
        return Task.FromResult(requests);
    }

    private async Task BroadcastUpdate(RequestStatus request)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync("RequestUpdate", request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting request update for {RequestId}", request.RequestId);
        }
    }
}

public class RequestStatus
{
    public string RequestId { get; set; } = string.Empty;
    public string RequestType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Details { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? LastUpdateTime { get; set; }
    public DateTime? EndTime { get; set; }
    public long DurationMs { get; set; }
    public bool Success { get; set; }
    public string? Result { get; set; }
    public string? Error { get; set; }
}
