using System.Diagnostics;
using BookOfTheEnd.Interop;
using BookOfTheEnd.Models;
using BookOfTheEnd.Models.Health;
using BookOfTheEnd.Services.Scanning;

namespace BookOfTheEnd.Services.Health;

/// <summary>
/// Read-only surface scan. Samples blocks evenly across the volume, times each read,
/// and classifies blocks as Healthy / Slow / Bad. It never writes, so it is safe to run
/// against a drive that is about to be recovered. Cancellable via <see cref="ScanController"/>.
/// </summary>
public sealed class SurfaceScanService
{
    private const int DefaultSamples = 256;
    private const int BlockBytes = 1 * 1024 * 1024;
    private const double SlowFloorMs = 30.0;
    private const double SlowFactor = 4.0;

    private readonly LoggingService _log;

    public SurfaceScanService(LoggingService log) => _log = log;

    public Task<SurfaceScanResult> ScanAsync(
        DriveModel drive,
        ScanController controller,
        IProgress<ScanProgress> progress,
        int samples = DefaultSamples)
    {
        return Task.Run(() =>
        {
            long volumeLength = drive.TotalSize;
            if (volumeLength <= 0)
                return EmptyResult("Drive size unknown; cannot run a surface scan.");

            samples = Math.Clamp(samples, 16, 4096);
            long stride = Math.Max(BlockBytes, volumeLength / samples);
            var blocks = new List<SurfaceBlock>(samples);
            var sw = Stopwatch.StartNew();

            RawVolumeReader? reader = null;
            try
            {
                reader = RawVolumeReader.Open(drive.Letter, volumeLength);

                long offset = 0;
                int index = 0;
                while (offset < volumeLength)
                {
                    controller.WaitIfPaused();

                    int want = (int)Math.Min(BlockBytes, volumeLength - offset);
                    var block = new SurfaceBlock { OffsetBytes = offset, LengthBytes = want };

                    var blockSw = Stopwatch.StartNew();
                    try
                    {
                        byte[] data = reader.Read(offset, want);
                        blockSw.Stop();
                        if (data.Length == 0)
                        {
                            block.Status = SectorStatus.Bad;
                        }
                        else
                        {
                            block.ReadMs = blockSw.Elapsed.TotalMilliseconds;
                            block.Status = SectorStatus.Healthy; // refined below
                        }
                    }
                    catch (Exception)
                    {
                        blockSw.Stop();
                        block.Status = SectorStatus.Bad;
                    }

                    blocks.Add(block);
                    index++;

                    double pct = Math.Min(99, offset * 100.0 / volumeLength);
                    progress.Report(new ScanProgress
                    {
                        Percent = pct,
                        BytesProcessed = offset,
                        TotalBytes = volumeLength,
                        FilesFound = blocks.Count(b => b.Status == SectorStatus.Bad),
                        Elapsed = sw.Elapsed,
                        CurrentActivity = $"Surface scan: block {index}/{samples}..."
                    });

                    offset += stride;
                }
            }
            catch (OperationCanceledException)
            {
                // Return what we have so far.
            }
            catch (UnauthorizedAccessException)
            {
                return EmptyResult("Administrator rights are required for surface scanning.");
            }
            catch (Exception ex)
            {
                _log.Warn($"Surface scan failed on {drive.Letter}: {ex.Message}");
                return EmptyResult($"Surface scan failed: {ex.Message}");
            }
            finally
            {
                reader?.Dispose();
            }

            ClassifySlowBlocks(blocks);

            int healthy = blocks.Count(b => b.Status == SectorStatus.Healthy);
            int slow = blocks.Count(b => b.Status == SectorStatus.Slow);
            int bad = blocks.Count(b => b.Status == SectorStatus.Bad);

            progress.Report(new ScanProgress
            {
                Percent = 100,
                FilesFound = bad,
                Elapsed = sw.Elapsed,
                CurrentActivity = $"Surface scan complete. {bad} bad, {slow} slow, {healthy} healthy block(s)."
            });

            return new SurfaceScanResult
            {
                TotalBytesScanned = blocks.Sum(b => b.LengthBytes),
                HealthyBlocks = healthy,
                SlowBlocks = slow,
                BadBlocks = bad,
                Completed = true,
                Blocks = blocks
            };
        }, controller.Token);
    }

    /// <summary>Marks healthy blocks whose read time is a clear outlier as Slow.</summary>
    private static void ClassifySlowBlocks(List<SurfaceBlock> blocks)
    {
        var times = blocks
            .Where(b => b.Status == SectorStatus.Healthy && b.ReadMs > 0)
            .Select(b => b.ReadMs)
            .OrderBy(t => t)
            .ToList();
        if (times.Count < 4) return;

        double median = times[times.Count / 2];
        double threshold = Math.Max(SlowFloorMs, median * SlowFactor);

        foreach (var b in blocks)
            if (b.Status == SectorStatus.Healthy && b.ReadMs > threshold)
                b.Status = SectorStatus.Slow;
    }

    private static SurfaceScanResult EmptyResult(string note) => new()
    {
        Completed = false,
        Note = note,
        Blocks = Array.Empty<SurfaceBlock>()
    };
}
