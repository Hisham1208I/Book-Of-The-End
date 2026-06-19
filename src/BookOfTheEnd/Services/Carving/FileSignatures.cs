using BookOfTheEnd.Models;

namespace BookOfTheEnd.Services.Carving;

public enum SizeStrategy
{
    /// <summary>Scan forward for a footer byte pattern.</summary>
    Footer,
    /// <summary>RIFF container: 32-bit little-endian size at offset 4 (+8).</summary>
    RiffSize,
    /// <summary>BMP: 32-bit little-endian size at offset 2.</summary>
    BmpSize,
    /// <summary>No reliable terminator: carve a bounded default block.</summary>
    Fixed
}

/// <summary>Describes how to detect and bound one carvable file type.</summary>
public sealed class FileSignature
{
    public required string Extension { get; init; }
    public required FileCategory Category { get; init; }
    public required byte[] Header { get; init; }

    /// <summary>Offset within the file where <see cref="Header"/> appears (e.g. ftyp at 4).</summary>
    public int HeaderOffset { get; init; }

    public byte[]? Footer { get; init; }
    public SizeStrategy Strategy { get; init; } = SizeStrategy.Footer;
    public long MaxSize { get; init; } = 50L * 1024 * 1024;
    public long FixedSize { get; init; } = 4L * 1024 * 1024;
    public RecoveryQuality Quality { get; init; } = RecoveryQuality.Good;
}

public static class FileSignatures
{
    public static readonly IReadOnlyList<FileSignature> All = new List<FileSignature>
    {
        new() { Extension = ".jpg", Category = FileCategory.Image,
            Header = new byte[]{0xFF,0xD8,0xFF}, Footer = new byte[]{0xFF,0xD9},
            Strategy = SizeStrategy.Footer, MaxSize = 40L*1024*1024, Quality = RecoveryQuality.Good },
        new() { Extension = ".png", Category = FileCategory.Image,
            Header = new byte[]{0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A},
            Footer = new byte[]{0x49,0x45,0x4E,0x44,0xAE,0x42,0x60,0x82},
            Strategy = SizeStrategy.Footer, MaxSize = 60L*1024*1024, Quality = RecoveryQuality.Excellent },
        new() { Extension = ".gif", Category = FileCategory.Image,
            Header = new byte[]{0x47,0x49,0x46,0x38}, Footer = new byte[]{0x3B},
            Strategy = SizeStrategy.Footer, MaxSize = 30L*1024*1024 },
        new() { Extension = ".bmp", Category = FileCategory.Image,
            Header = new byte[]{0x42,0x4D}, Strategy = SizeStrategy.BmpSize, MaxSize = 40L*1024*1024 },
        new() { Extension = ".tif", Category = FileCategory.Image,
            Header = new byte[]{0x49,0x49,0x2A,0x00}, Strategy = SizeStrategy.Fixed,
            FixedSize = 8L*1024*1024, MaxSize = 80L*1024*1024, Quality = RecoveryQuality.Fair },
        new() { Extension = ".pdf", Category = FileCategory.Document,
            Header = new byte[]{0x25,0x50,0x44,0x46}, Footer = new byte[]{0x25,0x25,0x45,0x4F,0x46},
            Strategy = SizeStrategy.Footer, MaxSize = 100L*1024*1024, Quality = RecoveryQuality.Good },
        new() { Extension = ".zip", Category = FileCategory.Archive,
            Header = new byte[]{0x50,0x4B,0x03,0x04}, Footer = new byte[]{0x50,0x4B,0x05,0x06},
            Strategy = SizeStrategy.Footer, MaxSize = 200L*1024*1024, Quality = RecoveryQuality.Fair },
        new() { Extension = ".rar", Category = FileCategory.Archive,
            Header = new byte[]{0x52,0x61,0x72,0x21,0x1A,0x07}, Strategy = SizeStrategy.Fixed,
            FixedSize = 16L*1024*1024, MaxSize = 200L*1024*1024, Quality = RecoveryQuality.Fair },
        new() { Extension = ".7z", Category = FileCategory.Archive,
            Header = new byte[]{0x37,0x7A,0xBC,0xAF,0x27,0x1C}, Strategy = SizeStrategy.Fixed,
            FixedSize = 16L*1024*1024, MaxSize = 200L*1024*1024, Quality = RecoveryQuality.Fair },
        new() { Extension = ".mp4", Category = FileCategory.Video,
            Header = new byte[]{0x66,0x74,0x79,0x70}, HeaderOffset = 4, Strategy = SizeStrategy.Fixed,
            FixedSize = 32L*1024*1024, MaxSize = 500L*1024*1024, Quality = RecoveryQuality.Fair },
        new() { Extension = ".avi", Category = FileCategory.Video,
            Header = new byte[]{0x52,0x49,0x46,0x46}, Strategy = SizeStrategy.RiffSize,
            MaxSize = 500L*1024*1024, Quality = RecoveryQuality.Good },
        new() { Extension = ".mkv", Category = FileCategory.Video,
            Header = new byte[]{0x1A,0x45,0xDF,0xA3}, Strategy = SizeStrategy.Fixed,
            FixedSize = 32L*1024*1024, MaxSize = 500L*1024*1024, Quality = RecoveryQuality.Fair },
        new() { Extension = ".wav", Category = FileCategory.Audio,
            Header = new byte[]{0x52,0x49,0x46,0x46}, Strategy = SizeStrategy.RiffSize,
            MaxSize = 200L*1024*1024, Quality = RecoveryQuality.Good },
        new() { Extension = ".flac", Category = FileCategory.Audio,
            Header = new byte[]{0x66,0x4C,0x61,0x43}, Strategy = SizeStrategy.Fixed,
            FixedSize = 16L*1024*1024, MaxSize = 100L*1024*1024, Quality = RecoveryQuality.Fair },
        new() { Extension = ".mp3", Category = FileCategory.Audio,
            Header = new byte[]{0x49,0x44,0x33}, Strategy = SizeStrategy.Fixed,
            FixedSize = 8L*1024*1024, MaxSize = 50L*1024*1024, Quality = RecoveryQuality.Fair },
    };

    /// <summary>Largest header offset+length, used to size inter-chunk overlap.</summary>
    public static int MaxHeaderSpan { get; } =
        All.Max(s => s.HeaderOffset + s.Header.Length);
}
