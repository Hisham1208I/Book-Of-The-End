using System.Text;
using BookOfTheEnd.Interop;

namespace BookOfTheEnd.Services.Fat;

/// <summary>
/// Reads a FAT32 volume and scans directory trees for deleted entries whose cluster
/// chains are still present in the File Allocation Table.
/// </summary>
public sealed class FatVolume : IDisposable
{
    private const uint FatMask = 0x0FFFFFFF;
    private const uint FatEocMin = 0x0FFFFFF8;
    private const uint FatBad = 0x0FFFFFF7;

    private readonly RawVolumeReader _reader;
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

    public FatBootSector Boot { get; }

    private FatVolume(RawVolumeReader reader, FatBootSector boot)
    {
        _reader = reader;
        Boot = boot;
        reader.SetSectorSize(boot.BytesPerSector);
    }

    public static FatVolume Open(string driveLetter, long volumeLength = 0)
    {
        var reader = RawVolumeReader.Open(driveLetter, volumeLength);
        try
        {
            byte[] bootBytes = reader.ReadAligned(0, 512);
            if (!FatBootSector.TryParse(bootBytes, out FatBootSector? boot) || boot is null)
                throw new InvalidOperationException($"{driveLetter}: is not a FAT32 volume.");
            return new FatVolume(reader, boot);
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    /// <summary>Estimated bytes of directory data to scan (used for progress).</summary>
    public long ScanBudgetBytes => Boot.TotalSectors * Boot.BytesPerSector / 8;

    public IEnumerable<FatDeletedFile> ScanDeletedFiles(CancellationToken token, Action<long>? onBytesScanned = null)
    {
        _seen.Clear();
        var progress = new ScanProgressState();
        foreach (var file in ScanDirectory(Boot.RootCluster, "", token, progress, onBytesScanned))
            yield return file;
    }

    private sealed class ScanProgressState
    {
        public long Scanned;
    }

    private IEnumerable<FatDeletedFile> ScanDirectory(
        uint dirCluster,
        string path,
        CancellationToken token,
        ScanProgressState progress,
        Action<long>? onBytesScanned)
    {
        foreach (uint cluster in GetDirectoryClusters(dirCluster, token))
        {
            token.ThrowIfCancellationRequested();
            progress.Scanned += Boot.BytesPerCluster;
            onBytesScanned?.Invoke(progress.Scanned);

            byte[] data = ReadCluster(cluster);
            var lfnParts = new SortedDictionary<int, string>();

            for (int i = 0; i + 32 <= data.Length; i += 32)
            {
                byte first = data[i];
                if (first == 0x00) break;

                byte attr = data[i + 0x0B];
                if (attr == 0x0F)
                {
                    if (!TryParseLfnEntry(data, i, out int seq, out string fragment)) continue;
                    lfnParts[seq] = fragment;
                    continue;
                }

                string? lfn = BuildLongName(lfnParts);
                lfnParts.Clear();

                bool deleted = first is 0xE5 or 0x05;
                bool isDir = (attr & 0x10) != 0;
                bool isLabel = (attr & 0x08) != 0;
                if (isLabel) continue;

                uint startCluster = ((uint)data[i + 0x14] << 16) | BitConverter.ToUInt16(data, i + 0x1A);
                long size = BitConverter.ToUInt32(data, i + 0x1C);
                DateTime? modified = ParseFatDateTime(
                    BitConverter.ToUInt16(data, i + 0x18),
                    BitConverter.ToUInt16(data, i + 0x16));

                string shortName = ReadShortName(data, i, first);
                string fileName = !string.IsNullOrWhiteSpace(lfn) ? lfn : shortName;

                if (isDir)
                {
                    if (fileName is "." or "..") continue;
                    if (startCluster >= 2)
                    {
                        string sub = CombinePath(path, fileName);
                        foreach (var nested in ScanDirectory(startCluster, sub, token, progress, onBytesScanned))
                            yield return nested;
                    }
                    continue;
                }

                if (!deleted) continue;
                if (startCluster < 2 || size <= 0) continue;

                var chain = BuildClusterChain(startCluster, size);
                if (chain.Count == 0) continue;

                string key = $"{startCluster}:{size}:{fileName}";
                if (!_seen.Add(key)) continue;

                yield return new FatDeletedFile
                {
                    FileName = fileName,
                    DirectoryPath = string.IsNullOrEmpty(path) ? null : path,
                    Size = size,
                    StartCluster = startCluster,
                    ClusterChain = chain,
                    Modified = modified,
                    HasLongFileName = !string.IsNullOrWhiteSpace(lfn)
                };
            }
        }
    }

    public byte[] ReadCluster(uint cluster)
    {
        long offset = ClusterOffset(cluster);
        return _reader.Read(offset, Boot.BytesPerCluster);
    }

    public List<uint> BuildClusterChain(uint startCluster, long fileSize)
    {
        var chain = new List<uint>();
        uint cluster = startCluster;
        long remaining = fileSize;
        int guard = 0;

        while (cluster >= 2 && remaining > 0 && guard++ < 1_000_000)
        {
            chain.Add(cluster);
            uint next = ReadFatEntry(cluster);
            if (next >= FatEocMin || next == FatBad || next < 2) break;
            cluster = next;
            remaining -= Boot.BytesPerCluster;
        }

        return chain;
    }

    private IEnumerable<uint> GetDirectoryClusters(uint dirCluster, CancellationToken token)
    {
        uint cluster = dirCluster;
        int guard = 0;
        while (cluster >= 2 && guard++ < 100_000)
        {
            token.ThrowIfCancellationRequested();
            yield return cluster;
            uint next = ReadFatEntry(cluster);
            if (next >= FatEocMin || next == FatBad || next < 2) yield break;
            cluster = next;
        }
    }

    private uint ReadFatEntry(uint cluster)
    {
        long offset = Boot.FatStartOffset + cluster * 4L;
        byte[] bytes = _reader.Read(offset, 4);
        if (bytes.Length < 4) return 0;
        return BitConverter.ToUInt32(bytes, 0) & FatMask;
    }

    private long ClusterOffset(uint cluster) =>
        Boot.DataStartOffset + (long)(cluster - 2) * Boot.BytesPerCluster;

    private static bool TryParseLfnEntry(byte[] data, int offset, out int sequence, out string fragment)
    {
        sequence = 0;
        fragment = "";
        byte seqByte = data[offset];
        if (seqByte == 0xE5) return false;

        sequence = seqByte & 0x3F;
        if (sequence is 0 or > 40) return false;

        var sb = new StringBuilder(13);
        AppendChars(sb, data, offset + 0x01, 5);
        AppendChars(sb, data, offset + 0x0E, 6);
        AppendChars(sb, data, offset + 0x1C, 2);
        fragment = sb.ToString();
        return fragment.Length > 0;
    }

    private static void AppendChars(StringBuilder sb, byte[] data, int offset, int charCount)
    {
        for (int i = 0; i < charCount * 2; i += 2)
        {
            if (offset + i + 1 >= data.Length) break;
            char c = (char)BitConverter.ToUInt16(data, offset + i);
            if (c == '\0' || c == 0xFFFF) break;
            sb.Append(c);
        }
    }

    private static string BuildLongName(SortedDictionary<int, string> parts)
    {
        if (parts.Count == 0) return "";
        var sb = new StringBuilder();
        foreach (int key in parts.Keys.OrderBy(k => k))
            sb.Append(parts[key]);
        return sb.ToString().Trim();
    }

    private static string ReadShortName(byte[] data, int offset, byte first)
    {
        var nameBytes = new byte[8];
        Array.Copy(data, offset, nameBytes, 0, 8);
        nameBytes[0] = first switch
        {
            0xE5 => (byte)'?',
            0x05 => 0xE5,
            _ => first
        };
        string body = Encoding.ASCII.GetString(nameBytes).TrimEnd(' ');
        string ext = Encoding.ASCII.GetString(data, offset + 8, 3).TrimEnd(' ');
        if (string.IsNullOrWhiteSpace(ext)) return body;
        return body + "." + ext;
    }

    private static string CombinePath(string parent, string child)
    {
        if (string.IsNullOrEmpty(parent)) return child;
        return $"{parent}\\{child}";
    }

    private static DateTime? ParseFatDateTime(ushort date, ushort time)
    {
        if (date == 0) return null;
        try
        {
            int day = date & 0x1F;
            int month = (date >> 5) & 0x0F;
            int year = ((date >> 9) & 0x7F) + 1980;
            int second = (time & 0x1F) * 2;
            int minute = (time >> 5) & 0x3F;
            int hour = (time >> 11) & 0x1F;
            return new DateTime(year, month, day, hour, minute, second);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => _reader.Dispose();
}
