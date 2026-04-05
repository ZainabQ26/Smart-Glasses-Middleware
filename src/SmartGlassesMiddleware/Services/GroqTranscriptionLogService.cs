namespace SmartGlassesMiddleware.Services;

public sealed class GroqTranscriptionEntry
{
    public string Text { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
}

public interface IGroqTranscriptionLogService
{
    void Add(string text, string type);
    IReadOnlyList<GroqTranscriptionEntry> GetRecent(int maxCount = 200);
    void Clear();
}

public sealed class GroqTranscriptionLogService : IGroqTranscriptionLogService
{
    private readonly object _lock = new object();
    private const int MaxEntries = 200;
    private readonly LinkedList<GroqTranscriptionEntry> _entries = new();

    public void Add(string text, string type)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        lock (_lock)
        {
            _entries.AddLast(new GroqTranscriptionEntry
            {
                Text = text.Trim(),
                Type = type,
                TimestampUtc = DateTime.UtcNow
            });
            while (_entries.Count > MaxEntries)
                _entries.RemoveFirst();
        }
    }

    public IReadOnlyList<GroqTranscriptionEntry> GetRecent(int maxCount = 200)
    {
        lock (_lock)
        {
            return _entries.TakeLast(Math.Min(maxCount, _entries.Count)).ToList();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
    }
}
