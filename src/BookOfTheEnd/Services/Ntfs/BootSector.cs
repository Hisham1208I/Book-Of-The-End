namespace BookOfTheEnd.Services.Ntfs;

/// <summary>Parsed NTFS boot sector ($Boot) geometry.</summary>
public sealed class BootSector
{
    public int BytesPerSector { get; private init; }
    public int SectorsPerCluster { get; private init; }
    public int BytesPerCluster => BytesPerSector * SectorsPerCluster;
    public long MftStartCluster { get; private init; }
    public int MftRecordSize { get; private init; }

    public long MftByteOffset => MftStartCluster * BytesPerCluster;

    public static bool TryParse(byte[] boot, out BootSector? result)
    {
        result = null;
        if (boot.Length < 0x50) return false;

        // "NTFS    " OEM id at offset 3.
        if (boot[3] != (byte)'N' || boot[4] != (byte)'T' || boot[5] != (byte)'F' || boot[6] != (byte)'S')
            return false;

        int bytesPerSector = BitConverter.ToUInt16(boot, 0x0B);
        int sectorsPerCluster = boot[0x0D];
        if (bytesPerSector == 0 || sectorsPerCluster == 0) return false;

        long mftCluster = BitConverter.ToInt64(boot, 0x30);

        sbyte clustersPerRecord = unchecked((sbyte)boot[0x40]);
        int recordSize = clustersPerRecord >= 0
            ? clustersPerRecord * bytesPerSector * sectorsPerCluster
            : 1 << (-clustersPerRecord);

        if (recordSize <= 0) recordSize = 1024;

        result = new BootSector
        {
            BytesPerSector = bytesPerSector,
            SectorsPerCluster = sectorsPerCluster,
            MftStartCluster = mftCluster,
            MftRecordSize = recordSize
        };
        return true;
    }
}
