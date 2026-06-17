using System.Management;
using BookOfTheEnd.Models.Health;

namespace BookOfTheEnd.Services.Health;

/// <summary>
/// Collects SMART / reliability data for the physical disk backing a drive letter.
///
/// Sources (best-effort, in priority order):
///  1. Win32_LogicalDisk -> Partition -> Win32_DiskDrive  (maps letter to a physical disk)
///  2. MSFT_PhysicalDisk + MSFT_StorageReliabilityCounter (root\microsoft\windows\storage)
///     -> temperature, wear, power-on hours, read errors, cycles, health status (incl. NVMe)
///  3. MSStorageDriver_FailurePredictStatus / ...Data (root\wmi)
///     -> SMART predict-fail flag and ATA attributes (reallocated/pending sectors)
///
/// Everything degrades gracefully: missing fields stay null rather than throwing.
/// </summary>
public sealed class SmartDataService
{
    private readonly LoggingService _log;

    public SmartDataService(LoggingService log) => _log = log;

    public sealed record SmartResult(
        string Model,
        string Serial,
        string Interface,
        long CapacityBytes,
        bool IsSsd,
        SmartReadings Readings);

    /// <summary>Reads health data for the disk that hosts <paramref name="driveLetter"/> (e.g. "C").</summary>
    public SmartResult Read(string driveLetter)
    {
        try
        {
            var disk = ResolveDiskDrive(driveLetter);
            if (disk is null)
            {
                return new SmartResult("", "", "", 0, false,
                    new SmartReadings { HasData = false, Note = "Could not map the volume to a physical disk." });
            }

            uint index = GetUInt(disk.Mo, "Index");
            string pnp = GetString(disk.Mo, "PNPDeviceID");
            string model = disk.Model;
            long capacity = disk.SizeBytes;
            string iface = disk.InterfaceType;

            string serial = "";
            string? healthStatus = null;
            bool isSsd = false;
            int? temperature = null, temperatureMax = null, wear = null;
            long? powerOnHours = null, readErrorsTotal = null, readErrorsUncorrected = null, powerCycles = null;

            using (var physical = QueryPhysicalDisk(index))
            {
                if (physical is not null)
                {
                    serial = GetString(physical, "SerialNumber").Trim();
                    healthStatus = HealthStatusName(GetInt(physical, "HealthStatus"));
                    int mediaType = GetInt(physical, "MediaType") ?? 0;
                    int busType = GetInt(physical, "BusType") ?? 0;
                    if (string.IsNullOrWhiteSpace(iface)) iface = BusTypeName(busType);
                    isSsd = mediaType == 4 || busType == 17; // SSD or NVMe
                    if (capacity <= 0) capacity = GetLong(physical, "Size") ?? 0;
                    if (string.IsNullOrWhiteSpace(model)) model = GetString(physical, "FriendlyName");

                    var rc = QueryReliabilityCounter(physical);
                    if (rc is not null)
                    {
                        using (rc)
                        {
                            temperature = NormalizeTemp(GetInt(rc, "Temperature"));
                            temperatureMax = NormalizeTemp(GetInt(rc, "TemperatureMax"));
                            wear = ClampWear(GetInt(rc, "Wear"));
                            powerOnHours = GetLong(rc, "PowerOnHours");
                            readErrorsTotal = GetLong(rc, "ReadErrorsTotal");
                            readErrorsUncorrected = GetLong(rc, "ReadErrorsUncorrected");
                            powerCycles = GetLong(rc, "StartStopCycleCount");
                        }
                    }
                }
            }

            var (predictFailure, reallocated, pending, ataTemp) = ReadFailurePredict(pnp);
            temperature ??= ataTemp;

            bool predicted = predictFailure
                             || string.Equals(healthStatus, "Unhealthy", StringComparison.OrdinalIgnoreCase);

            bool anyData = temperature is not null || wear is not null || powerOnHours is not null
                           || reallocated is not null || pending is not null || healthStatus is not null;

            var readings = new SmartReadings
            {
                TemperatureC = temperature,
                TemperatureMaxC = temperatureMax,
                PowerOnHours = powerOnHours,
                PowerCycles = powerCycles,
                ReallocatedSectors = reallocated,
                PendingSectors = pending,
                WearPercentUsed = wear,
                ReadErrorsTotal = readErrorsTotal,
                ReadErrorsUncorrected = readErrorsUncorrected,
                PredictFailure = predicted,
                WmiHealthStatus = healthStatus,
                HasData = anyData,
                Note = anyData ? null : "No SMART data exposed for this device (common on USB bridges / VMs)."
            };

            return new SmartResult(model, serial, iface, capacity, isSsd, readings);
        }
        catch (ManagementException ex)
        {
            _log.Warn($"SMART query failed for {driveLetter}: {ex.Message}");
            return new SmartResult("", "", "", 0, false,
                new SmartReadings { HasData = false, Note = "SMART query failed. Run as Administrator." });
        }
        catch (Exception ex)
        {
            _log.Warn($"SMART read error for {driveLetter}: {ex.Message}");
            return new SmartResult("", "", "", 0, false,
                new SmartReadings { HasData = false, Note = "SMART data unavailable." });
        }
    }

    private sealed record DiskDrive(ManagementObject Mo, string Model, string InterfaceType, long SizeBytes);

    /// <summary>Walks logical disk -> partition -> physical disk to find the backing drive.</summary>
    private DiskDrive? ResolveDiskDrive(string driveLetter)
    {
        string id = driveLetter.TrimEnd(':') + ":";
        try
        {
            using var partitions = new ManagementObjectSearcher(
                $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{id}'}} WHERE AssocClass=Win32_LogicalDiskToPartition");
            foreach (ManagementObject partition in partitions.Get())
            {
                using (partition)
                {
                    string partId = GetString(partition, "DeviceID");
                    using var disks = new ManagementObjectSearcher(
                        $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partId}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition");
                    foreach (ManagementObject disk in disks.Get())
                    {
                        return new DiskDrive(
                            disk,
                            GetString(disk, "Model"),
                            GetString(disk, "InterfaceType"),
                            GetLong(disk, "Size") ?? 0);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"Disk mapping failed for {driveLetter}: {ex.Message}");
        }
        return null;
    }

    private ManagementObject? QueryPhysicalDisk(uint deviceId)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"\\.\root\microsoft\windows\storage",
                $"SELECT * FROM MSFT_PhysicalDisk WHERE DeviceId = '{deviceId}'");
            foreach (ManagementObject mo in searcher.Get())
                return mo;
        }
        catch (Exception ex)
        {
            _log.Warn($"MSFT_PhysicalDisk query failed: {ex.Message}");
        }
        return null;
    }

    private ManagementObject? QueryReliabilityCounter(ManagementObject physicalDisk)
    {
        try
        {
            foreach (ManagementObject rc in physicalDisk.GetRelated("MSFT_StorageReliabilityCounter"))
                return rc;
        }
        catch (Exception ex)
        {
            _log.Warn($"MSFT_StorageReliabilityCounter query failed: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// Reads the ATA SMART predict-fail flag plus reallocated/pending sectors, matching
    /// the WMI instance to this disk by PnP id. NVMe drives typically expose none of this.
    /// </summary>
    private (bool predictFailure, long? reallocated, long? pending, int? temp) ReadFailurePredict(string pnpDeviceId)
    {
        bool predict = false;
        long? reallocated = null, pending = null;
        int? temp = null;

        if (string.IsNullOrWhiteSpace(pnpDeviceId)) return (false, null, null, null);
        string target = Normalize(pnpDeviceId);

        try
        {
            using var status = new ManagementObjectSearcher(
                @"\\.\root\wmi", "SELECT InstanceName, PredictFailure FROM MSStorageDriver_FailurePredictStatus");
            foreach (ManagementObject mo in status.Get())
            {
                using (mo)
                {
                    if (!InstanceMatches(GetString(mo, "InstanceName"), target)) continue;
                    predict = Convert.ToBoolean(mo["PredictFailure"] ?? false);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"FailurePredictStatus query failed: {ex.Message}");
        }

        try
        {
            using var data = new ManagementObjectSearcher(
                @"\\.\root\wmi", "SELECT InstanceName, VendorSpecific FROM MSStorageDriver_FailurePredictData");
            foreach (ManagementObject mo in data.Get())
            {
                using (mo)
                {
                    if (!InstanceMatches(GetString(mo, "InstanceName"), target)) continue;
                    if (mo["VendorSpecific"] is byte[] vendor)
                        (reallocated, pending, temp) = ParseAtaAttributes(vendor);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"FailurePredictData query failed: {ex.Message}");
        }

        return (predict, reallocated, pending, temp);
    }

    /// <summary>
    /// Parses the ATA SMART attribute table (VendorSpecific): 2-byte header followed by
    /// up to 30 entries of 12 bytes [id, flags(2), value, worst, raw(6), reserved].
    /// </summary>
    private static (long? reallocated, long? pending, int? temp) ParseAtaAttributes(byte[] data)
    {
        long? reallocated = null, pending = null;
        int? temp = null;
        if (data.Length < 2 + 12) return (null, null, null);

        for (int offset = 2; offset + 12 <= data.Length; offset += 12)
        {
            byte id = data[offset];
            if (id == 0) continue;
            long raw = data[offset + 5]
                       | ((long)data[offset + 6] << 8)
                       | ((long)data[offset + 7] << 16)
                       | ((long)data[offset + 8] << 24)
                       | ((long)data[offset + 9] << 32)
                       | ((long)data[offset + 10] << 40);
            switch (id)
            {
                case 0x05: reallocated = raw & 0xFFFFFFFF; break;
                case 0xC5: pending = raw & 0xFFFFFFFF; break;
                case 0xC2: temp = (int)(raw & 0xFF); break;
            }
        }
        return (reallocated, pending, temp);
    }

    private static bool InstanceMatches(string instanceName, string normalizedPnp)
    {
        if (string.IsNullOrWhiteSpace(instanceName)) return false;
        string inst = Normalize(instanceName);
        if (inst.EndsWith("_0")) inst = inst[..^2];
        return inst == normalizedPnp || inst.StartsWith(normalizedPnp) || normalizedPnp.StartsWith(inst);
    }

    private static string Normalize(string s) => s.Trim().ToUpperInvariant();

    private static int? NormalizeTemp(int? value) => value is > 0 and < 200 ? value : null;
    private static int? ClampWear(int? value) => value is >= 0 and <= 100 ? value : null;

    private static string HealthStatusName(int? code) => code switch
    {
        0 => "Healthy",
        1 => "Warning",
        2 => "Unhealthy",
        _ => "Unknown"
    };

    private static string BusTypeName(int busType) => busType switch
    {
        3 => "ATA",
        7 => "USB",
        8 => "RAID",
        10 => "SAS",
        11 => "SATA",
        12 => "SD",
        13 => "MMC",
        17 => "NVMe",
        _ => ""
    };

    private static string GetString(ManagementBaseObject mo, string name)
    {
        try { return Convert.ToString(mo[name]) ?? ""; } catch { return ""; }
    }

    private static int? GetInt(ManagementBaseObject mo, string name)
    {
        try { var v = mo[name]; return v is null ? null : Convert.ToInt32(v); } catch { return null; }
    }

    private static long? GetLong(ManagementBaseObject mo, string name)
    {
        try { var v = mo[name]; return v is null ? null : Convert.ToInt64(v); } catch { return null; }
    }

    private static uint GetUInt(ManagementBaseObject mo, string name)
    {
        try { var v = mo[name]; return v is null ? 0u : Convert.ToUInt32(v); } catch { return 0u; }
    }
}
