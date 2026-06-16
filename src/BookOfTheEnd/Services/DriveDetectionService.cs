using System.IO;
using System.Management;
using BookOfTheEnd.Models;

namespace BookOfTheEnd.Services;

/// <summary>
/// Enumerates mounted volumes and enriches them with physical-disk details
/// (bus type, media type, internal/external) resolved via WMI.
/// </summary>
public sealed class DriveDetectionService
{
    private readonly LoggingService _log;

    public DriveDetectionService(LoggingService log) => _log = log;

    public IReadOnlyList<DriveModel> GetDrives()
    {
        var diskInfo = TryBuildPhysicalDiskMap();
        var result = new List<DriveModel>();

        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            try
            {
                string letter = drive.Name.TrimEnd('\\', ':', ' ');
                if (string.IsNullOrEmpty(letter)) continue;

                bool ready = drive.IsReady;
                var (media, connection, model, bus) = ResolveDiskTraits(letter, drive.DriveType, diskInfo);

                result.Add(new DriveModel
                {
                    Letter = letter,
                    RootPath = drive.Name,
                    VolumeLabel = ready ? SafeLabel(drive) : "",
                    FileSystem = ready ? SafeFormat(drive) : "",
                    TotalSize = ready ? drive.TotalSize : 0,
                    FreeSpace = ready ? drive.TotalFreeSpace : 0,
                    Connection = connection,
                    MediaType = media,
                    Model = model,
                    BusType = bus,
                    IsReady = ready
                });
            }
            catch (Exception ex)
            {
                _log.Warn($"Failed to read drive '{drive.Name}': {ex.Message}");
            }
        }

        return result;
    }

    private static string SafeLabel(DriveInfo drive)
    {
        try { return drive.VolumeLabel ?? ""; } catch { return ""; }
    }

    private static string SafeFormat(DriveInfo drive)
    {
        try { return drive.DriveFormat ?? ""; } catch { return ""; }
    }

    private (DriveMediaType, DriveConnection, string, string) ResolveDiskTraits(
        string letter, DriveType driveType, IReadOnlyDictionary<string, PhysicalDiskInfo> diskInfo)
    {
        if (driveType == DriveType.Removable)
            return (DriveMediaType.Removable, DriveConnection.External, "", "Removable");

        if (driveType == DriveType.Network)
            return (DriveMediaType.Unknown, DriveConnection.External, "", "Network");

        if (diskInfo.TryGetValue(letter, out var info))
        {
            bool external = info.BusType is "USB" or "1394" or "SD" or "MMC"
                            || driveType == DriveType.Removable;
            return (info.MediaType, external ? DriveConnection.External : DriveConnection.Internal,
                info.Model, info.BusType);
        }

        // Fixed disk we couldn't map -> assume internal.
        return (DriveMediaType.Unknown,
            driveType == DriveType.Fixed ? DriveConnection.Internal : DriveConnection.Unknown,
            "", "");
    }

    /// <summary>
    /// Builds a map from drive letter to physical disk traits by walking the WMI
    /// association chain: Win32_LogicalDisk -> Partition -> Win32_DiskDrive,
    /// then matching against MSFT_PhysicalDisk for bus/media type.
    /// </summary>
    private IReadOnlyDictionary<string, PhysicalDiskInfo> TryBuildPhysicalDiskMap()
    {
        var byLetter = new Dictionary<string, PhysicalDiskInfo>(StringComparer.OrdinalIgnoreCase);
        try
        {
            // Index physical disk traits from the Storage namespace.
            var physical = new Dictionary<uint, (DriveMediaType media, string bus, string model)>();
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    @"\\.\root\microsoft\windows\storage",
                    "SELECT DeviceId, MediaType, BusType, FriendlyName FROM MSFT_PhysicalDisk");
                foreach (ManagementBaseObject mo in searcher.Get())
                {
                    if (!uint.TryParse(Convert.ToString(mo["DeviceId"]), out uint deviceId)) continue;
                    var media = (Convert.ToInt32(mo["MediaType"] ?? 0)) switch
                    {
                        3 => DriveMediaType.HardDisk,
                        4 => DriveMediaType.SolidState,
                        _ => DriveMediaType.Unknown
                    };
                    string bus = BusTypeName(Convert.ToInt32(mo["BusType"] ?? 0));
                    string model = Convert.ToString(mo["FriendlyName"]) ?? "";
                    physical[deviceId] = (media, bus, model);
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"MSFT_PhysicalDisk query failed: {ex.Message}");
            }

            using var diskDrives = new ManagementObjectSearcher(
                "SELECT DeviceID, Index, Model, InterfaceType, MediaType FROM Win32_DiskDrive");
            foreach (ManagementObject disk in diskDrives.Get())
            {
                uint index = Convert.ToUInt32(disk["Index"] ?? 0u);
                physical.TryGetValue(index, out var phys);

                DriveMediaType media = phys.media;
                string bus = phys.bus;
                string model = !string.IsNullOrWhiteSpace(phys.model)
                    ? phys.model
                    : Convert.ToString(disk["Model"]) ?? "";
                string interfaceType = Convert.ToString(disk["InterfaceType"]) ?? "";
                if (string.IsNullOrEmpty(bus)) bus = interfaceType;
                if (media == DriveMediaType.Unknown &&
                    (Convert.ToString(disk["MediaType"]) ?? "").Contains("Removable", StringComparison.OrdinalIgnoreCase))
                    media = DriveMediaType.Removable;

                foreach (ManagementObject partition in disk.GetRelated("Win32_DiskPartition"))
                {
                    foreach (ManagementObject logical in partition.GetRelated("Win32_LogicalDisk"))
                    {
                        string? id = Convert.ToString(logical["DeviceID"]); // e.g. "C:"
                        if (string.IsNullOrEmpty(id)) continue;
                        string letter = id.TrimEnd(':');
                        byLetter[letter] = new PhysicalDiskInfo(media, bus, model);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"WMI disk enumeration failed: {ex.Message}");
        }

        return byLetter;
    }

    private static string BusTypeName(int busType) => busType switch
    {
        1 => "SCSI",
        2 => "ATAPI",
        3 => "ATA",
        4 => "1394",
        5 => "SSA",
        6 => "Fibre",
        7 => "USB",
        8 => "RAID",
        9 => "iSCSI",
        10 => "SAS",
        11 => "SATA",
        12 => "SD",
        13 => "MMC",
        17 => "NVMe",
        _ => ""
    };

    private readonly record struct PhysicalDiskInfo(DriveMediaType MediaType, string BusType, string Model);
}
