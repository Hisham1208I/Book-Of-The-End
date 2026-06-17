using System.IO;
using System.Text;
using BookOfTheEnd.Interop;
using BookOfTheEnd.Models;

namespace BookOfTheEnd.Cli;

/// <summary>
/// Discovers candidate volumes to scan. On Linux it reads <c>/proc/partitions</c> and
/// probes each device's boot sector to identify NTFS/FAT32; on Windows it falls back to
/// mounted logical drives so the same tool can be exercised there.
/// </summary>
public static class DeviceScanner
{
    public static IReadOnlyList<DriveModel> Enumerate()
    {
        return OperatingSystem.IsWindows() ? EnumerateWindows() : EnumerateLinux();
    }

    private static List<DriveModel> EnumerateLinux()
    {
        var drives = new List<DriveModel>();
        const string partitions = "/proc/partitions";
        if (!File.Exists(partitions)) return drives;

        foreach (string raw in File.ReadLines(partitions).Skip(2))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) continue;
            if (!long.TryParse(parts[2], out long blocks)) continue;

            string name = parts[3];
            // Skip loop/ram devices and tiny partitions (< 8 MB).
            if (name.StartsWith("loop") || name.StartsWith("ram")) continue;
            long size = blocks * 1024L;
            if (size < 8L * 1024 * 1024) continue;

            string devicePath = $"/dev/{name}";
            string fs = ProbeFileSystem(devicePath, size);

            drives.Add(new DriveModel
            {
                Letter = devicePath,
                RootPath = devicePath,
                VolumeLabel = name,
                FileSystem = fs,
                TotalSize = size,
                FreeSpace = 0,
                IsReady = true,
                Connection = DriveConnection.Internal,
                MediaType = DriveMediaType.Unknown
            });
        }

        return drives;
    }

    private static List<DriveModel> EnumerateWindows()
    {
        var drives = new List<DriveModel>();
        foreach (var d in DriveInfo.GetDrives())
        {
            if (!d.IsReady) continue;
            if (d.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
            string letter = d.Name.TrimEnd('\\', ':');
            drives.Add(new DriveModel
            {
                Letter = letter,
                RootPath = d.RootDirectory.FullName,
                VolumeLabel = SafeLabel(d),
                FileSystem = SafeFormat(d),
                TotalSize = SafeSize(d),
                FreeSpace = SafeFree(d),
                IsReady = true,
                Connection = d.DriveType == DriveType.Removable ? DriveConnection.External : DriveConnection.Internal,
                MediaType = d.DriveType == DriveType.Removable ? DriveMediaType.Removable : DriveMediaType.Unknown
            });
        }
        return drives;
    }

    /// <summary>Reads the boot sector and returns "NTFS", "FAT32", or "RAW/Other".</summary>
    private static string ProbeFileSystem(string devicePath, long size)
    {
        try
        {
            using var reader = RawVolumeReader.Open(devicePath, size);
            byte[] boot = reader.ReadAligned(0, 512);
            if (boot.Length < 512) return "Unknown";

            if (Matches(boot, 3, "NTFS    ")) return "NTFS";
            if (Matches(boot, 82, "FAT32   ")) return "FAT32";
            if (Matches(boot, 54, "FAT")) return "FAT";
            return "RAW/Other";
        }
        catch (UnauthorizedAccessException)
        {
            return "(need root)";
        }
        catch
        {
            return "Unknown";
        }
    }

    private static bool Matches(byte[] data, int offset, string ascii)
    {
        byte[] pattern = Encoding.ASCII.GetBytes(ascii);
        if (offset + pattern.Length > data.Length) return false;
        for (int i = 0; i < pattern.Length; i++)
            if (data[offset + i] != pattern[i]) return false;
        return true;
    }

    private static string SafeLabel(DriveInfo d) { try { return d.VolumeLabel; } catch { return ""; } }
    private static string SafeFormat(DriveInfo d) { try { return d.DriveFormat; } catch { return ""; } }
    private static long SafeSize(DriveInfo d) { try { return d.TotalSize; } catch { return 0; } }
    private static long SafeFree(DriveInfo d) { try { return d.TotalFreeSpace; } catch { return 0; } }
}
