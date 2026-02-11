using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Services;

public interface IDocumentTaskProcessor
{
    Task<object> ProcessBackgroundTaskAsync(BackgroundTaskEntry entry, CancellationToken ct = default);
}
