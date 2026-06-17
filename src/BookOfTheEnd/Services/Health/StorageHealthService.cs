using BookOfTheEnd.Models;
using BookOfTheEnd.Models.Health;

namespace BookOfTheEnd.Services.Health;

/// <summary>
/// Facade that produces a full <see cref="DeviceHealth"/> for a drive by composing the
/// SMART reader, the health analysis engine, and the recovery-readiness decision layer.
/// Surface-scan evidence is optional and, when supplied, sharpens the readiness verdict.
/// </summary>
public sealed class StorageHealthService
{
    private readonly SmartDataService _smart;
    private readonly HealthAnalysisService _analysis;
    private readonly RecoveryReadinessService _readiness;
    private readonly LoggingService _log;

    public StorageHealthService(
        SmartDataService smart,
        HealthAnalysisService analysis,
        RecoveryReadinessService readiness,
        LoggingService log)
    {
        _smart = smart;
        _analysis = analysis;
        _readiness = readiness;
        _log = log;
    }

    public Task<DeviceHealth> EvaluateAsync(DriveModel drive, SurfaceScanResult? surface = null)
        => Task.Run(() => Evaluate(drive, surface));

    public DeviceHealth Evaluate(DriveModel drive, SurfaceScanResult? surface = null)
    {
        var smart = _smart.Read(drive.Letter);
        var analysis = _analysis.Analyze(smart.Readings, smart.IsSsd);
        var readiness = _readiness.Evaluate(analysis.Status, smart.Readings, surface);

        bool isSsd = smart.IsSsd || drive.MediaType == DriveMediaType.SolidState;
        string model = !string.IsNullOrWhiteSpace(smart.Model) ? smart.Model
            : !string.IsNullOrWhiteSpace(drive.Model) ? drive.Model
            : $"{drive.Letter}:";
        string iface = !string.IsNullOrWhiteSpace(smart.Interface) ? smart.Interface : drive.BusType;
        long capacity = smart.CapacityBytes > 0 ? smart.CapacityBytes : drive.TotalSize;

        return new DeviceHealth
        {
            DriveLetter = drive.Letter,
            Model = model,
            Serial = smart.Serial,
            Interface = iface,
            CapacityBytes = capacity,
            IsSsd = isSsd,
            Smart = smart.Readings,
            HealthScore = analysis.Score,
            Status = analysis.Status,
            RiskLevel = analysis.Risk,
            ClonePriority = readiness.Priority,
            Readiness = readiness.Readiness,
            Findings = analysis.Findings
        };
    }
}
