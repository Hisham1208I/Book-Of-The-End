namespace BookOfTheEnd.Services.Fat;

/// <summary>A deleted file discovered in a FAT32 directory structure.</summary>
public sealed class FatDeletedFile
{
    public required string FileName { get; init; }
    public string? DirectoryPath { get; init; }
    public long Size { get; init; }
    public uint StartCluster { get; init; }
    public required IReadOnlyList<uint> ClusterChain { get; init; }
    public DateTime? Modified { get; init; }
    public bool HasLongFileName { get; init; }
}
