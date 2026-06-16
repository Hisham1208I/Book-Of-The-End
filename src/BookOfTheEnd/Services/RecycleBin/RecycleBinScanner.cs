using System.IO;
using System.Text;
using BookOfTheEnd.Models;

namespace BookOfTheEnd.Services.RecycleBin;

/// <summary>
/// Scans the per-volume $Recycle.Bin folder. Each deleted item is stored as a
/// pair: $I&lt;id&gt; (metadata) and $R&lt;id&gt; (the surviving data). Because $R still
/// exists, these items recover cleanly with full original metadata.
/// </summary>
public sealed class RecycleBinScanner
{
    private readonly LoggingService _log;

    public RecycleBinScanner(LoggingService log) => _log = log;

    public IEnumerable<RecoverableFile> Scan(string driveLetter, CancellationToken token)
    {
        string root = $"{driveLetter}:\\$Recycle.Bin";
        if (!Directory.Exists(root)) yield break;

        IEnumerable<string> sidDirs;
        try { sidDirs = Directory.EnumerateDirectories(root); }
        catch (Exception ex) { _log.Warn($"Recycle bin root unreadable on {driveLetter}: {ex.Message}"); yield break; }

        foreach (string sidDir in sidDirs)
        {
            token.ThrowIfCancellationRequested();
            IEnumerable<string> infoFiles;
            try { infoFiles = Directory.EnumerateFiles(sidDir, "$I*"); }
            catch { continue; }

            foreach (string infoFile in infoFiles)
            {
                token.ThrowIfCancellationRequested();
                RecoverableFile? file = null;
                try { file = ParseInfoFile(infoFile, driveLetter); }
                catch (Exception ex) { _log.Warn($"Failed to parse {infoFile}: {ex.Message}"); }
                if (file is not null) yield return file;
            }
        }
    }

    private static RecoverableFile? ParseInfoFile(string infoPath, string driveLetter)
    {
        byte[] data = File.ReadAllBytes(infoPath);
        if (data.Length < 24) return null;

        long version = BitConverter.ToInt64(data, 0);
        long originalSize = BitConverter.ToInt64(data, 8);
        long deletedFileTime = BitConverter.ToInt64(data, 16);

        string originalPath;
        if (version >= 2 && data.Length >= 28)
        {
            int nameChars = BitConverter.ToInt32(data, 24);
            int byteLen = Math.Max(0, (nameChars - 1) * 2);
            if (28 + byteLen > data.Length) byteLen = data.Length - 28;
            originalPath = Encoding.Unicode.GetString(data, 28, Math.Max(0, byteLen));
        }
        else
        {
            int byteLen = Math.Min(520, data.Length - 24);
            originalPath = Encoding.Unicode.GetString(data, 24, byteLen).TrimEnd('\0');
        }

        // The $R file shares the id; it holds the real bytes and still exists.
        string dir = Path.GetDirectoryName(infoPath)!;
        string infoName = Path.GetFileName(infoPath);
        string rName = "$R" + infoName.Substring(2);
        string rPath = Path.Combine(dir, rName);
        if (!File.Exists(rPath)) return null;

        string fileName = string.IsNullOrWhiteSpace(originalPath)
            ? Path.GetFileName(rPath)
            : Path.GetFileName(originalPath);
        if (string.IsNullOrWhiteSpace(fileName)) fileName = rName;

        string ext = Path.GetExtension(fileName);
        DateTime? deleted = SafeFileTime(deletedFileTime);
        DateTime? modified = null;
        try { modified = File.GetLastWriteTime(rPath); } catch { }

        return new RecoverableFile
        {
            Source = RecoverySource.RecycleBin,
            DriveLetter = driveLetter,
            FileName = fileName,
            Extension = ext,
            Category = FileTypeMap.FromFileName(fileName),
            Size = originalSize > 0 ? originalSize : SafeLength(rPath),
            Modified = modified,
            Deleted = deleted,
            OriginalPath = string.IsNullOrWhiteSpace(originalPath) ? null : Path.GetDirectoryName(originalPath),
            RawDataPath = rPath,
            Status = RecoveryStatus.Recoverable,
            Quality = RecoveryQuality.Excellent
        };
    }

    private static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; } catch { return 0; }
    }

    private static DateTime? SafeFileTime(long fileTime)
    {
        if (fileTime <= 0) return null;
        try { return DateTime.FromFileTimeUtc(fileTime).ToLocalTime(); } catch { return null; }
    }
}
