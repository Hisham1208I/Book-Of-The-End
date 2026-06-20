using System.IO;
using System.Text;
using BookOfTheEnd.Models;

namespace BookOfTheEnd.Services;

public enum PreviewKind { None, Image, Text, Pdf, Media }

public sealed class PreviewResult
{
    public PreviewKind Kind { get; init; } = PreviewKind.None;
    public byte[]? ImageBytes { get; init; }
    public string? Text { get; init; }
    public string? TempFilePath { get; init; }
    public string Message { get; init; } = "";
    public RecoverableFile? File { get; init; }
}

/// <summary>
/// Produces previews before recovery by extracting (a bounded amount of) the file
/// content to memory or a temp file the WPF controls can render.
/// </summary>
public sealed class PreviewService
{
    private const long ImageCap = 40L * 1024 * 1024;
    private const long TextCap = 512L * 1024;
    private const long MediaCap = 300L * 1024 * 1024;

    private readonly FileContentService _content;
    private readonly LoggingService _log;
    private readonly string _tempDir;

    public PreviewService(FileContentService content, LoggingService log)
    {
        _content = content;
        _log = log;
        _tempDir = Path.Combine(Path.GetTempPath(), "BookOfTheEnd", "preview");
        Directory.CreateDirectory(_tempDir);
    }

    public Task<PreviewResult> CreateAsync(RecoverableFile file, CancellationToken token)
    {
        return Task.Run(() =>
        {
            try
            {
                return file.Category switch
                {
                    FileCategory.Image => Image(file),
                    FileCategory.Document when IsText(file.Extension) => TextPreview(file),
                    FileCategory.Document when file.Extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
                        => MediaOrDoc(file, PreviewKind.Pdf, token),
                    FileCategory.Audio => MediaOrDoc(file, PreviewKind.Media, token),
                    FileCategory.Video => MediaOrDoc(file, PreviewKind.Media, token),
                    _ => HexDump(file)
                };
            }
            catch (Exception ex)
            {
                _log.Warn($"Preview failed for {file.FileName}: {ex.Message}");
                return new PreviewResult { Kind = PreviewKind.None, File = file, Message = $"Preview unavailable: {ex.Message}" };
            }
        }, token);
    }

    private PreviewResult Image(RecoverableFile file)
    {
        byte[] bytes = _content.ReadBytes(file, ImageCap);
        return new PreviewResult { Kind = PreviewKind.Image, ImageBytes = bytes, File = file };
    }

    private PreviewResult TextPreview(RecoverableFile file)
    {
        byte[] bytes = _content.ReadBytes(file, TextCap);
        string text = DecodeText(bytes);
        return new PreviewResult { Kind = PreviewKind.Text, Text = text, File = file };
    }

    private PreviewResult MediaOrDoc(RecoverableFile file, PreviewKind kind, CancellationToken token)
    {
        string temp = Path.Combine(_tempDir, $"preview_{Guid.NewGuid():N}{file.Extension}");
        using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            _content.WriteTo(file, fs, MediaCap, token);
        }
        return new PreviewResult { Kind = kind, TempFilePath = temp, File = file };
    }

    private PreviewResult HexDump(RecoverableFile file)
    {
        const int maxBytes = 256;
        byte[] bytes = _content.ReadBytes(file, maxBytes);
        if (bytes.Length == 0)
            return new PreviewResult { Kind = PreviewKind.None, File = file, Message = "File appears to be empty." };

        var sb = new StringBuilder();
        sb.AppendLine($"Hex preview — first {bytes.Length} bytes of {file.SizeDisplay}");
        sb.AppendLine();
        for (int row = 0; row < bytes.Length; row += 16)
        {
            sb.Append($"{row:X4}  ");
            int end = Math.Min(row + 16, bytes.Length);
            for (int i = row; i < end; i++)
                sb.Append($"{bytes[i]:X2} ");
            for (int i = end; i < row + 16; i++)
                sb.Append("   ");
            sb.Append(" ");
            for (int i = row; i < end; i++)
            {
                char c = (char)bytes[i];
                sb.Append(c >= 0x20 && c < 0x7F ? c : '.');
            }
            sb.AppendLine();
        }
        return new PreviewResult { Kind = PreviewKind.Text, Text = sb.ToString(), File = file };
    }

    private static bool IsText(string ext) =>
        ext.ToLowerInvariant() is ".txt" or ".csv" or ".log" or ".rtf" or ".json" or ".xml" or ".md";

    private static string DecodeText(byte[] bytes)
    {
        if (bytes.Length == 0) return "(empty file)";
        // Honor a UTF-8/UTF-16 BOM when present, otherwise default to UTF-8.
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        return Encoding.UTF8.GetString(bytes);
    }

    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch { /* best effort */ }
    }
}
