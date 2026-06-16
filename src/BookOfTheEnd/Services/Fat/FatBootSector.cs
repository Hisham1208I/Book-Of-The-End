using System.Text;

namespace BookOfTheEnd.Services.Fat;

/// <summary>Parsed FAT32 BIOS Parameter Block from sector 0.</summary>
public sealed class FatBootSector
{
    public int BytesPerSector { get; private init; }
    public int SectorsPerCluster { get; private init; }
    public int ReservedSectors { get; private init; }
    public int NumberOfFats { get; private init; }
    public long SectorsPerFat { get; private init; }
    public uint RootCluster { get; private init; }
    public long TotalSectors { get; private init; }

    public int BytesPerCluster => BytesPerSector * SectorsPerCluster;

    public long FatStartOffset => (long)ReservedSectors * BytesPerSector;

    public long DataStartOffset => (ReservedSectors + NumberOfFats * SectorsPerFat) * (long)BytesPerSector;

    public static bool TryParse(byte[] boot, out FatBootSector? sector)
    {
        sector = null;
        if (boot.Length < 512) return false;
        if (boot[510] != 0x55 || boot[511] != 0xAA) return false;

        int bytesPerSector = BitConverter.ToUInt16(boot, 0x0B);
        if (bytesPerSector is not (512 or 1024 or 2048 or 4096)) return false;

        int sectorsPerCluster = boot[0x0D];
        if (sectorsPerCluster is 0 or > 128 || !IsPowerOfTwo(sectorsPerCluster)) return false;

        int reserved = BitConverter.ToUInt16(boot, 0x0E);
        int fats = boot[0x10];
        if (fats is 0 or > 2) return false;

        ushort rootEntries = BitConverter.ToUInt16(boot, 0x11);
        ushort fat16Size = BitConverter.ToUInt16(boot, 0x16);
        if (rootEntries != 0 || fat16Size != 0) return false;

        string fsType = Encoding.ASCII.GetString(boot, 0x52, 8).Trim();
        if (!fsType.StartsWith("FAT32", StringComparison.OrdinalIgnoreCase)) return false;

        long sectorsPerFat = BitConverter.ToUInt32(boot, 0x24);
        if (sectorsPerFat <= 0) return false;

        uint rootCluster = BitConverter.ToUInt32(boot, 0x2C);
        if (rootCluster < 2) return false;

        long total = BitConverter.ToUInt16(boot, 0x13);
        if (total == 0) total = BitConverter.ToUInt32(boot, 0x20);
        if (total <= 0) return false;

        sector = new FatBootSector
        {
            BytesPerSector = bytesPerSector,
            SectorsPerCluster = sectorsPerCluster,
            ReservedSectors = reserved,
            NumberOfFats = fats,
            SectorsPerFat = sectorsPerFat,
            RootCluster = rootCluster,
            TotalSectors = total
        };
        return true;
    }

    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;
}
