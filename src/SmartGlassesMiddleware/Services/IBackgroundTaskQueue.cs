using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Services;

public interface IBackgroundTaskQueue
{
    Task EnqueueAsync(BackgroundTaskEntry entry);
    ValueTask<BackgroundTaskEntry?> DequeueAsync(CancellationToken cancellationToken);
    BackgroundTaskEntry? Get(Guid id);
    Task UpdateAsync(BackgroundTaskEntry entry);
}
