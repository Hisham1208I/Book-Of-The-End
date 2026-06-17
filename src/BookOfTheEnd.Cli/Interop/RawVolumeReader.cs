using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BookOfTheEnd.Interop;

/// <summary>
/// Cross-platform raw volume reader. On Linux it opens the block device path
/// (e.g. <c>/dev/sda1</c>) directly with a <see cref="FileStream"/>; on Windows it
/// opens <c>\\.\C:</c> via CreateFileW. The public surface matches the WPF app's
/// reader so the shared scan engines work unchanged.
/// </summary>
public sealed class RawVolumeReader : IDisposable
{
    private readonly FileStream _stream;
    private readonly SafeFileHandle? _handle;

    public int SectorSize { get; private set; } = 512;

    /// <summary>Total addressable length of the volume in bytes, if known.</summary>
    public long Length { get; }

    private RawVolumeReader(FileStream stream, SafeFileHandle? handle, long length)
    {
        _stream = stream;
        _handle = handle;
        Length = length;
    }

    /// <summary>
    /// Opens a volume for raw reading. <paramref name="device"/> is a drive letter
    /// ("C") or device path ("/dev/sda1", "\\.\C:"). Throws
    /// <see cref="UnauthorizedAccessException"/> when not running as root/Administrator.
    /// </summary>
    public static RawVolumeReader Open(string device, long volumeLength = 0)
    {
        return OperatingSystem.IsWindows()
            ? OpenWindows(device, volumeLength)
            : OpenUnix(device, volumeLength);
    }

    private static RawVolumeReader OpenUnix(string device, long volumeLength)
    {
        string path = device.StartsWith('/') ? device : $"/dev/{device}";
        try
        {
            // bufferSize 1 prevents FileStream from issuing unaligned look-ahead reads.
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1);
            long length = volumeLength;
            if (length <= 0)
            {
                try { length = stream.Seek(0, SeekOrigin.End); stream.Seek(0, SeekOrigin.Begin); }
                catch { length = 0; }
            }
            return new RawVolumeReader(stream, null, length);
        }
        catch (UnauthorizedAccessException)
        {
            throw new UnauthorizedAccessException(
                $"Access denied opening {path}. Run Book of the End with sudo (root).");
        }
        catch (FileNotFoundException)
        {
            throw new FileNotFoundException($"Device {path} was not found.");
        }
    }

    private static RawVolumeReader OpenWindows(string device, long volumeLength)
    {
        string path = device.Contains('\\') || device.Contains(':')
            ? device
            : $"\\\\.\\{device}:";

        SafeFileHandle handle = CreateFileW(
            path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_SEQUENTIAL_SCAN, IntPtr.Zero);

        if (handle.IsInvalid)
        {
            int err = Marshal.GetLastWin32Error();
            handle.Dispose();
            if (err == ERROR_ACCESS_DENIED)
                throw new UnauthorizedAccessException(
                    $"Access denied opening {path}. Run as Administrator.");
            throw new Win32Exception(err, $"Failed to open {path} (error {err}).");
        }

        var stream = new FileStream(handle, FileAccess.Read, bufferSize: 1, isAsync: false);
        return new RawVolumeReader(stream, handle, volumeLength);
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

    /// <summary>Reads <paramref name="count"/> bytes at an arbitrary offset, handling alignment.</summary>
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
        _handle?.Dispose();
    }

    // --- Windows P/Invoke (only used on Windows) ---
    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_SEQUENTIAL_SCAN = 0x08000000;
    private const int ERROR_ACCESS_DENIED = 5;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);
}
