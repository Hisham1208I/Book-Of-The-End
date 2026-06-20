namespace BookOfTheEnd.Models.Health;

/// <summary>
/// A persisted point-in-time snapshot of SMART readings for trend tracking.
/// Stored per-disk in %AppData%\Book of the End\smart-history.json.
/// </summary>
public sealed class SmartHistoryEntry
{
    public DateTime Timestamp { get; set; }
    public string DriveLetter { get; set; } = "";
    public int? TemperatureC { get; set; }
    public long? ReallocatedSectors { get; set; }
    public long? PendingSectors { get; set; }
    public int? WearPercentUsed { get; set; }
    public long? PowerOnHours { get; set; }
    public int HealthScore { get; set; }
}
