using BookOfTheEnd.Interop;
using BookOfTheEnd.Models;

namespace BookOfTheEnd.Services.Ntfs;

/// <summary>
/// High-level NTFS reader: boots from the raw volume, locates the MFT, enumerates
/// records (deleted and live), and reconstructs file content from data runs.
/// </summary>
public sealed class NtfsVolume : IDisposable
{
    private readonly RawVolumeReader _reader;

    public BootSector Boot { get; }

    /// <summary>Data runs describing where the MFT itself lives on the volume.</summary>
    private readonly List<DataRun> _mftRuns;

    private NtfsVolume(RawVolumeReader reader, BootSector boot, List<DataRun> mftRuns)
    {
        _reader = reader;
        Boot = boot;
        _mftRuns = mftRuns;
    }

    public static NtfsVolume Open(string driveLetter, long volumeLength = 0)
    {
        var reader = RawVolumeReader.Open(driveLetter, volumeLength);
        try
        {
            byte[] bootBytes = reader.ReadAligned(0, 512);
            if (!BootSector.TryParse(bootBytes, out BootSector? boot) || boot is null)
                throw new InvalidOperationException($"{driveLetter}: is not an NTFS volume.");

            reader.SetSectorSize(boot.BytesPerSector);

            // Read MFT record 0 ($MFT) to discover the MFT's own layout.
            byte[] rec0 = reader.Read(boot.MftByteOffset, boot.MftRecordSize);
            ApplyFixup(rec0, boot.BytesPerSector);
            MftRecord mft = MftRecord.Parse(rec0);

            List<DataRun> runs = mft.DataRuns is { Count: > 0 }
                ? new List<DataRun>(mft.DataRuns)
                // Fallback: treat the MFT as a single run starting at its cluster.
                : new List<DataRun> { new(boot.MftStartCluster, 1 << 20) };

            return new NtfsVolume(reader, boot, runs);
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    /// <summary>Estimated total bytes of the MFT (used for progress reporting).</summary>
    public long MftSizeBytes
    {
        get
        {
            long clusters = 0;
            foreach (var run in _mftRuns)
                if (!run.IsSparse) clusters += run.ClusterCount;
            return clusters * Boot.BytesPerCluster;
        }
    }

    /// <summary>
    /// Enumerates every MFT record. <paramref name="onBytesRead"/> reports the running
    /// number of MFT bytes consumed for progress.
    /// </summary>
    public IEnumerable<MftRecord> EnumerateRecords(Action<long>? onBytesRead = null)
    {
        int recordSize = Boot.MftRecordSize;
        int bytesPerCluster = Boot.BytesPerCluster;
        int chunkClusters = Math.Max(1, (8 * 1024 * 1024) / bytesPerCluster);
        var chunkBuffer = new byte[chunkClusters * bytesPerCluster];

        var leftover = new List<byte>(recordSize);
        long totalRead = 0;

        foreach (var run in _mftRuns)
        {
            if (run.IsSparse) continue;
            long clustersRemaining = run.ClusterCount;
            long clusterIndex = 0;

            while (clustersRemaining > 0)
            {
                int take = (int)Math.Min(chunkClusters, clustersRemaining);
                long offset = (run.StartCluster + clusterIndex) * bytesPerCluster;
                int wanted = take * bytesPerCluster;
                int got = _reader.ReadInto(offset, chunkBuffer, wanted);
                if (got <= 0) break;

                totalRead += got;
                onBytesRead?.Invoke(totalRead);

                int dataLen = got;
                int cursor = 0;

                // Drain any leftover bytes from the previous chunk first.
                if (leftover.Count > 0)
                {
                    int need = recordSize - leftover.Count;
                    int avail = Math.Min(need, dataLen);
                    for (int i = 0; i < avail; i++) leftover.Add(chunkBuffer[i]);
                    cursor = avail;
                    if (leftover.Count == recordSize)
                    {
                        byte[] rec = leftover.ToArray();
                        leftover.Clear();
                        ApplyFixup(rec, Boot.BytesPerSector);
                        yield return MftRecord.Parse(rec);
                    }
                }

                while (cursor + recordSize <= dataLen)
                {
                    var rec = new byte[recordSize];
                    Array.Copy(chunkBuffer, cursor, rec, 0, recordSize);
                    cursor += recordSize;
                    ApplyFixup(rec, Boot.BytesPerSector);
                    yield return MftRecord.Parse(rec);
                }

                // Stash trailing partial record.
                for (int i = cursor; i < dataLen; i++) leftover.Add(chunkBuffer[i]);

                clusterIndex += take;
                clustersRemaining -= take;
            }
        }
    }

    /// <summary>Reconstructs file bytes from non-resident data runs, trimmed to real size.</summary>
    public byte[] ReadContent(IReadOnlyList<DataRun> runs, long realSize)
    {
        long remaining = realSize;
        var output = new byte[realSize];
        int written = 0;
        int bytesPerCluster = Boot.BytesPerCluster;
        var buffer = new byte[Math.Min(8 * 1024 * 1024, Math.Max(bytesPerCluster, (int)Math.Min(realSize, int.MaxValue)))];

        foreach (var run in runs)
        {
            if (remaining <= 0) break;
            if (run.IsSparse)
            {
                long zeroBytes = Math.Min(remaining, run.ClusterCount * bytesPerCluster);
                written += (int)zeroBytes; // output already zero-initialized
                remaining -= zeroBytes;
                continue;
            }

            long runBytes = run.ClusterCount * bytesPerCluster;
            long offset = run.StartCluster * bytesPerCluster;
            long toCopy = Math.Min(remaining, runBytes);
            long copied = 0;

            while (copied < toCopy)
            {
                int chunk = (int)Math.Min(buffer.Length, toCopy - copied);
                // Align chunk up to sector for the physical read, then slice.
                int alignedChunk = chunk;
                int sector = _reader.SectorSize;
                if (alignedChunk % sector != 0) alignedChunk += sector - (alignedChunk % sector);
                if (alignedChunk > buffer.Length) alignedChunk = buffer.Length;
                int got = _reader.ReadInto(offset + copied, buffer, alignedChunk);
                if (got <= 0) break;
                int usable = (int)Math.Min(chunk, got);
                Array.Copy(buffer, 0, output, written, usable);
                written += usable;
                copied += usable;
                remaining -= usable;
            }
        }

        if (written < output.Length) Array.Resize(ref output, written);
        return output;
    }

    /// <summary>
    /// Applies the NTFS Update Sequence Array fixup, restoring the last two bytes of
    /// each sector that were swapped out for the update sequence number.
    /// </summary>
    private static void ApplyFixup(byte[] record, int bytesPerSector)
    {
        if (record.Length < 0x08) return;
        int usaOffset = BitConverter.ToUInt16(record, 0x04);
        int usaCount = BitConverter.ToUInt16(record, 0x06);
        if (usaCount <= 1) return;
        if (usaOffset + usaCount * 2 > record.Length) return;

        for (int i = 1; i < usaCount; i++)
        {
            int sectorEnd = i * bytesPerSector - 2;
            if (sectorEnd + 2 > record.Length) break;
            record[sectorEnd] = record[usaOffset + i * 2];
            record[sectorEnd + 1] = record[usaOffset + i * 2 + 1];
        }
    }

    public void Dispose() => _reader.Dispose();
}
