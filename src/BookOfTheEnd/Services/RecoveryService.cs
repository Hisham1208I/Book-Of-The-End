using System.IO;
using System.Text;
using BookOfTheEnd.Models;

namespace BookOfTheEnd.Services;

public sealed record RecoveryResult(RecoverableFile File, bool Success, string OutputPath, string? Error);

/// <summary>
/// Writes recovered files to a user-chosen destination, preserving original names,
/// extensions, folder structure, and timestamps where available. Refuses to write
/// back to the source drive to avoid overwriting the very data being recovered.
/// After writing, runs structural validation (header/footer checks) and EXIF/ID3
/// auto-rename on synthetically named files.
/// </summary>
public sealed class RecoveryService
{
    private readonly FileContentService _content;
    private readonly LoggingService _log;

    public RecoveryService(FileContentService content, LoggingService log)
    {
        _content = content;
        _log = log;
    }

    /// <summary>True when the destination resolves to the same drive being recovered from.</summary>
    public static bool IsSameDrive(IEnumerable<RecoverableFile> files, string destinationFolder)
    {
        string? destRoot = Path.GetPathRoot(Path.GetFullPath(destinationFolder))?.TrimEnd('\\', ':');
        if (string.IsNullOrEmpty(destRoot)) return false;
        return files.Any(f => string.Equals(f.DriveLetter, destRoot, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<RecoveryResult>> RecoverAsync(
        IReadOnlyList<RecoverableFile> files,
        string destinationFolder,
        bool preserveStructure,
        IProgress<(int done, int total, string current)> progress,
        CancellationToken token)
    {
        var results = new List<RecoveryResult>();
        int done = 0;

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            progress.Report((done, files.Count, file.FileName));

            RecoveryResult result;
            try
            {
                string output = await Task.Run(() => RecoverOne(file, destinationFolder, preserveStructure, token), token);
                // Status is set inside RecoverOne (Verified / Corrupt / Recovered)
                result = new RecoveryResult(file, true, output, null);
                _log.Info($"Recovered '{file.FileName}' [{file.Status}] -> {output}");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                file.Status = RecoveryStatus.Failed;
                result = new RecoveryResult(file, false, "", ex.Message);
                _log.Error($"Failed to recover '{file.FileName}'", ex);
            }

            results.Add(result);
            done++;
            progress.Report((done, files.Count, file.FileName));
        }

        WriteReport(results, destinationFolder);
        return results;
    }

    private string RecoverOne(RecoverableFile file, string destinationFolder, bool preserveStructure, CancellationToken token)
    {
        string targetDir = destinationFolder;
        if (preserveStructure && !string.IsNullOrWhiteSpace(file.OriginalPath))
        {
            string relative = StripRoot(file.OriginalPath!);
            if (!string.IsNullOrWhiteSpace(relative))
                targetDir = Path.Combine(destinationFolder, relative);
        }
        Directory.CreateDirectory(targetDir);

        string safeName = MakeSafeName(file);
        string fullPath = EnsureUnique(Path.Combine(targetDir, safeName));

        using (var fs = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            _content.WriteTo(file, fs, long.MaxValue, token);
        }

        // Auto-rename synthetically named files using EXIF date or ID3 tags
        if (file.IsNameSynthesized)
            fullPath = TryAutoRename(file, fullPath);

        // Structural validation: check header + footer integrity
        bool? valid = FileValidationService.Validate(fullPath, file.Extension);
        file.Status = valid switch
        {
            true  => RecoveryStatus.Verified,
            false => RecoveryStatus.Corrupt,
            null  => RecoveryStatus.Recovered
        };

        RestoreTimestamps(fullPath, file);
        return fullPath;
    }

    private string TryAutoRename(RecoverableFile file, string currentPath)
    {
        try
        {
            string? baseName = MetadataExtractService.TryExtractBaseName(currentPath, file.Extension);
            if (string.IsNullOrWhiteSpace(baseName)) return currentPath;

            string dir = Path.GetDirectoryName(currentPath)!;
            string newPath = EnsureUnique(Path.Combine(dir, baseName + file.Extension));
            File.Move(currentPath, newPath);
            file.FileName = Path.GetFileName(newPath);
            file.IsNameSynthesized = false;
            return newPath;
        }
        catch
        {
            return currentPath; // keep the synthesized name if anything fails
        }
    }

    private static void RestoreTimestamps(string path, RecoverableFile file)
    {
        try
        {
            if (file.Created is { } created) File.SetCreationTime(path, created);
            if (file.Modified is { } modified) File.SetLastWriteTime(path, modified);
        }
        catch
        {
            // Non-fatal: timestamps are best-effort.
        }
    }

    private static string MakeSafeName(RecoverableFile file)
    {
        string name = file.FileName;
        if (string.IsNullOrWhiteSpace(name))
            name = $"recovered_{DateTime.Now:HHmmss}{file.Extension}";

        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');

        if (string.IsNullOrWhiteSpace(Path.GetExtension(name)) && !string.IsNullOrWhiteSpace(file.Extension))
            name += file.Extension;

        return name;
    }

    private static string EnsureUnique(string fullPath)
    {
        if (!File.Exists(fullPath)) return fullPath;
        string dir = Path.GetDirectoryName(fullPath)!;
        string name = Path.GetFileNameWithoutExtension(fullPath);
        string ext = Path.GetExtension(fullPath);
        for (int i = 1; ; i++)
        {
            string candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private static string StripRoot(string path)
    {
        string p = path.Replace('/', '\\').TrimStart('\\');
        if (p.Length >= 2 && p[1] == ':') p = p.Substring(2).TrimStart('\\');
        return p;
    }

    private void WriteReport(IReadOnlyList<RecoveryResult> results, string destination)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Book of the End - Recovery Report");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Destination: {destination}");
        sb.AppendLine($"Total: {results.Count}, Succeeded: {results.Count(r => r.Success)}, Failed: {results.Count(r => !r.Success)}");
        sb.AppendLine($"Verified: {results.Count(r => r.File.Status == RecoveryStatus.Verified)}, " +
                      $"Corrupt: {results.Count(r => r.File.Status == RecoveryStatus.Corrupt)}");
        sb.AppendLine(new string('-', 60));
        foreach (var r in results)
        {
            sb.AppendLine($"[{(r.Success ? "OK" : "FAIL")}] {r.File.FileName} ({r.File.SizeDisplay}) " +
                          $"src={r.File.Source} quality={r.File.Quality} status={r.File.Status}");
            if (r.Success) sb.AppendLine($"        -> {r.OutputPath}");
            else sb.AppendLine($"        !! {r.Error}");
        }
        _log.WriteRecoveryReport(sb.ToString());
    }
}
