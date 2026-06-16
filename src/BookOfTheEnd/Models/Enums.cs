namespace BookOfTheEnd.Models;

public enum ScanType
{
    Quick,
    Deep
}

public enum DriveConnection
{
    Unknown,
    Internal,
    External
}

public enum DriveMediaType
{
    Unknown,
    HardDisk,
    SolidState,
    Removable
}

public enum DriveStatus
{
    Online,
    Formatted,
    Offline
}

public enum AppTab
{
    Scan,
    Results
}

public enum FileCategory
{
    Other,
    Image,
    Video,
    Audio,
    Document,
    Archive
}

/// <summary>
/// Where a recoverable entry was discovered, which dictates how its bytes are read back.
/// </summary>
public enum RecoverySource
{
    /// <summary>Found in the Recycle Bin; the underlying $R file still exists on disk.</summary>
    RecycleBin,

    /// <summary>Deleted entry recovered from the NTFS Master File Table.</summary>
    MasterFileTable,

    /// <summary>Carved from raw sectors using a file signature (Deep Scan).</summary>
    Carved,

    /// <summary>Deleted entry recovered from a FAT32 directory and allocation table.</summary>
    Fat32Directory
}

public enum RecoveryStatus
{
    /// <summary>Metadata and data location are intact; high chance of a clean recovery.</summary>
    Recoverable,

    /// <summary>Data can likely be recovered but the original name/metadata was lost.</summary>
    PartialMetadata,

    /// <summary>Data location is known but may have been partially overwritten.</summary>
    Overwritten,

    /// <summary>The file has already been recovered to disk in this session.</summary>
    Recovered,

    /// <summary>A recovery attempt failed.</summary>
    Failed
}

public enum RecoveryQuality
{
    Unknown,
    Poor,
    Fair,
    Good,
    Excellent
}
