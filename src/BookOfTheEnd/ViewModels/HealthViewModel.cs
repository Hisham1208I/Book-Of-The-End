using System.Collections.ObjectModel;
using System.Windows.Threading;
using BookOfTheEnd.Models;
using BookOfTheEnd.Models.Health;
using BookOfTheEnd.Services;
using BookOfTheEnd.Services.Health;
using BookOfTheEnd.Services.Scanning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BookOfTheEnd.ViewModels;

/// <summary>
/// Backs the Health dashboard tab: live SMART/health readings for the selected drive
/// (auto-refreshed every 60s) plus an on-demand read-only surface scan.
/// </summary>
public sealed partial class HealthViewModel : ObservableObject
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);

    private readonly StorageHealthService _health;
    private readonly SurfaceScanService _surface;
    private readonly LoggingService _log;
    private readonly DispatcherTimer _timer;

    private DriveModel? _currentDrive;
    private SurfaceScanResult? _lastSurface;
    private ScanController? _surfaceController;

    public HealthViewModel(StorageHealthService health, SurfaceScanService surface, LoggingService log)
    {
        _health = health;
        _surface = surface;
        _log = log;

        _timer = new DispatcherTimer { Interval = RefreshInterval };
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    public ObservableCollection<SurfaceBlock> SurfaceBlocks { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHealth))]
    [NotifyPropertyChangedFor(nameof(HasSmartData))]
    private DeviceHealth? _currentHealth;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusNote = "Select a drive to view its health.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunSurfaceScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelSurfaceScanCommand))]
    private bool _isSurfaceScanning;

    [ObservableProperty] private double _surfaceProgress;
    [ObservableProperty] private string _surfaceActivity = "Surface scan has not been run yet.";
    [ObservableProperty] private string _surfaceSummary = "";
    [ObservableProperty] private bool _hasSurfaceResult;

    public bool HasHealth => CurrentHealth is not null;
    public bool HasSmartData => CurrentHealth?.DataAvailable == true;

    /// <summary>Switches the dashboard to a new drive and refreshes immediately.</summary>
    public void SetDrive(DriveModel? drive)
    {
        if (ReferenceEquals(_currentDrive, drive)) return;
        _currentDrive = drive;
        _lastSurface = null;
        SurfaceBlocks.Clear();
        SurfaceResultReset();
        _timer.Stop();
        if (drive is not null)
        {
            _ = RefreshAsync();
            _timer.Start();
        }
        else
        {
            CurrentHealth = null;
            StatusNote = "Select a drive to view its health.";
        }
        RunSurfaceScanCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task Refresh() => await RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_currentDrive is not { } drive)
        {
            CurrentHealth = null;
            return;
        }

        try
        {
            IsLoading = true;
            var health = await _health.EvaluateAsync(drive, _lastSurface);
            CurrentHealth = health;
            StatusNote = health.DataAvailable
                ? $"Updated {DateTime.Now:HH:mm:ss}."
                : health.Smart.Note ?? "No SMART data available for this device.";
        }
        catch (Exception ex)
        {
            _log.Warn($"Health refresh failed for {drive.Letter}: {ex.Message}");
            StatusNote = $"Health check failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanRunSurfaceScan() => !IsSurfaceScanning && _currentDrive is not null;

    [RelayCommand(CanExecute = nameof(CanRunSurfaceScan))]
    private async Task RunSurfaceScan()
    {
        if (_currentDrive is not { } drive) return;

        _surfaceController = new ScanController();
        IsSurfaceScanning = true;
        SurfaceProgress = 0;
        HasSurfaceResult = false;
        SurfaceBlocks.Clear();

        var progress = new Progress<ScanProgress>(p =>
        {
            SurfaceProgress = p.Percent;
            if (!string.IsNullOrEmpty(p.CurrentActivity)) SurfaceActivity = p.CurrentActivity;
        });

        try
        {
            var result = await _surface.ScanAsync(drive, _surfaceController, progress);
            _lastSurface = result;
            foreach (var block in result.Blocks)
                SurfaceBlocks.Add(block);

            if (!result.Completed && result.Note is not null)
            {
                SurfaceActivity = result.Note;
                HasSurfaceResult = false;
            }
            else
            {
                SurfaceSummary = $"{result.BadBlocks} bad · {result.SlowBlocks} slow · {result.HealthyBlocks} healthy blocks";
                HasSurfaceResult = SurfaceBlocks.Count > 0;
            }

            // Fold the surface evidence into the readiness verdict.
            await RefreshAsync();
        }
        catch (OperationCanceledException)
        {
            SurfaceActivity = "Surface scan cancelled.";
        }
        catch (Exception ex)
        {
            _log.Warn($"Surface scan error on {drive.Letter}: {ex.Message}");
            SurfaceActivity = $"Surface scan failed: {ex.Message}";
        }
        finally
        {
            IsSurfaceScanning = false;
            _surfaceController?.Dispose();
            _surfaceController = null;
        }
    }

    private bool CanCancelSurfaceScan() => IsSurfaceScanning;

    [RelayCommand(CanExecute = nameof(CanCancelSurfaceScan))]
    private void CancelSurfaceScan() => _surfaceController?.Cancel();

    private void SurfaceResultReset()
    {
        SurfaceProgress = 0;
        SurfaceActivity = "Surface scan has not been run yet.";
        SurfaceSummary = "";
        HasSurfaceResult = false;
    }
}
