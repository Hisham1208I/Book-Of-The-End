namespace BookOfTheEnd.Models;

public sealed class SessionData
{
    public string SchemaVersion { get; set; } = "1";
    public DateTime SavedAt { get; set; }
    public string DriveLetter { get; set; } = "";
    public string ScanType { get; set; } = "";
    public int TotalFound { get; set; }
    public List<SessionFile> Files { get; set; } = new();
}

public sealed class SessionFile
{
    public string Source { get; set; } = "";
    public string DriveLetter { get; set; } = "";
    public string FileName { get; set; } = "";
    public bool IsNameSynthesized { get; set; }
    public string Extension { get; set; } = "";
    public string Category { get; set; } = "";
    public long Size { get; set; }
    public DateTime? Modified { get; set; }
    public DateTime? Created { get; set; }
    public DateTime? Deleted { get; set; }
    public string? OriginalPath { get; set; }
    public string Status { get; set; } = "";
    public string Quality { get; set; } = "";
    public string? RawDataPath { get; set; }
    public string? ResidentDataBase64 { get; set; }
    public List<SessionDataRun>? DataRuns { get; set; }
    public int BytesPerCluster { get; set; }
    public long CarveOffset { get; set; }
    public string? SignatureTag { get; set; }
    public uint FatStartCluster { get; set; }
    public List<uint>? FatClusterChain { get; set; }
    public long FatDataStartOffset { get; set; }
}

public sealed class SessionDataRun
{
    public long StartCluster { get; set; }
    public long ClusterCount { get; set; }
}
