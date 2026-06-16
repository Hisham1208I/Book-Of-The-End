using System.Diagnostics;
using System.Text;
using BookOfTheEnd.Interop;
using BookOfTheEnd.Models;
using BookOfTheEnd.Services.Carving;

namespace BookOfTheEnd.Services.Scanning;

/// <summary>
/// Sector-level scan: streams the raw volume and carves files by matching known
/// header signatures, bounding each by a footer, embedded size field, or a cap.
/// Recovers files no longer referenced by the file system (e.g. after formatting).
/// </summary>
public sealed class DeepScanEngine : IScanEngine
{
    private const int ChunkSize = 16 * 1024 * 1024;
    private const int FooterWindow = 1 * 1024 * 1024;

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
        return Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            int found = 0;
            long volumeLength = drive.TotalSize > 0 ? drive.TotalSize : 0;

            RawVolumeReader? reader = null;
            try
            {
                reader = RawVolumeReader.Open(drive.Letter, volumeLength);
                int overlap = Math.Max(reader.SectorSize, FileSignatures.MaxHeaderSpan);
                long skipUntil = 0;
                long absOffset = 0;

                while (absOffset < volumeLength)
                {
                    controller.WaitIfPaused();

                    int want = (int)Math.Min(ChunkSize, volumeLength - absOffset);
                    byte[] data = reader.Read(absOffset, want);
                    if (data.Length == 0) break;

                    for (int i = 0; i < data.Length; i++)
                    {
                        if (!_byFirstByte.TryGetValue(data[i], out var candidates)) continue;
                        foreach (var sig in candidates)
                        {
                            if (!Matches(data, i, sig.Header)) continue;
                            long fileStart = absOffset + i - sig.HeaderOffset;
                            if (fileStart < skipUntil || fileStart < 0) continue;
                            if (!options.Includes(sig.Category)) continue;

                            long size = DetermineSize(reader, sig, fileStart, volumeLength);
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

                    if (want < ChunkSize) break;
                    absOffset += want - overlap;
                }
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
                reader?.Dispose();
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
                string expected = sig.Extension == ".avi" ? "AVI " : "WAVE";
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

            default:
                return Math.Min(sig.FixedSize, maxBound);
        }
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
