using BookOfTheEnd.Models;

namespace BookOfTheEnd.Services.Scanning;

/// <summary>A scan strategy (Quick or Deep) over a single drive.</summary>
public interface IScanEngine
{
    ScanType ScanType { get; }

    /// <summary>
    /// Runs the scan, reporting incremental progress and surfacing each found file
    /// through <paramref name="onFileFound"/>. Honors pause/cancel via the controller.
    /// </summary>
    Task ScanAsync(
        DriveModel drive,
        ScanOptions options,
        ScanController controller,
        IProgress<ScanProgress> progress,
        Action<RecoverableFile> onFileFound);
}
