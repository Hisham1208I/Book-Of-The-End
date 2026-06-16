namespace BookOfTheEnd.Services.Scanning;

/// <summary>
/// Coordinates cooperative pause/resume/cancel for a running scan. Engines call
/// <see cref="WaitIfPaused"/> at safe checkpoints.
/// </summary>
public sealed class ScanController : IDisposable
{
    private readonly ManualResetEventSlim _gate = new(true);
    private readonly CancellationTokenSource _cts = new();

    public CancellationToken Token => _cts.Token;

    public bool IsPaused => !_gate.IsSet;

    public void Pause() => _gate.Reset();

    public void Resume() => _gate.Set();

    public void Cancel()
    {
        _gate.Set(); // release any paused wait so it can observe cancellation
        _cts.Cancel();
    }

    /// <summary>Blocks while paused; throws if cancelled.</summary>
    public void WaitIfPaused()
    {
        if (!_gate.IsSet)
            _gate.Wait(_cts.Token);
        _cts.Token.ThrowIfCancellationRequested();
    }

    public void Dispose()
    {
        _gate.Dispose();
        _cts.Dispose();
    }
}
