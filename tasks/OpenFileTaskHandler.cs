using SmartGlassesMiddleware.Services;
using SmartGlassesMiddleware.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

public class OpenFileTaskHandler : ITaskHandler
{
    private readonly ApplicationDbContext _context;
    private static readonly Regex FileIndexRegex = new(@"\b(?:file|document)\s*(?:number\s*)?(\d{1,2})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string Name => "open-file";

    public OpenFileTaskHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<string> HandleAsync(string input)
    {
        var recentFiles = await _context.ManagedFiles
            .AsNoTracking()
            .Where(f => !string.IsNullOrWhiteSpace(f.StorageKey) && f.FileSize > 0)
            .OrderByDescending(f => f.UploadedAt)
            .Take(10)
            .Select(f => new
            {
                f.Id,
                DisplayName = string.IsNullOrWhiteSpace(f.Label) ? f.FileName : f.Label
            })
            .ToListAsync();

        if (recentFiles.Count == 0)
            return "No uploaded files found. Please upload a file first.";

        var requestedIndex = ParseRequestedIndex(input);
        if (requestedIndex.HasValue)
        {
            var idx = requestedIndex.Value - 1;
            if (idx >= 0 && idx < recentFiles.Count)
            {
                var selected = recentFiles[idx];
                return $"__ACTION:SELECT_DOCUMENT:{selected.Id}:{selected.DisplayName}";
            }
        }

        return "__ACTION:SHOW_RECENT_FILES__";
    }

    private static int? ParseRequestedIndex(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var indexedMatch = FileIndexRegex.Match(input);
        if (indexedMatch.Success && int.TryParse(indexedMatch.Groups[1].Value, out var numberedIndex) && numberedIndex > 0)
            return numberedIndex;

        var anyNumberMatch = Regex.Match(input, @"\b(\d{1,2})\b");
        if (anyNumberMatch.Success && int.TryParse(anyNumberMatch.Groups[1].Value, out var fallbackIndex) && fallbackIndex > 0)
            return fallbackIndex;

        return null;
    }
}
