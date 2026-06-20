using System.IO;
using System.Text;

namespace BookOfTheEnd.Services;

/// <summary>
/// Reads the first and last few bytes of a recovered file to determine whether
/// its structure is intact. Returns null for formats that cannot be checked.
/// </summary>
public static class FileValidationService
{
    private const int ZipTailScan = 65_536; // 64 KB for end-of-central-directory search

    public static bool? Validate(string path, string extension)
    {
        try
        {
            return extension.ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => ValidateJpeg(path),
                ".png"            => ValidatePng(path),
                ".gif"            => ValidateGif(path),
                ".zip" or ".docx" or ".xlsx" or ".pptx" or ".jar" or ".apk" => ValidateZip(path),
                ".pdf"            => ValidatePdf(path),
                _                 => null
            };
        }
        catch { return null; }
    }

    private static bool ValidateJpeg(string path)
    {
        using var fs = File.OpenRead(path);
        if (fs.Length < 4) return false;
        Span<byte> head = stackalloc byte[2];
        fs.ReadExactly(head);
        if (head[0] != 0xFF || head[1] != 0xD8) return false;
        fs.Seek(-2, SeekOrigin.End);
        Span<byte> tail = stackalloc byte[2];
        fs.ReadExactly(tail);
        return tail[0] == 0xFF && tail[1] == 0xD9;
    }

    private static bool ValidatePng(string path)
    {
        using var fs = File.OpenRead(path);
        if (fs.Length < 16) return false;
        fs.Seek(-8, SeekOrigin.End);
        Span<byte> tail = stackalloc byte[8];
        fs.ReadExactly(tail);
        ReadOnlySpan<byte> iend = [0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82];
        return tail.SequenceEqual(iend);
    }

    private static bool ValidateGif(string path)
    {
        using var fs = File.OpenRead(path);
        if (fs.Length < 6) return false;
        fs.Seek(-1, SeekOrigin.End);
        return fs.ReadByte() == 0x3B;
    }

    private static bool ValidateZip(string path)
    {
        using var fs = File.OpenRead(path);
        if (fs.Length < 22) return false;
        long scanFrom = Math.Max(0, fs.Length - ZipTailScan);
        fs.Seek(scanFrom, SeekOrigin.Begin);
        byte[] tail = new byte[(int)(fs.Length - scanFrom)];
        fs.ReadExactly(tail);
        for (int i = tail.Length - 22; i >= 0; i--)
        {
            if (tail[i] == 0x50 && tail[i + 1] == 0x4B && tail[i + 2] == 0x05 && tail[i + 3] == 0x06)
                return true;
        }
        return false;
    }

    private static bool ValidatePdf(string path)
    {
        using var fs = File.OpenRead(path);
        if (fs.Length < 8) return false;
        Span<byte> head = stackalloc byte[5];
        fs.ReadExactly(head);
        if (!head.SequenceEqual("%PDF-"u8)) return false;
        long scanFrom = Math.Max(0, fs.Length - 1024);
        fs.Seek(scanFrom, SeekOrigin.Begin);
        byte[] tail = new byte[(int)(fs.Length - scanFrom)];
        fs.ReadExactly(tail);
        return Encoding.ASCII.GetString(tail).Contains("%%EOF", StringComparison.Ordinal);
    }
}
