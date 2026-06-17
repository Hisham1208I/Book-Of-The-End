using BookOfTheEnd.Models.Health;

namespace BookOfTheEnd.Services.Health;

/// <summary>
/// The decision layer: given health + (optional) surface-scan evidence, decides whether
/// it is safe to recover now or whether the drive should be cloned first, and how urgent
/// that clone is.
/// </summary>
public sealed class RecoveryReadinessService
{
    public sealed record ReadinessResult(RecoveryReadiness Readiness, ClonePriority Priority);

    public ReadinessResult Evaluate(HealthStatus status, SmartReadings smart, SurfaceScanResult? surface)
    {
        bool badSectors = surface is { HasBadSectors: true };
        bool pending = smart.PendingSectors is > 0;
        bool reallocated = smart.ReallocatedSectors is > 0;
        bool hotPause = smart.TemperatureC is > 70;

        ClonePriority priority = ClonePriority.Low;
        var reasons = new List<string>();

        if (status == HealthStatus.Failing || smart.PredictFailure || badSectors)
        {
            priority = ClonePriority.Emergency;
            if (smart.PredictFailure) reasons.Add("firmware predicts failure");
            if (badSectors) reasons.Add($"{surface!.BadBlocks} bad block(s) found on the surface scan");
            if (status == HealthStatus.Failing) reasons.Add("overall health is failing");
        }
        else if (status == HealthStatus.Critical || pending)
        {
            priority = ClonePriority.High;
            if (pending) reasons.Add($"{smart.PendingSectors:N0} pending sector(s)");
            if (status == HealthStatus.Critical) reasons.Add("overall health is critical");
        }
        else if (status == HealthStatus.Warning || reallocated)
        {
            priority = ClonePriority.Moderate;
            if (reallocated) reasons.Add($"{smart.ReallocatedSectors:N0} reallocated sector(s)");
            if (status == HealthStatus.Warning) reasons.Add("a health warning is active");
        }

        // Risk score: blend inverse-health weighting with concrete defect signals.
        int risk = priority switch
        {
            ClonePriority.Emergency => 90,
            ClonePriority.High => 70,
            ClonePriority.Moderate => 45,
            _ => 10
        };
        if (badSectors) risk = Math.Min(100, risk + Math.Min(10, surface!.BadBlocks));
        if (status == HealthStatus.Unknown) risk = Math.Max(risk, 25);

        bool ready = priority is ClonePriority.Low or ClonePriority.Moderate;

        string recommendation;
        string reason = reasons.Count > 0
            ? "Detected: " + string.Join(", ", reasons) + "."
            : "No defect indicators detected.";

        if (status == HealthStatus.Unknown)
        {
            recommendation = "Health unknown - proceed with caution and avoid repeated scans.";
            reason = "SMART data could not be read for this device.";
            ready = true;
        }
        else if (priority == ClonePriority.Emergency)
        {
            recommendation = "Do NOT recover directly. Clone/image the drive first, then recover from the clone.";
        }
        else if (priority == ClonePriority.High)
        {
            recommendation = "Cloning is strongly recommended before recovery to avoid further damage.";
            ready = false;
        }
        else if (priority == ClonePriority.Moderate)
        {
            recommendation = "Recovery can proceed, but consider cloning if results look incomplete.";
        }
        else
        {
            recommendation = "Drive appears healthy. Recovery can proceed.";
        }

        if (hotPause)
            recommendation += " Drive is very hot - let it cool before long operations.";

        return new ReadinessResult(
            new RecoveryReadiness
            {
                RecoveryReady = ready,
                RiskScore = risk,
                Recommendation = recommendation,
                Reason = reason
            },
            priority);
    }
}
