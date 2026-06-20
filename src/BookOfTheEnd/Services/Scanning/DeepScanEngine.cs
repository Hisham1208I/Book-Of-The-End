using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using BookOfTheEnd.Interop;
using BookOfTheEnd.Models;
using BookOfTheEnd.Services.Carving;

namespace BookOfTheEnd.Services.Scanning;

/// <summary>
/// Sector-level scan: streams the raw volume and carves files by matching known
/// header signatures, bounding each by a footer, embedded size field, or a cap.
/// Recovers files no longer referenced by the file system (e.g. after formatting).
///
/// Uses a producer/consumer pipeline (System.Threading.Channels) so the OS can
/// prefetch the next 16 MB chunk while the CPU processes the current one, hiding
/// sequential I/O latency behind CPU work. Best gains on SSDs.
/// </summary>
public sealed class DeepScanEngine : IScanEngine
{
    private const int ChunkSize = 16 * 1024 * 1024;
    private const int FooterWindow = 1 * 1024 * 1024;
    private const int PipelineDepth = 2; // chunks buffered ahead of the consumer

    private readonly LoggingService _log;
    private readonly Dictionary<byte, List<FileSignature>> _byFirstByte;

    public DeepScanEngine(LoggingService log)
    {
        _log = log;
        _byFirstByte = new Dictionary<byte, List<FileSignature>>();
        foreach (var sig in FileSignatures.All)
        {
            if (!_byFirstByte.TryGetValue(sig.Header[0], out var list))
                _byFirstByte[sig.Header[0]] = list = new List<FileSignature>();
            list.Add(sig);
        }
    }

    public ScanType ScanType => ScanType.Deep;

    public Task ScanAsync(
        DriveModel drive,
        ScanOptions options,
        ScanController controller,
        IProgress<ScanProgress> progress,
        Action<RecoverableFile> onFileFound)
    {
        return Task.Run(async () =>
        {
            var sw = Stopwatch.StartNew();
            int found = 0;
            long volumeLength = drive.TotalSize > 0 ? drive.TotalSize : 0;
            int overlap = Math.Max(512, FileSignatures.MaxHeaderSpan);

            // Two independent reader handles so I/O can overlap between producer and consumer.
            RawVolumeReader? producerReader = null;
            RawVolumeReader? consumerReader = null;

            try
            {
                producerReader = RawVolumeReader.Open(drive.Letter, volumeLength);
                consumerReader = RawVolumeReader.Open(drive.Letter, volumeLength);

                var channel = Channel.CreateBounded<(long AbsOffset, byte[] Data)>(
                    new BoundedChannelOptions(PipelineDepth)
                    {
                        SingleWriter = true,
                        SingleReader = true,
                        FullMode = BoundedChannelFullMode.Wait
                    });

                // Producer: read sequential chunks and push them into the channel.
                var producerTask = Task.Run(async () =>
                {
                    long pos = 0;
                    try
                    {
                        while (pos < volumeLength)
                        {
                            controller.Token.ThrowIfCancellationRequested();
                            int want = (int)Math.Min(ChunkSize, volumeLength - pos);
                            byte[] data = producerReader.Read(pos, want);
                            if (data.Length == 0) break;
                            await channel.Writer.WriteAsync((pos, data), controller.Token);
                            if (want < ChunkSize) break;
                            pos += want - overlap;
                        }
                    }
                    finally
                    {
                        channel.Writer.Complete();
                    }
                }, controller.Token);

                // Consumer: match signatures in each chunk; DetermineSize uses consumerReader.
                long skipUntil = 0;

                await foreach (var (absOffset, data) in channel.Reader.ReadAllAsync(controller.Token))
                {
                    controller.WaitIfPaused();

                    for (int i = 0; i < data.Length; i++)
                    {
                        if (!_byFirstByte.TryGetValue(data[i], out var candidates)) continue;
                        foreach (var sig in candidates)
                        {
                            if (!Matches(data, i, sig.Header)) continue;
                            long fileStart = absOffset + i - sig.HeaderOffset;
                            if (fileStart < skipUntil || fileStart < 0) continue;
                            if (!options.Includes(sig.Category)) continue;

                            long size = DetermineSize(consumerReader, sig, fileStart, volumeLength);
                            if (size <= 0) continue;

                            var file = new RecoverableFile
                            {
                                Source = RecoverySource.Carved,
                                DriveLetter = drive.Letter,
                                FileName = $"recovered_{fileStart:X}{sig.Extension}",
                                IsNameSynthesized = true,
                                Extension = sig.Extension,
                                Category = sig.Category,
                                Size = size,
                                CarveOffset = fileStart,
                                SignatureTag = sig.Extension.TrimStart('.').ToUpperInvariant(),
                                Status = RecoveryStatus.PartialMetadata,
                                Quality = sig.Quality
                            };
                            found++;
                            onFileFound(file);
                            skipUntil = fileStart + size;
                            break;
                        }
                    }

                    double pct = volumeLength > 0 ? Math.Min(99, absOffset * 100.0 / volumeLength) : 0;
                    progress.Report(BuildProgress(pct, absOffset, volumeLength, found, sw,
                        "Carving raw sectors..."));
                }

                await producerTask; // re-throw any producer exception
            }
            catch (OperationCanceledException) { throw; }
            catch (UnauthorizedAccessException ex)
            {
                _log.Warn(ex.Message);
                progress.Report(new ScanProgress
                {
                    Percent = 100, FilesFound = found, Elapsed = sw.Elapsed,
                    CurrentActivity = "Administrator rights required for raw sector access."
                });
                return;
            }
            catch (Exception ex)
            {
                _log.Error($"Deep scan failed on {drive.Letter}", ex);
            }
            finally
            {
                producerReader?.Dispose();
                consumerReader?.Dispose();
            }

            progress.Report(new ScanProgress
            {
                Percent = 100, FilesFound = found, Elapsed = sw.Elapsed,
                BytesProcessed = volumeLength, TotalBytes = volumeLength,
                CurrentActivity = $"Deep scan complete. {found} file(s) carved."
            });
        }, controller.Token);
    }

    private static bool Matches(byte[] data, int pos, byte[] pattern)
    {
        if (pos + pattern.Length > data.Length) return false;
        for (int i = 0; i < pattern.Length; i++)
            if (data[pos + i] != pattern[i]) return false;
        return true;
    }

    private long DetermineSize(RawVolumeReader reader, FileSignature sig, long fileStart, long volumeLength)
    {
        long maxBound = Math.Min(sig.MaxSize, volumeLength - fileStart);
        if (maxBound <= 0) return 0;

        switch (sig.Strategy)
        {
            case SizeStrategy.Footer:
                long end = FindFooter(reader, sig, fileStart, maxBound, volumeLength);
                return end > 0 ? Math.Min(end - fileStart, maxBound) : 0;

            case SizeStrategy.RiffSize:
            {
                byte[] head = reader.Read(fileStart, 12);
                if (head.Length < 12) return 0;
                string form = Encoding.ASCII.GetString(head, 8, 4);
                string expected = sig.Extension switch
                {
                    ".avi"  => "AVI ",
                    ".webp" => "WEBP",
                    _       => "WAVE"
                };
                if (!string.Equals(form, expected, StringComparison.Ordinal)) return 0;
                long riffSize = BitConverter.ToUInt32(head, 4) + 8L;
                return riffSize is > 64 && riffSize <= maxBound ? riffSize : 0;
            }

            case SizeStrategy.BmpSize:
            {
                byte[] head = reader.Read(fileStart, 6);
                if (head.Length < 6) return 0;
                long bmpSize = BitConverter.ToUInt32(head, 2);
                return bmpSize is > 54 && bmpSize <= maxBound ? bmpSize : 0;
            }

            case SizeStrategy.BoxSize:
                return WalkIsoBoxes(reader, fileStart, maxBound);

            default:
                return Math.Min(sig.FixedSize, maxBound);
        }
    }

    /// <summary>
    /// Walks top-level ISO Base Media File Format boxes starting at <paramref name="fileStart"/>,
    /// summing their sizes to find where the file ends.
    /// Each top-level box costs one 16-byte disk read regardless of its content size,
    /// so a typical MP4 (ftyp + free + mdat + moov) is only 4 reads total.
    /// </summary>
    private static long WalkIsoBoxes(RawVolumeReader reader, long fileStart, long maxBound)
    {
        long pos = fileStart;
        long ceiling = fileStart + maxBound;

        while (pos < ceiling)
        {
            // Read enough for a full extended header (8 bytes basic, 16 bytes if largesize)
            int wantH = (int)Math.Min(16L, ceiling - pos);
            if (wantH < 8) break;
            byte[] h = reader.Read(pos, wantH);
            if (h.Length < 8) break;

            // Box size field is big-endian
            long boxSize = ((long)h[0] << 24) | ((long)h[1] << 16) | ((long)h[2] << 8) | h[3];

            if (boxSize == 0)
            {
                // Extends to end of file — use current position as approximate end
                break;
            }

            if (boxSize == 1)
            {
                // 64-bit largesize immediately follows the 4-byte type field
                if (h.Length < 16) break;
                boxSize = ((long)h[8]  << 56) | ((long)h[9]  << 48)
                        | ((long)h[10] << 40) | ((long)h[11] << 32)
                        | ((long)h[12] << 24) | ((long)h[13] << 16)
                        | ((long)h[14] <<  8) |  (long)h[15];
            }

            // A malformed or out-of-bounds box means we've found the file boundary
            if (boxSize < 8 || pos + boxSize > ceiling) break;

            pos += boxSize;
        }

        long total = pos - fileStart;
        // Require at least an ftyp box (24 bytes minimum) to consider it valid
        return total >= 24 ? total : 0;
    }

    private static long FindFooter(RawVolumeReader reader, FileSignature sig, long fileStart, long maxBound, long volumeLength)
    {
        byte[] footer = sig.Footer!;
        long pos = fileStart + sig.Header.Length;
        long searchEnd = Math.Min(volumeLength, fileStart + maxBound);
        int overlap = footer.Length - 1;

        while (pos < searchEnd)
        {
            int want = (int)Math.Min(FooterWindow, searchEnd - pos);
            byte[] window = reader.Read(pos, want);
            if (window.Length == 0) break;

            for (int i = 0; i + footer.Length <= window.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < footer.Length; j++)
                    if (window[i + j] != footer[j]) { match = false; break; }
                if (match)
                    return pos + i + footer.Length;
            }

            if (want < FooterWindow) break;
            pos += want - overlap;
        }
        return -1;
    }

    private static ScanProgress BuildProgress(double percent, long processed, long total, int found, Stopwatch sw, string activity)
    {
        TimeSpan? eta = null;
        if (percent > 0.5 && percent < 100)
        {
            double totalSeconds = sw.Elapsed.TotalSeconds / (percent / 100.0);
            eta = TimeSpan.FromSeconds(Math.Max(0, totalSeconds - sw.Elapsed.TotalSeconds));
        }
        return new ScanProgress
        {
            Percent = percent,
            BytesProcessed = processed,
            TotalBytes = total,
            FilesFound = found,
            Elapsed = sw.Elapsed,
            EstimatedRemaining = eta,
            CurrentActivity = activity
        };
    }
}
