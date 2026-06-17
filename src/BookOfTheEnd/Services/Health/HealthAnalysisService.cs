using BookOfTheEnd.Models.Health;

namespace BookOfTheEnd.Services.Health;

/// <summary>
/// Converts a raw SMART snapshot into an overall health score, status, and risk level
/// using fixed thresholds. Pure and deterministic (no I/O), so it is easy to reason about.
/// </summary>
public sealed class HealthAnalysisService
{
    public sealed record AnalysisResult(
        int Score,
        HealthStatus Status,
        RiskLevel Risk,
        IReadOnlyList<string> Findings);

    public AnalysisResult Analyze(SmartReadings smart, bool isSsd)
    {
        var findings = new List<string>();

        if (!smart.HasData)
        {
            return new AnalysisResult(0, HealthStatus.Unknown, RiskLevel.Low,
                new[] { smart.Note ?? "No SMART data available for this device." });
        }

        int score = 100;
        var floor = HealthStatus.Excellent; // hard lower bound forced by critical signals

        if (smart.PredictFailure)
        {
            score -= 80;
            floor = Worse(floor, HealthStatus.Failing);
            findings.Add("Firmware predicts imminent drive failure (SMART fail flag).");
        }

        if (string.Equals(smart.WmiHealthStatus, "Unhealthy", StringComparison.OrdinalIgnoreCase))
        {
            score -= 60;
            floor = Worse(floor, HealthStatus.Critical);
            findings.Add("Windows reports this disk as Unhealthy.");
        }
        else if (string.Equals(smart.WmiHealthStatus, "Warning", StringComparison.OrdinalIgnoreCase))
        {
            score -= 20;
            floor = Worse(floor, HealthStatus.Warning);
            findings.Add("Windows reports a health Warning for this disk.");
        }

        if (smart.PendingSectors is { } pending && pending > 0)
        {
            score -= (int)Math.Min(40, 20 + pending);
            floor = Worse(floor, HealthStatus.Warning);
            findings.Add($"{pending:N0} pending (unreadable) sector(s) detected.");
        }

        if (smart.ReallocatedSectors is { } realloc && realloc > 0)
        {
            score -= realloc > 50 ? 40 : 15;
            floor = Worse(floor, realloc > 50 ? HealthStatus.Critical : HealthStatus.Warning);
            findings.Add($"{realloc:N0} reallocated sector(s) - the drive is remapping bad blocks.");
        }

        if (smart.TemperatureC is { } temp)
        {
            if (temp > 70)
            {
                score -= 35;
                floor = Worse(floor, HealthStatus.Critical);
                findings.Add($"Temperature is critical ({temp} C).");
            }
            else if (temp > 60)
            {
                score -= 15;
                floor = Worse(floor, HealthStatus.Warning);
                findings.Add($"Temperature is high ({temp} C).");
            }
        }

        if (smart.RemainingLifePercent is { } life)
        {
            if (life < 10)
            {
                score -= 40;
                floor = Worse(floor, HealthStatus.Critical);
                findings.Add($"SSD has only {life}% of rated life remaining.");
            }
            else if (life < 20)
            {
                score -= 20;
                floor = Worse(floor, HealthStatus.Warning);
                findings.Add($"SSD life is getting low ({life}% remaining).");
            }
        }

        if (smart.ReadErrorsUncorrected is { } ue && ue > 0)
        {
            score -= 15;
            floor = Worse(floor, HealthStatus.Warning);
            findings.Add($"{ue:N0} uncorrectable read error(s) recorded.");
        }

        score = Math.Clamp(score, 0, 100);
        var status = Worse(FromScore(score), floor);

        if (findings.Count == 0)
            findings.Add("All monitored SMART attributes are within healthy ranges.");

        return new AnalysisResult(score, status, ToRisk(status), findings);
    }

    private static HealthStatus FromScore(int score) => score switch
    {
        >= 90 => HealthStatus.Excellent,
        >= 75 => HealthStatus.Good,
        >= 50 => HealthStatus.Warning,
        >= 25 => HealthStatus.Critical,
        _ => HealthStatus.Failing
    };

    private static RiskLevel ToRisk(HealthStatus status) => status switch
    {
        HealthStatus.Failing => RiskLevel.Emergency,
        HealthStatus.Critical => RiskLevel.High,
        HealthStatus.Warning => RiskLevel.Moderate,
        _ => RiskLevel.Low
    };

    /// <summary>Returns the more severe of two statuses.</summary>
    private static HealthStatus Worse(HealthStatus a, HealthStatus b)
    {
        int Rank(HealthStatus s) => s switch
        {
            HealthStatus.Excellent => 0,
            HealthStatus.Good => 1,
            HealthStatus.Warning => 2,
            HealthStatus.Critical => 3,
            HealthStatus.Failing => 4,
            _ => -1
        };
        return Rank(a) >= Rank(b) ? a : b;
    }
}
