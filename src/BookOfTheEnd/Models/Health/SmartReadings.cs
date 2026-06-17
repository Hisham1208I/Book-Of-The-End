namespace BookOfTheEnd.Models.Health;

/// <summary>
/// A point-in-time snapshot of a physical disk's SMART / reliability attributes.
/// All values are nullable because availability varies by bus type, driver, and
/// privilege level; consumers must degrade gracefully when a field is null.
/// </summary>
public sealed class SmartReadings
{
    public int? TemperatureC { get; init; }
    public int? TemperatureMaxC { get; init; }
    public long? PowerOnHours { get; init; }
    public long? PowerCycles { get; init; }

    /// <summary>SATA SMART attribute 0x05 (reallocated sector count).</summary>
    public long? ReallocatedSectors { get; init; }

    /// <summary>SATA SMART attribute 0xC5 (current pending sector count).</summary>
    public long? PendingSectors { get; init; }

    /// <summary>SSD/NVMe wear as percentage used (0 = new, 100 = fully worn).</summary>
    public int? WearPercentUsed { get; init; }

    /// <summary>Remaining life percentage (100 - wear) when wear is known.</summary>
    public int? RemainingLifePercent => WearPercentUsed is { } w ? Math.Clamp(100 - w, 0, 100) : null;

    public long? ReadErrorsTotal { get; init; }
    public long? ReadErrorsUncorrected { get; init; }

    /// <summary>True when the drive's firmware predicts imminent failure.</summary>
    public bool PredictFailure { get; init; }

    /// <summary>Raw HealthStatus reported by MSFT_PhysicalDisk (Healthy / Warning / Unhealthy).</summary>
    public string? WmiHealthStatus { get; init; }

    /// <summary>False when no readings could be collected at all.</summary>
    public bool HasData { get; init; }

    /// <summary>Optional human-readable note (e.g. why data is missing).</summary>
    public string? Note { get; init; }
}
