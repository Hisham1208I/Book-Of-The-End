using BookOfTheEnd.Interop;
using BookOfTheEnd.Models;

namespace BookOfTheEnd.Services;

/// <summary>
/// Measures sequential read throughput by reading raw sectors from the volume for a
/// fixed duration. Never writes; safe to run on any drive at any time.
/// </summary>
public sealed class DriveSpeedService
{
    private const int BufferSize = 16 * 1024 * 1024; // 16 MB read buffer
    private readonly LoggingService _log;

    public DriveSpeedService(LoggingService log) => _log = log;

    /// <summary>
    /// Reads sequentially from <paramref name="drive"/> for <paramref name="duration"/> and
    /// returns the measured throughput in MB/s. Reports elapsed fraction [0..1] to
    /// <paramref name="progress"/> while running.
    /// </summary>
    public async Task<double> BenchmarkAsync(
        DriveModel drive,
        TimeSpan duration,
        IProgress<double> progress,
        CancellationToken token)
    {
        return await Task.Run(() =>
        {
            long bytesRead = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            double totalMs = duration.TotalMilliseconds;
            byte[] buffer = new byte[BufferSize];

            try
            {
                using var reader = RawVolumeReader.Open(drive.Letter, drive.TotalSize);
                long offset = 0;
                while (sw.Elapsed < duration)
                {
                    token.ThrowIfCancellationRequested();
                    int n = reader.ReadInto(offset, buffer, buffer.Length);
                    if (n == 0) break; // reached end of volume
                    bytesRead += n;
                    offset += n;
                    double fraction = Math.Min(sw.Elapsed.TotalMilliseconds / totalMs, 1.0);
                    progress.Report(fraction);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _log.Warn($"Speed benchmark failed on {drive.Letter}:: {ex.Message}");
                throw;
            }
            finally
            {
                sw.Stop();
            }

            double elapsedSec = sw.Elapsed.TotalSeconds;
            if (elapsedSec < 0.01) return 0;
            return bytesRead / elapsedSec / (1024.0 * 1024.0);
        }, token);
    }
}
