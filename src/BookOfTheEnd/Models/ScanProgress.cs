namespace BookOfTheEnd.Models;

/// <summary>
/// Immutable snapshot of scan progress, pushed to the UI through <see cref="IProgress{T}"/>.
/// </summary>
public sealed class ScanProgress
{
    public double Percent { get; init; }

    public long BytesProcessed { get; init; }

    public long TotalBytes { get; init; }

    public int FilesFound { get; init; }

    public string CurrentActivity { get; init; } = "";

    public TimeSpan Elapsed { get; init; }

    public TimeSpan? EstimatedRemaining { get; init; }

    public string EtaDisplay =>
        EstimatedRemaining is { } eta && eta >= TimeSpan.Zero
            ? FormatSpan(eta)
            : "Estimating...";

    public string ElapsedDisplay => FormatSpan(Elapsed);

    private static string FormatSpan(TimeSpan span)
    {
        if (span.TotalHours >= 1)
            return $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}";
        return $"{span.Minutes:00}:{span.Seconds:00}";
    }
}
