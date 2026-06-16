using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BookOfTheEnd.Interop;

/// <summary>
/// Reads raw bytes from a mounted volume (e.g. \\.\C:). All physical reads are
/// sector aligned; callers may request arbitrary offsets/lengths and the reader
/// transparently aligns and slices.
/// </summary>
public sealed class RawVolumeReader : IDisposable
{
    private readonly SafeFileHandle _handle;
    private readonly FileStream _stream;

    public int SectorSize { get; private set; } = 512;

    /// <summary>Total addressable length of the volume in bytes, if known.</summary>
    public long Length { get; }

    private RawVolumeReader(SafeFileHandle handle, FileStream stream, long length)
    {
        _handle = handle;
        _stream = stream;
        Length = length;
    }

    /// <summary>
    /// Opens the volume for the given drive letter (without colon), e.g. "C".
    /// Throws <see cref="UnauthorizedAccessException"/> when not elevated.
    /// </summary>
    public static RawVolumeReader Open(string driveLetter, long volumeLength = 0)
    {
        string path = $"\\\\.\\{driveLetter}:";
        SafeFileHandle handle = NativeMethods.CreateFileW(
            path,
            NativeMethods.GENERIC_READ,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            NativeMethods.FILE_FLAG_SEQUENTIAL_SCAN,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            int err = Marshal.GetLastWin32Error();
            handle.Dispose();
            if (err == NativeMethods.ERROR_ACCESS_DENIED)
                throw new UnauthorizedAccessException(
                    $"Access denied opening {path}. Run Book of the End as Administrator.");
            throw new Win32Exception(err, $"Failed to open {path} (error {err}).");
        }

        // bufferSize 1 keeps FileStream from issuing its own unaligned look-ahead reads.
        var stream = new FileStream(handle, FileAccess.Read, bufferSize: 1, isAsync: false);
        return new RawVolumeReader(handle, stream, volumeLength);
    }

    /// <summary>Reads exactly <paramref name="count"/> sector-aligned bytes at a sector-aligned offset.</summary>
    public byte[] ReadAligned(long offset, int count)
    {
        var buffer = new byte[count];
        _stream.Seek(offset, SeekOrigin.Begin);
        int read = 0;
        while (read < count)
        {
            int n = _stream.Read(buffer, read, count - read);
            if (n <= 0) break;
            read += n;
        }
        if (read < count) Array.Resize(ref buffer, read);
        return buffer;
    }

    /// <summary>
    /// Reads <paramref name="count"/> bytes starting at an arbitrary <paramref name="offset"/>,
    /// handling sector alignment internally.
    /// </summary>
    public byte[] Read(long offset, int count)
    {
        long alignedStart = offset - (offset % SectorSize);
        long startPadding = offset - alignedStart;
        long end = offset + count;
        long alignedEnd = end % SectorSize == 0 ? end : end + (SectorSize - end % SectorSize);
        int alignedCount = (int)(alignedEnd - alignedStart);

        byte[] raw = ReadAligned(alignedStart, alignedCount);
        int available = (int)Math.Max(0, raw.Length - startPadding);
        int resultLen = Math.Min(count, available);
        var result = new byte[resultLen];
        if (resultLen > 0)
            Array.Copy(raw, startPadding, result, 0, resultLen);
        return result;
    }

    /// <summary>Reads a large span into a caller-supplied buffer; returns bytes read.</summary>
    public int ReadInto(long alignedOffset, byte[] buffer, int count)
    {
        _stream.Seek(alignedOffset, SeekOrigin.Begin);
        int read = 0;
        while (read < count)
        {
            int n = _stream.Read(buffer, read, count - read);
            if (n <= 0) break;
            read += n;
        }
        return read;
    }

    internal void SetSectorSize(int sectorSize)
    {
        if (sectorSize is 512 or 1024 or 2048 or 4096)
            SectorSize = sectorSize;
    }

    public void Dispose()
    {
        _stream.Dispose();
        _handle.Dispose();
    }
}
