namespace BookOfTheEnd.Models.Health;

/// <summary>
/// The decision layer that differentiates this module: whether it is safe to
/// recover from the drive now, or whether it should be cloned/imaged first.
/// </summary>
public sealed class RecoveryReadiness
{
    /// <summary>True when recovery can proceed without an elevated risk of worsening data loss.</summary>
    public bool RecoveryReady { get; init; } = true;

    /// <summary>0 (safe) to 100 (do not read this drive without cloning first).</summary>
    public int RiskScore { get; init; }

    /// <summary>Short actionable recommendation shown to the user.</summary>
    public string Recommendation { get; init; } = "Drive appears healthy. Recovery can proceed.";

    /// <summary>Why the recommendation was made.</summary>
    public string Reason { get; init; } = "";
}
