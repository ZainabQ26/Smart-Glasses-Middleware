using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SmartGlassesMiddleware.Hubs;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Data;
using System.Collections.Concurrent;

namespace SmartGlassesMiddleware.Services;

public interface IRequestMonitoringService
{
    Task<string> StartRequest(string requestType, string description, string? requestId = null);
    Task UpdateRequestStatus(string requestId, string status, string? details = null, int? progress = null);
    Task CompleteRequest(string requestId, bool success, string? result = null, string? error = null);
    Task<List<RequestStatus>> GetRecentRequests(int count = 50);
}

public class RequestMonitoringService : IRequestMonitoringService
{
    private readonly ConcurrentDictionary<string, RequestStatus> _activeRequests = new();
    private readonly ConcurrentQueue<RequestStatus> _recentRequests = new();
    private readonly IHubContext<TaskHub> _hubContext;
    private readonly ILogger<RequestMonitoringService> _logger;
    private readonly int _maxRecentRequests;
    private readonly IServiceScopeFactory _scopeFactory;
    private bool _initialized = false;

    public RequestMonitoringService(
        IHubContext<TaskHub> hubContext, 
        ILogger<RequestMonitoringService> logger, 
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory)
    {
        _hubContext = hubContext;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _maxRecentRequests = configuration.GetValue<int?>("Monitoring:MaxRecentRequests") ?? 100;
    }

    public async Task<string> StartRequest(string requestType, string description, string? requestId = null)
    {
        await EnsureInitializedAsync();
        
        var generatedRequestId = requestId ?? Guid.NewGuid().ToString();
        var request = new RequestStatus
        {
            Id = Guid.NewGuid(),
            RequestId = generatedRequestId,
            RequestType = requestType,
            Description = description,
            Status = "Started",
            StartTime = DateTime.UtcNow
        };

        _activeRequests[request.RequestId] = request;
        _recentRequests.Enqueue(request);

        // Trim old requests
        while (_recentRequests.Count > _maxRecentRequests)
        {
            _recentRequests.TryDequeue(out _);
        }

        _logger.LogInformation("[DIAG] About to broadcast for request {RequestId}", generatedRequestId);
        await BroadcastUpdate(request);
        _logger.LogInformation("Started request {RequestId}: {Description}", generatedRequestId, description);
        return generatedRequestId;
    }

    public async Task UpdateRequestStatus(string requestId, string status, string? details = null, int? progress = null)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            _logger.LogWarning("UpdateRequestStatus called with null or empty requestId");
            return;
        }

        if (_activeRequests.TryGetValue(requestId, out var request))
        {
            request.Status = status;
            if (details != null)
            {
                request.Details = details;
            }
            if (progress.HasValue)
            {
                request.Progress = progress.Value;
            }
            request.LastUpdateTime = DateTime.UtcNow;
            await BroadcastUpdate(request);
            _logger.LogInformation("Updated request {RequestId}: {Status}", requestId, status);
        }
    }

    public async Task CompleteRequest(string requestId, bool success, string? result = null, string? error = null)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            _logger.LogWarning("CompleteRequest called with null or empty requestId");
            return;
        }

        if (_activeRequests.TryRemove(requestId, out var request))
        {
            request.Status = success ? "Completed" : "Failed";
            request.Success = success;
            request.Result = result;
            request.Error = error;
            request.EndTime = DateTime.UtcNow;
            request.DurationMs = (long)(request.EndTime.Value - request.StartTime).TotalMilliseconds;

            // Ensure UI progress reaches 100% on completion
            try { request.Progress = 100; } catch { }

            // Add to recent requests queue
            _recentRequests.Enqueue(request);
            
            // Trim old requests
            while (_recentRequests.Count > _maxRecentRequests)
            {
                _recentRequests.TryDequeue(out _);
            }

            await BroadcastUpdate(request);
            
            // Persist to database using a scoped DbContext
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.RequestStatuses.Add(request);
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist request status to database for {RequestId}", requestId);
            }
            
            _logger.LogInformation("Completed request {RequestId}: Success={Success}, Duration={Duration}ms", 
                requestId, success, request.DurationMs);
        }
    }

    public async Task<List<RequestStatus>> GetRecentRequests(int count = 200)
    {
        await EnsureInitializedAsync();
        
        // Prioritize active requests, then recent completed requests
        var activeList = _activeRequests.Values.ToList();
        var recentList = _recentRequests
            .OrderByDescending(r => r.LastUpdateTime ?? r.StartTime)
            .ToList();
        
        // Combine: all active + as many recent as needed to reach count
        var combined = activeList
            .Concat(recentList)
            .DistinctBy(r => r.RequestId)
            .Take(count)
            .ToList();
            
        _logger.LogInformation("[DIAG] GetRecentRequests: active={Active}, recent={Recent}, combined={Combined}", 
            activeList.Count, _recentRequests.Count, combined.Count);
        return combined;
    }
    
    private async Task EnsureInitializedAsync()
    {
        if (_initialized) return;
        
        try
        {
            // Load recent completed requests from database using a scoped DbContext
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var dbRequests = await db.RequestStatuses
                    .OrderByDescending(r => r.StartTime)
                    .Take(_maxRecentRequests)
                    .ToListAsync();

                foreach (var request in dbRequests.OrderBy(r => r.StartTime))
                {
                    _recentRequests.Enqueue(request);
                }

                _logger.LogInformation("Loaded {Count} historical request statuses from database", dbRequests.Count);
                _initialized = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load historical request statuses from database");
            _initialized = true; // Mark as initialized anyway to prevent repeated attempts
        }
    }

    private async Task BroadcastUpdate(RequestStatus request)
    {
        try
        {
            // Use Task.Run to prevent blocking and potential deadlocks
            _ = Task.Run(async () =>
            {
                try
                {
                    await _hubContext.Clients.All.SendAsync("RequestUpdate", request);
                    _logger.LogDebug("Broadcast request update {RequestId} to SignalR clients", request.RequestId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to broadcast request update {RequestId} via SignalR", request.RequestId);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error initiating SignalR broadcast for request {RequestId}", request.RequestId);
        }
        
        await Task.CompletedTask;
    }
}
