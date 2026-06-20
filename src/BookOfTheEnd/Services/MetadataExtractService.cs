using System.IO;
using System.Text;

namespace BookOfTheEnd.Services;

/// <summary>
/// Extracts human-readable metadata from recovered file bytes so that synthetically
/// named files (e.g. recovered_1A3F0000.jpg) can be renamed to something meaningful
/// (e.g. 2022-07-15 14-23-05.jpg, or Artist - Title.mp3).
/// </summary>
public static class MetadataExtractService
{
    /// <summary>
    /// Reads <paramref name="path"/> and returns a better base filename (without extension),
    /// or null when no useful metadata was found.
    /// </summary>
    public static string? TryExtractBaseName(string path, string extension)
    {
        try
        {
            return extension.ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => ExtractJpegDate(path),
                ".mp3"            => ExtractMp3Tags(path),
                _                 => null
            };
        }
        catch { return null; }
    }

    // ── JPEG EXIF ──────────────────────────────────────────────────────────────

    private static string? ExtractJpegDate(string path)
    {
        using var fs = File.OpenRead(path);
        if (fs.Length < 12) return null;

        // Must start with SOI marker FF D8
        if (fs.ReadByte() != 0xFF || fs.ReadByte() != 0xD8) return null;

        while (fs.Position + 4 <= fs.Length)
        {
            if (fs.ReadByte() != 0xFF) break;
            int marker = fs.ReadByte();
            int segLen = ReadUInt16BE(fs) - 2;
            if (segLen < 0) break;

            if (marker == 0xE1 && segLen >= 12) // APP1
            {
                long segStart = fs.Position;
                byte[] seg = new byte[segLen];
                fs.ReadExactly(seg, 0, segLen);
                string? date = ParseExifDate(seg);
                if (date is not null) return date;
                fs.Seek(segStart + segLen, SeekOrigin.Begin);
            }
            else
            {
                fs.Seek(segLen, SeekOrigin.Current);
            }
        }
        return null;
    }

    private static string? ParseExifDate(byte[] app1)
    {
        // Expect "Exif\0\0" header
        if (app1.Length < 14) return null;
        if (app1[0] != 'E' || app1[1] != 'x' || app1[2] != 'i' || app1[3] != 'f') return null;

        int tiff = 6; // offset of TIFF header within app1
        bool le = app1[tiff] == 0x49; // 'II' = little-endian, 'MM' = big-endian

        uint ifd0Off = R32(app1, tiff + 4, le);
        int ifdPos = tiff + (int)ifd0Off;
        if (ifdPos + 2 > app1.Length) return null;

        int count = R16(app1, ifdPos, le);
        ifdPos += 2;

        uint subIfdOff = 0;
        for (int i = 0; i < count && ifdPos + 12 <= app1.Length; i++, ifdPos += 12)
        {
            ushort tag = R16(app1, ifdPos, le);
            if (tag == 0x8769) subIfdOff = R32(app1, ifdPos + 8, le); // ExifIFD pointer
            else if (tag == 0x0132) return ReadAsciiDate(app1, ifdPos, tiff, le); // DateTime
        }

        // Look in ExifIFD for DateTimeOriginal (preferred)
        if (subIfdOff > 0)
        {
            int subPos = tiff + (int)subIfdOff;
            if (subPos + 2 <= app1.Length)
            {
                int subCount = R16(app1, subPos, le);
                subPos += 2;
                for (int i = 0; i < subCount && subPos + 12 <= app1.Length; i++, subPos += 12)
                {
                    ushort tag = R16(app1, subPos, le);
                    if (tag == 0x9003) return ReadAsciiDate(app1, subPos, tiff, le); // DateTimeOriginal
                }
            }
        }
        return null;
    }

    private static string? ReadAsciiDate(byte[] data, int entryPos, int tiffBase, bool le)
    {
        uint count = R32(data, entryPos + 4, le);
        if (count < 19) return null;
        uint valOff = R32(data, entryPos + 8, le);
        int strPos = tiffBase + (int)valOff;
        if (strPos + 19 > data.Length) return null;
        string raw = Encoding.ASCII.GetString(data, strPos, 19); // "YYYY:MM:DD HH:MM:SS"
        if (raw.Length < 19 || !char.IsDigit(raw[0])) return null;
        // Verify it's not a null date
        if (raw.StartsWith("0000", StringComparison.Ordinal)) return null;
        // Make filesystem-safe: "YYYY-MM-DD HH-MM-SS"
        return raw[..4] + "-" + raw[5..7] + "-" + raw[8..10] + " " + raw[11..13] + "-" + raw[14..16] + "-" + raw[17..19];
    }

    // ── MP3 ID3v2 ──────────────────────────────────────────────────────────────

    private static string? ExtractMp3Tags(string path)
    {
        using var fs = File.OpenRead(path);
        if (fs.Length < 10) return null;

        byte[] hdr = new byte[10];
        fs.ReadExactly(hdr, 0, 10);
        if (hdr[0] != 'I' || hdr[1] != 'D' || hdr[2] != '3') return null;

        // Syncsafe integer tag size
        int tagSize = ((hdr[6] & 0x7F) << 21) | ((hdr[7] & 0x7F) << 14)
                    | ((hdr[8] & 0x7F) << 7) | (hdr[9] & 0x7F);
        if (tagSize < 10 || tagSize > 10_485_760) return null; // cap at 10 MB

        byte[] tag = new byte[Math.Min(tagSize, 131_072)];
        fs.ReadExactly(tag, 0, tag.Length);

        string? title  = ReadId3Frame(tag, "TIT2");
        string? artist = ReadId3Frame(tag, "TPE1");

        if (title is null && artist is null) return null;
        string name = (artist, title) switch
        {
            (not null, not null) => $"{artist} - {title}",
            (not null, null)     => artist,
            (null, not null)     => title,
            _                    => ""
        };
        return SanitizeName(name);
    }

    private static string? ReadId3Frame(byte[] tag, string id)
    {
        byte b0 = (byte)id[0], b1 = (byte)id[1], b2 = (byte)id[2], b3 = (byte)id[3];
        for (int i = 0; i + 10 < tag.Length; i++)
        {
            if (tag[i] != b0 || tag[i+1] != b1 || tag[i+2] != b2 || tag[i+3] != b3) continue;
            int size = (tag[i+4] << 24) | (tag[i+5] << 16) | (tag[i+6] << 8) | tag[i+7];
            if (size <= 1 || i + 10 + size > tag.Length) continue;
            byte enc = tag[i + 10];
            int textStart = i + 11;
            int textLen = size - 1;
            string text = enc switch
            {
                1 => Encoding.Unicode.GetString(tag, textStart, textLen),
                3 => Encoding.UTF8.GetString(tag, textStart, textLen),
                _ => Encoding.Latin1.GetString(tag, textStart, textLen)
            };
            text = text.Trim('\0', '\r', '\n', ' ');
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        return null;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string SanitizeName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Length > 120 ? name[..120] : name;
    }

    private static int ReadUInt16BE(Stream s) => (s.ReadByte() << 8) | s.ReadByte();

    private static ushort R16(byte[] d, int off, bool le) =>
        off + 2 > d.Length ? (ushort)0 :
        le ? (ushort)(d[off] | (d[off+1] << 8))
           : (ushort)((d[off] << 8) | d[off+1]);

    private static uint R32(byte[] d, int off, bool le) =>
        (uint)(off + 4 > d.Length ? 0 :
        le ? (d[off] | (d[off+1] << 8) | (d[off+2] << 16) | (d[off+3] << 24))
           : ((d[off] << 24) | (d[off+1] << 16) | (d[off+2] << 8) | d[off+3]));
}
