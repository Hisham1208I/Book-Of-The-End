using System.IO;
using BookOfTheEnd.Interop;
using BookOfTheEnd.Models;

namespace BookOfTheEnd.Services;

/// <summary>
/// Reconstructs the bytes of a <see cref="RecoverableFile"/> regardless of source
/// (Recycle Bin $R file, resident MFT data, non-resident cluster runs, or carved offset).
/// </summary>
public sealed class FileContentService
{
    private const int CopyBuffer = 4 * 1024 * 1024;

    /// <summary>Writes up to <paramref name="maxBytes"/> of file content to a stream.</summary>
    public void WriteTo(RecoverableFile file, Stream destination, long maxBytes, CancellationToken token)
    {
        switch (file.Source)
        {
            case RecoverySource.RecycleBin:
                WriteFromPath(file.RawDataPath!, destination, maxBytes, token);
                break;

            case RecoverySource.MasterFileTable when file.ResidentData is { Length: > 0 }:
                int residentLen = (int)Math.Min(file.ResidentData.Length, maxBytes);
                destination.Write(file.ResidentData, 0, residentLen);
                break;

            case RecoverySource.MasterFileTable:
                WriteFromRuns(file, destination, maxBytes, token);
                break;

            case RecoverySource.Carved:
                WriteFromOffset(file, destination, maxBytes, token);
                break;

            case RecoverySource.Fat32Directory:
                WriteFromFatClusters(file, destination, maxBytes, token);
                break;
        }
    }

    public byte[] ReadBytes(RecoverableFile file, long maxBytes)
    {
        using var ms = new MemoryStream();
        WriteTo(file, ms, maxBytes, CancellationToken.None);
        return ms.ToArray();
    }

    private static void WriteFromPath(string path, Stream destination, long maxBytes, CancellationToken token)
    {
        using var src = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var buffer = new byte[CopyBuffer];
        long remaining = maxBytes;
        int read;
        while (remaining > 0 && (read = src.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining))) > 0)
        {
            token.ThrowIfCancellationRequested();
            destination.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    private static void WriteFromRuns(RecoverableFile file, Stream destination, long maxBytes, CancellationToken token)
    {
        if (file.DataRuns is null || file.BytesPerCluster <= 0) return;
        using var reader = RawVolumeReader.Open(file.DriveLetter);
        int bpc = file.BytesPerCluster;
        long remaining = Math.Min(file.Size, maxBytes);

        foreach (var run in file.DataRuns)
        {
            if (remaining <= 0) break;
            if (run.IsSparse)
            {
                long zeros = Math.Min(remaining, run.ClusterCount * (long)bpc);
                WriteZeros(destination, zeros);
                remaining -= zeros;
                continue;
            }

            long runBytes = run.ClusterCount * (long)bpc;
            long offset = run.StartCluster * (long)bpc;
            long toCopy = Math.Min(remaining, runBytes);
            long copied = 0;
            while (copied < toCopy)
            {
                token.ThrowIfCancellationRequested();
                int chunk = (int)Math.Min(CopyBuffer, toCopy - copied);
                byte[] bytes = reader.Read(offset + copied, chunk);
                if (bytes.Length == 0) break;
                destination.Write(bytes, 0, bytes.Length);
                copied += bytes.Length;
                remaining -= bytes.Length;
            }
        }
    }

    private static void WriteFromFatClusters(RecoverableFile file, Stream destination, long maxBytes, CancellationToken token)
    {
        if (file.FatClusterChain is null or { Count: 0 } || file.BytesPerCluster <= 0) return;
        using var reader = RawVolumeReader.Open(file.DriveLetter);
        long remaining = Math.Min(file.Size, maxBytes);
        int bpc = file.BytesPerCluster;

        foreach (uint cluster in file.FatClusterChain)
        {
            if (remaining <= 0) break;
            long offset = file.FatDataStartOffset + (long)(cluster - 2) * bpc;
            int toRead = (int)Math.Min(bpc, remaining);
            byte[] bytes = reader.Read(offset, toRead);
            if (bytes.Length == 0) break;
            destination.Write(bytes, 0, bytes.Length);
            remaining -= bytes.Length;
            token.ThrowIfCancellationRequested();
        }
    }

    private static void WriteFromOffset(RecoverableFile file, Stream destination, long maxBytes, CancellationToken token)
    {
        using var reader = RawVolumeReader.Open(file.DriveLetter);
        long remaining = Math.Min(file.Size, maxBytes);
        long offset = file.CarveOffset;
        while (remaining > 0)
        {
            token.ThrowIfCancellationRequested();
            int chunk = (int)Math.Min(CopyBuffer, remaining);
            byte[] bytes = reader.Read(offset, chunk);
            if (bytes.Length == 0) break;
            destination.Write(bytes, 0, bytes.Length);
            offset += bytes.Length;
            remaining -= bytes.Length;
        }
    }

    private static void WriteZeros(Stream destination, long count)
    {
        var zeros = new byte[Math.Min(count, CopyBuffer)];
        long remaining = count;
        while (remaining > 0)
        {
            int n = (int)Math.Min(zeros.Length, remaining);
            destination.Write(zeros, 0, n);
            remaining -= n;
        }
    }
}
