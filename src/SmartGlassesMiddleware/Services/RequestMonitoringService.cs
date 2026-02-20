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
    Task AddActivity(string requestId, string activity, int? progress = null);
    Task CompleteRequest(string requestId, bool success, string? result = null, string? error = null);
    Task<List<RequestStatus>> GetRecentRequests(int count = 50);
    Task ClearAll();
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
                // Add activity to history for streaming
                request.ActivityHistory.Add(new RequestActivity
                {
                    Timestamp = DateTime.UtcNow,
                    Activity = details,
                    Progress = progress
                });
            }
            if (progress.HasValue)
            {
                request.Progress = progress.Value;
            }
            request.LastUpdateTime = DateTime.UtcNow;
            await BroadcastUpdate(request);
            _logger.LogInformation("Updated request {RequestId}: {Status} - {Details}", requestId, status, details);
        }
    }

    public async Task AddActivity(string requestId, string activity, int? progress = null)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            _logger.LogWarning("AddActivity called with null or empty requestId");
            return;
        }

        if (_activeRequests.TryGetValue(requestId, out var request))
        {
            // Add activity to history
            request.ActivityHistory.Add(new RequestActivity
            {
                Timestamp = DateTime.UtcNow,
                Activity = activity,
                Progress = progress
            });
            
            // Update current details and progress
            request.Details = activity;
            if (progress.HasValue)
            {
                request.Progress = progress.Value;
            }
            request.LastUpdateTime = DateTime.UtcNow;
            
            // Broadcast detailed activity update (streaming)
            await BroadcastActivityUpdate(requestId, activity, progress);
            _logger.LogDebug("Added activity to request {RequestId}: {Activity}", requestId, activity);
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

    public async Task ClearAll()
    {
        // Clear in-memory state
        _activeRequests.Clear();
        while (_recentRequests.TryDequeue(out _)) { }
        _initialized = false; // force reload (from now-empty DB) on next access

        // Delete persisted monitoring records from the database
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.RequestStatuses.ExecuteDeleteAsync();
            await db.RequestHistories.ExecuteDeleteAsync();
            _logger.LogInformation("Cleared all request monitoring data from database");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear request monitoring data from database");
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

    private async Task BroadcastActivityUpdate(string requestId, string activity, int? progress)
    {
        try
        {
            // Use Task.Run to prevent blocking
            _ = Task.Run(async () =>
            {
                try
                {
                    await _hubContext.Clients.All.SendAsync("ActivityUpdate", new
                    {
                        requestId,
                        activity,
                        progress,
                        timestamp = DateTime.UtcNow
                    });
                    _logger.LogDebug("Broadcast activity update for {RequestId}: {Activity}", requestId, activity);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to broadcast activity update for {RequestId} via SignalR", requestId);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error initiating activity broadcast for request {RequestId}", requestId);
        }
        
        await Task.CompletedTask;
    }
}
