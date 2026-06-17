namespace BookOfTheEnd.Models.Health;

public enum SectorStatus
{
    Unscanned,
    Healthy,
    Slow,
    Bad
}

/// <summary>One contiguous block of the volume classified by read behavior.</summary>
public sealed class SurfaceBlock
{
    public long OffsetBytes { get; init; }
    public long LengthBytes { get; init; }
    public SectorStatus Status { get; set; } = SectorStatus.Unscanned;
    public double ReadMs { get; set; }
}

/// <summary>Aggregate result of a read-only surface scan, plus the per-block map.</summary>
public sealed class SurfaceScanResult
{
    public long TotalBytesScanned { get; init; }
    public int HealthyBlocks { get; init; }
    public int SlowBlocks { get; init; }
    public int BadBlocks { get; init; }
    public bool Completed { get; init; }
    public IReadOnlyList<SurfaceBlock> Blocks { get; init; } = Array.Empty<SurfaceBlock>();

    /// <summary>Optional message (e.g. why the scan could not run).</summary>
    public string? Note { get; init; }

    public bool HasBadSectors => BadBlocks > 0;
}
