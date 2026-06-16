namespace BookOfTheEnd.Models;

/// <summary>
/// User-configurable parameters for a scan run.
/// </summary>
public sealed class ScanOptions
{
    public ScanType ScanType { get; init; } = ScanType.Quick;

    /// <summary>
    /// Categories to include. An empty set means "all categories".
    /// </summary>
    public HashSet<FileCategory> Categories { get; init; } = new();

    public bool Includes(FileCategory category) =>
        Categories.Count == 0 || Categories.Contains(category);
}
