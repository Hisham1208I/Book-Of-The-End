namespace BookOfTheEnd.Models.Health;

public enum HealthStatus
{
    Unknown,
    Excellent,
    Good,
    Warning,
    Critical,
    Failing
}

public enum RiskLevel
{
    Low,
    Moderate,
    High,
    Emergency
}

public enum ClonePriority
{
    Low,
    Moderate,
    High,
    Emergency
}

/// <summary>
/// The full health assessment for the physical disk backing a selected volume:
/// identity, SMART snapshot, computed health, and the recovery-readiness decision.
/// </summary>
public sealed class DeviceHealth
{
    public string DriveLetter { get; init; } = "";
    public string Model { get; init; } = "";
    public string Serial { get; init; } = "";
    public string Interface { get; init; } = "";
    public long CapacityBytes { get; init; }
    public bool IsSsd { get; init; }

    public SmartReadings Smart { get; init; } = new();

    /// <summary>Overall health score, 0 (failing) to 100 (excellent).</summary>
    public int HealthScore { get; init; }
    public HealthStatus Status { get; init; } = HealthStatus.Unknown;
    public RiskLevel RiskLevel { get; init; } = RiskLevel.Low;
    public ClonePriority ClonePriority { get; init; } = ClonePriority.Low;
    public RecoveryReadiness Readiness { get; init; } = new();

    /// <summary>Human-readable findings that drove the assessment.</summary>
    public IReadOnlyList<string> Findings { get; init; } = Array.Empty<string>();

    public bool DataAvailable => Smart.HasData;

    // --- Display helpers ---
    public string CapacityDisplay => CapacityBytes > 0 ? HumanSize.Format(CapacityBytes) : "—";
    public string InterfaceDisplay => string.IsNullOrWhiteSpace(Interface) ? "Unknown" : Interface;
    public string KindDisplay => IsSsd ? "SSD" : "HDD";
    public string TitleDisplay => string.IsNullOrWhiteSpace(Model) ? $"{DriveLetter}:" : Model;

    public string TemperatureDisplay => Smart.TemperatureC is { } t ? $"{t} °C" : "—";
    public string PowerOnHoursDisplay => Smart.PowerOnHours is { } h ? $"{h:N0} h" : "—";
    public string PowerCyclesDisplay => Smart.PowerCycles is { } c ? $"{c:N0}" : "—";
    public string ReallocatedDisplay => Smart.ReallocatedSectors is { } r ? $"{r:N0}" : "—";
    public string PendingDisplay => Smart.PendingSectors is { } p ? $"{p:N0}" : "—";
    public string RemainingLifeDisplay => Smart.RemainingLifePercent is { } l ? $"{l}%" : "—";
    public string WearDisplay => Smart.WearPercentUsed is { } w ? $"{w}% used" : "—";

    public string StatusLabel => Status.ToString();
    public string ClonePriorityLabel => ClonePriority.ToString();
    public string RiskLabel => RiskLevel.ToString();
}
