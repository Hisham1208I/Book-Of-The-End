using BookOfTheEnd.Interop;
using BookOfTheEnd.Models;

namespace BookOfTheEnd.Services;

/// <summary>
/// Clones a raw volume byte-for-byte to an .img file.
/// Reads sequentially in 16 MB chunks; never writes to the source drive.
/// </summary>
public sealed class DriveImageService
{
    private const int BufferSize = 16 * 1024 * 1024; // 16 MB

    private readonly LoggingService _log;

    public DriveImageService(LoggingService log) => _log = log;

    /// <param name="drive">Source volume to image.</param>
    /// <param name="outputPath">Destination .img file path (must not already exist).</param>
    /// <param name="progress">Reports (bytesWritten, totalBytes, activityText).</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>Total bytes written.</returns>
    public async Task<long> ImageAsync(
        DriveModel drive,
        string outputPath,
        IProgress<(long written, long total, string activity)> progress,
        CancellationToken token)
    {
        long volumeSize = drive.TotalSize;
        if (volumeSize <= 0)
            throw new InvalidOperationException($"Cannot determine size of drive {drive.Letter}:.");

        _log.Info($"Drive image started: {drive.Letter}: → {outputPath}  ({HumanSize.Format(volumeSize)})");

        return await Task.Run(() =>
        {
            using var reader = RawVolumeReader.Open(drive.Letter, volumeSize);
            using var output = new System.IO.FileStream(
                outputPath,
                System.IO.FileMode.CreateNew,
                System.IO.FileAccess.Write,
                System.IO.FileShare.None,
                65536,
                System.IO.FileOptions.SequentialScan);

            long written = 0;
            while (written < volumeSize)
            {
                token.ThrowIfCancellationRequested();
                int want = (int)Math.Min(BufferSize, volumeSize - written);
                byte[] chunk = reader.Read(written, want);
                if (chunk.Length == 0) break;
                output.Write(chunk, 0, chunk.Length);
                written += chunk.Length;
                double pct = written * 100.0 / volumeSize;
                progress.Report((written, volumeSize,
                    $"Imaging {drive.Letter}: — {pct:0.0}%  " +
                    $"({HumanSize.Format(written)} / {HumanSize.Format(volumeSize)})"));
            }

            _log.Info($"Drive image complete: {written:N0} bytes written to {outputPath}");
            return written;
        }, token);
    }
}
