using System.Diagnostics;
using System.IO;
using BookOfTheEnd.Models;
using BookOfTheEnd.Services.Fat;
using BookOfTheEnd.Services.Ntfs;
using BookOfTheEnd.Services.RecycleBin;

namespace BookOfTheEnd.Services.Scanning;

/// <summary>
/// Fast scan: Recycle Bin, deleted NTFS MFT records, and deleted FAT32 directory entries.
/// </summary>
public sealed class QuickScanEngine : IScanEngine
{
    private readonly LoggingService _log;
    private readonly RecycleBinScanner _recycleBin;

    public QuickScanEngine(LoggingService log)
    {
        _log = log;
        _recycleBin = new RecycleBinScanner(log);
    }

    public ScanType ScanType => ScanType.Quick;

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
            var token = controller.Token;

            progress.Report(new ScanProgress { Percent = 0, CurrentActivity = "Scanning Recycle Bin...", Elapsed = sw.Elapsed });
            try
            {
                foreach (var file in _recycleBin.Scan(drive.Letter, token))
                {
                    controller.WaitIfPaused();
                    if (!options.Includes(file.Category)) continue;
                    found++;
                    onFileFound(file);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _log.Warn($"Recycle bin scan failed on {drive.Letter}: {ex.Message}"); }

            if (drive.SupportsMftScan)
            {
                found = ScanNtfs(drive, options, controller, progress, onFileFound, sw, found, token);
            }
            else if (drive.SupportsFatScan)
            {
                found = ScanFat32(drive, options, controller, progress, onFileFound, sw, found, token);
            }
            else
            {
                progress.Report(new ScanProgress
                {
                    Percent = 100, FilesFound = found, Elapsed = sw.Elapsed,
                    CurrentActivity = $"Done. {drive.FileSystem} volumes support Recycle Bin scan only."
                });
            }
        }, controller.Token);
    }

    private int ScanNtfs(
        DriveModel drive,
        ScanOptions options,
        ScanController controller,
        IProgress<ScanProgress> progress,
        Action<RecoverableFile> onFileFound,
        Stopwatch sw,
        int found,
        CancellationToken token)
    {
        NtfsVolume? volume = null;
        try
        {
            volume = NtfsVolume.Open(drive.Letter, drive.TotalSize);
            long totalBytes = Math.Max(1, volume.MftSizeBytes);
            int bytesPerCluster = volume.Boot.BytesPerCluster;
            long lastReport = 0;

            foreach (var record in volume.EnumerateRecords(read =>
            {
                if (read - lastReport < 2 * 1024 * 1024) return;
                lastReport = read;
                double pct = Math.Min(99, read * 100.0 / totalBytes);
                progress.Report(BuildProgress(pct, read, totalBytes, found, sw, "Scanning Master File Table..."));
            }))
            {
                controller.WaitIfPaused();
                var file = ToRecoverableFileFromMft(record, drive.Letter, bytesPerCluster);
                if (file is null || !options.Includes(file.Category)) continue;
                found++;
                onFileFound(file);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException ex)
        {
            _log.Warn(ex.Message);
            progress.Report(new ScanProgress
            {
                Percent = 100, FilesFound = found, Elapsed = sw.Elapsed,
                CurrentActivity = "Administrator rights required for raw volume access."
            });
            return found;
        }
        catch (Exception ex)
        {
            _log.Error($"MFT scan failed on {drive.Letter}", ex);
        }
        finally
        {
            volume?.Dispose();
        }

        progress.Report(new ScanProgress
        {
            Percent = 100, FilesFound = found, Elapsed = sw.Elapsed,
            BytesProcessed = volume?.MftSizeBytes ?? 0, TotalBytes = volume?.MftSizeBytes ?? 0,
            CurrentActivity = $"Quick scan complete. {found} recoverable item(s) found."
        });
        return found;
    }

    private int ScanFat32(
        DriveModel drive,
        ScanOptions options,
        ScanController controller,
        IProgress<ScanProgress> progress,
        Action<RecoverableFile> onFileFound,
        Stopwatch sw,
        int found,
        CancellationToken token)
    {
        FatVolume? volume = null;
        try
        {
            volume = FatVolume.Open(drive.Letter, drive.TotalSize);
            long totalBytes = Math.Max(1, volume.ScanBudgetBytes);
            long lastReport = 0;

            foreach (var deleted in volume.ScanDeletedFiles(token, scanned =>
            {
                if (scanned - lastReport < 512 * 1024) return;
                lastReport = scanned;
                double pct = Math.Min(99, scanned * 100.0 / totalBytes);
                progress.Report(BuildProgress(pct, scanned, totalBytes, found, sw, "Scanning FAT32 directories..."));
            }))
            {
                controller.WaitIfPaused();
                var file = ToRecoverableFileFromFat(deleted, drive.Letter, volume.Boot.BytesPerCluster, volume.Boot.DataStartOffset);
                if (!options.Includes(file.Category)) continue;
                found++;
                onFileFound(file);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException ex)
        {
            _log.Warn(ex.Message);
            progress.Report(new ScanProgress
            {
                Percent = 100, FilesFound = found, Elapsed = sw.Elapsed,
                CurrentActivity = "Administrator rights required for raw volume access."
            });
            return found;
        }
        catch (Exception ex)
        {
            _log.Error($"FAT32 scan failed on {drive.Letter}", ex);
        }
        finally
        {
            volume?.Dispose();
        }

        progress.Report(new ScanProgress
        {
            Percent = 100, FilesFound = found, Elapsed = sw.Elapsed,
            CurrentActivity = $"Quick scan complete. {found} recoverable item(s) found."
        });
        return found;
    }

    private static RecoverableFile? ToRecoverableFileFromMft(MftRecord record, string driveLetter, int bytesPerCluster)
    {
        if (!record.IsValid || record.InUse || record.IsDirectory) return null;
        if (string.IsNullOrWhiteSpace(record.FileName)) return null;

        bool hasResident = record.ResidentData is { Length: > 0 };
        bool hasRuns = record.DataRuns is { Count: > 0 };
        if (!hasResident && !hasRuns) return null;
        if (record.RealSize <= 0 && !hasResident) return null;

        string name = record.FileName!;
        string ext = Path.GetExtension(name);

        return new RecoverableFile
        {
            Source = RecoverySource.MasterFileTable,
            DriveLetter = driveLetter,
            FileName = name,
            Extension = ext,
            Category = FileTypeMap.FromFileName(name),
            Size = record.RealSize > 0 ? record.RealSize : record.ResidentData!.Length,
            Modified = record.Modified,
            Created = record.Created,
            ResidentData = record.ResidentData,
            DataRuns = record.DataRuns,
            BytesPerCluster = bytesPerCluster,
            Status = RecoveryStatus.Recoverable,
            Quality = hasResident ? RecoveryQuality.Excellent : RecoveryQuality.Good
        };
    }

    private static RecoverableFile ToRecoverableFileFromFat(FatDeletedFile deleted, string driveLetter, int bytesPerCluster, long dataStartOffset)
    {
        string ext = Path.GetExtension(deleted.FileName);
        return new RecoverableFile
        {
            Source = RecoverySource.Fat32Directory,
            DriveLetter = driveLetter,
            FileName = deleted.FileName,
            Extension = ext,
            Category = FileTypeMap.FromFileName(deleted.FileName),
            Size = deleted.Size,
            Modified = deleted.Modified,
            OriginalPath = deleted.DirectoryPath,
            FatStartCluster = deleted.StartCluster,
            FatClusterChain = deleted.ClusterChain,
            FatDataStartOffset = dataStartOffset,
            BytesPerCluster = bytesPerCluster,
            Status = RecoveryStatus.Recoverable,
            Quality = deleted.HasLongFileName ? RecoveryQuality.Good : RecoveryQuality.Fair
        };
    }

    private static ScanProgress BuildProgress(double percent, long processed, long total, int found, Stopwatch sw, string activity)
    {
        TimeSpan? eta = null;
        if (percent > 1 && percent < 100)
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
