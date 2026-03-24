using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Services;

/// <summary>
/// Orchestrates the AI processing pipeline for a background media task
/// (transcription, image analysis, document RAG query, etc.).
/// </summary>
public interface IMediaProcessor
{
    /// <summary>
    /// Execute all processing steps for the given background task entry and return
    /// the result payload that will be stored on the task record.
    /// </summary>
    /// <param name="entry">The background task entry containing request data and metadata.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>An object representing the processing result (serialised to JSON for the task record).</returns>
    Task<object> ProcessBackgroundTaskAsync(BackgroundTaskEntry entry, CancellationToken ct = default);
}
