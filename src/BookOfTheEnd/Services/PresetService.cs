using System.IO;
using System.Text.Json;
using BookOfTheEnd.Models;

namespace BookOfTheEnd.Services;

/// <summary>Persists scan presets to a local JSON file and seeds sensible defaults.</summary>
public sealed class PresetService
{
    private readonly LoggingService _log;
    private readonly string _path;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public PresetService(LoggingService log)
    {
        _log = log;
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BookOfTheEnd");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "presets.json");
    }

    public List<ScanPreset> Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                var list = JsonSerializer.Deserialize<List<ScanPreset>>(json, JsonOptions);
                if (list is { Count: > 0 }) return list;
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"Failed to load presets: {ex.Message}");
        }

        var seeded = DefaultPresets();
        Save(seeded);
        return seeded;
    }

    public void Save(IEnumerable<ScanPreset> presets)
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(presets.ToList(), JsonOptions));
        }
        catch (Exception ex)
        {
            _log.Error("Failed to save presets", ex);
        }
    }

    private static List<ScanPreset> DefaultPresets() => new()
    {
        new ScanPreset
        {
            Name = "Deep Video Recovery",
            Description = "Recover video footage from formatted cards",
            ScanType = ScanType.Deep,
            Categories = { FileCategory.Video }
        },
        new ScanPreset
        {
            Name = "Quick Document Scan",
            Description = "Fast recovery of office documents",
            ScanType = ScanType.Quick,
            Categories = { FileCategory.Document }
        },
        new ScanPreset
        {
            Name = "Photo Rescue",
            Description = "Deep scan targeting RAW and JPEG images",
            ScanType = ScanType.Deep,
            Categories = { FileCategory.Image }
        }
    };
}
