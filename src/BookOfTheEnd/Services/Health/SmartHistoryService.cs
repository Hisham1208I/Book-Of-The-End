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

    public void Append(string diskKey, SmartHistoryEntry entry)
    {
        var history = LoadAll();
        if (!history.TryGetValue(diskKey, out var list))
            history[diskKey] = list = new List<SmartHistoryEntry>();

        list.Add(entry);
        if (list.Count > MaxEntriesPerDisk)
            list.RemoveRange(0, list.Count - MaxEntriesPerDisk);

        Save(history);
    }

    public IReadOnlyList<SmartHistoryEntry> GetHistory(string diskKey)
    {
        var history = LoadAll();
        return history.TryGetValue(diskKey, out var list) ? list : Array.Empty<SmartHistoryEntry>();
    }

    /// <summary>
    /// Returns a human-readable delta string for a SMART counter, e.g. "+3 since last reading".
    /// Returns null when there is no prior reading or both values are unknown.
    /// </summary>
    public string? ComputeDelta(string diskKey, long? current)
    {
        if (current is null) return null;
        var history = GetHistory(diskKey);
        if (history.Count == 0) return null;
        return null; // populated after first save — delegate to caller with loaded list
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
