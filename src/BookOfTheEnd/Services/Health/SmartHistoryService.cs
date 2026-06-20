using System.IO;
using System.Text.Json;
using BookOfTheEnd.Models.Health;

namespace BookOfTheEnd.Services.Health;

/// <summary>
/// Persists SMART readings per physical disk to build trend data over time.
/// File: %AppData%\Book of the End\smart-history.json, keyed by device serial/model.
/// </summary>
public sealed class SmartHistoryService
{
    private const int MaxEntriesPerDisk = 168; // 7 days @ hourly refresh
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    private readonly LoggingService _log;
    private readonly string _path;

    public SmartHistoryService(LoggingService log)
    {
        _log = log;
        _path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Book of the End", "smart-history.json");
    }

    /// <summary>
    /// Loads history for <paramref name="diskKey"/>, appends <paramref name="entry"/>,
    /// saves, and returns the entry that was the last one before the append (for trend computation).
    /// Single file read + single file write per call.
    /// </summary>
    public SmartHistoryEntry? AppendAndGetPrevious(string diskKey, SmartHistoryEntry entry)
    {
        var history = LoadAll();
        if (!history.TryGetValue(diskKey, out var list))
            history[diskKey] = list = new List<SmartHistoryEntry>();

        SmartHistoryEntry? prev = list.Count > 0 ? list[^1] : null;

        list.Add(entry);
        if (list.Count > MaxEntriesPerDisk)
            list.RemoveRange(0, list.Count - MaxEntriesPerDisk);

        Save(history);
        return prev;
    }

    public IReadOnlyList<SmartHistoryEntry> GetHistory(string diskKey)
    {
        var history = LoadAll();
        return history.TryGetValue(diskKey, out var list) ? list : Array.Empty<SmartHistoryEntry>();
    }

    private Dictionary<string, List<SmartHistoryEntry>> LoadAll()
    {
        if (!File.Exists(_path)) return new();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, List<SmartHistoryEntry>>>(
                       File.ReadAllText(_path), JsonOpts)
                   ?? new();
        }
        catch (Exception ex)
        {
            _log.Warn($"Failed to load SMART history: {ex.Message}");
            return new();
        }
    }

    private void Save(Dictionary<string, List<SmartHistoryEntry>> data)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(data, JsonOpts));
        }
        catch (Exception ex)
        {
            _log.Warn($"Failed to save SMART history: {ex.Message}");
        }
    }
}
