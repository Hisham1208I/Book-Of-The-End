namespace BookOfTheEnd.Models;

/// <summary>
/// A candidate file discovered by a scan. Holds enough location metadata for the
/// recovery service to reconstruct the bytes regardless of how it was found.
/// </summary>
public sealed class RecoverableFile
{
    public required RecoverySource Source { get; init; }

    /// <summary>Drive the file was found on, e.g. "C".</summary>
    public required string DriveLetter { get; init; }

    /// <summary>Best-known original file name (may be a generated fallback).</summary>
    public string FileName { get; set; } = "";

    /// <summary>True when <see cref="FileName"/> was synthesized because metadata was lost.</summary>
    public bool IsNameSynthesized { get; set; }

    public string Extension { get; set; } = "";

    public FileCategory Category { get; set; } = FileCategory.Other;

    public long Size { get; set; }

    public DateTime? Modified { get; set; }

    public DateTime? Created { get; set; }

    public DateTime? Deleted { get; set; }

    /// <summary>Original folder path relative to the volume root, when known.</summary>
    public string? OriginalPath { get; set; }

    public RecoveryStatus Status { get; set; } = RecoveryStatus.Recoverable;

    public RecoveryQuality Quality { get; set; } = RecoveryQuality.Unknown;

    // --- Recycle Bin source ---
    /// <summary>Full path to the surviving $R data file for Recycle Bin entries.</summary>
    public string? RawDataPath { get; set; }

    // --- MFT source ---
    /// <summary>Resident content stored directly in the MFT record (small files).</summary>
    public byte[]? ResidentData { get; set; }

    /// <summary>Non-resident cluster runs to read from the raw volume.</summary>
    public IReadOnlyList<DataRun>? DataRuns { get; set; }

    public int BytesPerCluster { get; set; }

    // --- Carved source ---
    /// <summary>Absolute byte offset of the carved file on the raw volume.</summary>
    public long CarveOffset { get; set; }

    /// <summary>A short signature/preview tag used for display.</summary>
    public string? SignatureTag { get; set; }

    // --- FAT32 source ---
    /// <summary>First cluster of a FAT32 deleted file.</summary>
    public uint FatStartCluster { get; set; }

    /// <summary>Cluster chain for reading FAT32 file content.</summary>
    public IReadOnlyList<uint>? FatClusterChain { get; set; }

    /// <summary>Byte offset of the FAT32 data region on the volume.</summary>
    public long FatDataStartOffset { get; set; }

    public string SizeDisplay => HumanSize.Format(Size);

    public override string ToString() => $"{FileName} ({SizeDisplay})";
}

/// <summary>Formats byte counts into human-readable strings.</summary>
public static class HumanSize
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB", "PB" };

    public static string Format(long bytes)
    {
        if (bytes < 0) return "-";
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} {Units[unit]}" : $"{value:0.##} {Units[unit]}";
    }
}
