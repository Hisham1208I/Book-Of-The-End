namespace BookOfTheEnd.Models;

/// <summary>
/// A single NTFS non-resident data run: a contiguous span of clusters on the volume.
/// </summary>
public readonly struct DataRun
{
    /// <summary>Starting Logical Cluster Number on the volume (-1 for a sparse/unallocated run).</summary>
    public long StartCluster { get; }

    /// <summary>Number of clusters covered by the run.</summary>
    public long ClusterCount { get; }

    public DataRun(long startCluster, long clusterCount)
    {
        StartCluster = startCluster;
        ClusterCount = clusterCount;
    }

    public bool IsSparse => StartCluster < 0;
}
