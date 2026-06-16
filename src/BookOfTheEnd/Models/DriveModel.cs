namespace BookOfTheEnd.Models;

/// <summary>
/// Describes a mounted volume plus the physical-disk characteristics resolved through WMI.
/// </summary>
public sealed class DriveModel
{
    /// <summary>Drive letter without trailing separators, e.g. "C".</summary>
    public string Letter { get; init; } = "";

    /// <summary>Root path, e.g. "C:\".</summary>
    public string RootPath { get; init; } = "";

    /// <summary>Device path used for raw access, e.g. "\\.\C:".</summary>
    public string DevicePath => $"\\\\.\\{Letter}:";

    public string VolumeLabel { get; init; } = "";

    public string FileSystem { get; init; } = "";

    public long TotalSize { get; init; }

    public long FreeSpace { get; init; }

    public long UsedSpace => Math.Max(0, TotalSize - FreeSpace);

    public double UsedPercent => TotalSize > 0 ? UsedSpace * 100.0 / TotalSize : 0;

    public string ConnectionDisplay
    {
        get
        {
            string conn = Connection switch
            {
                DriveConnection.Internal => "Internal",
                DriveConnection.External => "External",
                _ => "Drive"
            };
            string media = MediaType switch
            {
                DriveMediaType.SolidState => "SSD",
                DriveMediaType.HardDisk => "HDD",
                DriveMediaType.Removable => "Removable",
                _ => ""
            };
            return string.IsNullOrEmpty(media) ? conn : $"{conn} · {media}";
        }
    }

    public string CapacityDisplay =>
        TotalSize > 0 ? $"{HumanSize.Format(UsedSpace)} used of {HumanSize.Format(TotalSize)}" : "Not ready";

    /// <summary>Sidebar grouping bucket.</summary>
    public string GroupName => Connection == DriveConnection.External ? "External" : "Internal";

    public DriveStatus Status
    {
        get
        {
            if (!IsReady) return DriveStatus.Offline;
            if (string.IsNullOrWhiteSpace(FileSystem) ||
                FileSystem.Equals("RAW", StringComparison.OrdinalIgnoreCase))
                return DriveStatus.Formatted;
            return DriveStatus.Online;
        }
    }

    public string StatusLabel => Status switch
    {
        DriveStatus.Online => "Online",
        DriveStatus.Formatted => "Formatted",
        _ => "Offline"
    };

    /// <summary>Compact specs line, e.g. "NTFS · NVMe".</summary>
    public string SpecsLine
    {
        get
        {
            string fs = string.IsNullOrWhiteSpace(FileSystem) ? "RAW" : FileSystem;
            string bus = !string.IsNullOrWhiteSpace(BusType) ? BusType
                : MediaType switch
                {
                    DriveMediaType.SolidState => "SSD",
                    DriveMediaType.HardDisk => "HDD",
                    DriveMediaType.Removable => "Removable",
                    _ => ""
                };
            return string.IsNullOrEmpty(bus) ? fs : $"{fs} · {bus}";
        }
    }

    public string TotalSizeShort => TotalSize > 0 ? HumanSize.Format(TotalSize) : "—";

    /// <summary>Segoe MDL2 glyph representing the device kind.</summary>
    public string DeviceGlyph
    {
        get
        {
            if (BusType is "SD" or "MMC" || (Connection == DriveConnection.External && MediaType == DriveMediaType.Removable && BusType != "USB"))
                return "\uE7F1"; // SD/memory card
            if (BusType == "USB" || (Connection == DriveConnection.External && MediaType == DriveMediaType.Removable))
                return "\uE88E"; // USB
            if (Connection == DriveConnection.External)
                return "\uEDA2"; // external drive
            return "\uEDA2"; // internal disk
        }
    }

    public DriveConnection Connection { get; init; } = DriveConnection.Unknown;

    public DriveMediaType MediaType { get; init; } = DriveMediaType.Unknown;

    /// <summary>Friendly model string of the backing physical disk, when resolvable.</summary>
    public string Model { get; init; } = "";

    /// <summary>Bus type reported by Windows (USB, SATA, NVMe, etc.).</summary>
    public string BusType { get; init; } = "";

    public bool IsReady { get; init; }

    /// <summary>NTFS is required for Quick Scan MFT parsing.</summary>
    public bool SupportsMftScan =>
        string.Equals(FileSystem, "NTFS", StringComparison.OrdinalIgnoreCase);

    /// <summary>FAT32 is required for Quick Scan deleted directory parsing.</summary>
    public bool SupportsFatScan =>
        FileSystem.StartsWith("FAT32", StringComparison.OrdinalIgnoreCase);

    public override string ToString() =>
        string.IsNullOrWhiteSpace(VolumeLabel) ? $"{Letter}:\\" : $"{Letter}:\\ ({VolumeLabel})";
}
