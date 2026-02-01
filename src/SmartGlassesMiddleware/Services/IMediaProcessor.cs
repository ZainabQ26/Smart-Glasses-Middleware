using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Services;

public interface IMediaProcessor
{
    Task<object> ProcessBackgroundTaskAsync(BackgroundTaskEntry entry, CancellationToken ct = default);
}
