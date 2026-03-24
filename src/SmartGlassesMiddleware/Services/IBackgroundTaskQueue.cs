using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Services;

/// <summary>
/// A thread-safe queue for enqueuing, dequeuing, and inspecting background processing tasks.
/// </summary>
public interface IBackgroundTaskQueue
{
    /// <summary>
    /// Enqueue a new background task entry for processing.
    /// </summary>
    /// <param name="entry">The task entry to enqueue.</param>
    Task EnqueueAsync(BackgroundTaskEntry entry);

    /// <summary>
    /// Dequeue the next pending task, waiting asynchronously until one is available
    /// or the <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the wait.</param>
    /// <returns>The next <see cref="BackgroundTaskEntry"/>, or <c>null</c> if the queue is stopped.</returns>
    ValueTask<BackgroundTaskEntry?> DequeueAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Retrieve a task entry by its unique identifier without removing it from the queue.
    /// </summary>
    /// <param name="id">The task ID to look up.</param>
    /// <returns>The matching entry, or <c>null</c> if not found.</returns>
    BackgroundTaskEntry? Get(Guid id);

    /// <summary>
    /// Persist updated state (status, result, error) for an existing task entry.
    /// </summary>
    /// <param name="entry">The entry with updated fields to persist.</param>
    Task UpdateAsync(BackgroundTaskEntry entry);
}
