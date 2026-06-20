using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using BookOfTheEnd.Models;
using BookOfTheEnd.Models.Health;
using BookOfTheEnd.Services;
using BookOfTheEnd.Services.Health;
using BookOfTheEnd.Services.Scanning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace BookOfTheEnd.ViewModels;

/// <summary>
/// Backs the Health dashboard tab: live SMART/health readings for the selected drive
/// (auto-refreshed every 60s) plus surface scan and drive imaging.
/// </summary>
public sealed partial class HealthViewModel : ObservableObject
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);

    private readonly StorageHealthService _health;
    private readonly SurfaceScanService _surface;
    private readonly SmartHistoryService _smartHistory;
    private readonly DriveImageService _driveImage;
    private readonly LoggingService _log;
    private readonly DispatcherTimer _timer;

    private DriveModel? _currentDrive;
    private SurfaceScanResult? _lastSurface;
    private ScanController? _surfaceController;
    private CancellationTokenSource? _imagingCts;

    public HealthViewModel(
        StorageHealthService health,
        SurfaceScanService surface,
        SmartHistoryService smartHistory,
        DriveImageService driveImage,
        LoggingService log)
    {
        _health = health;
        _surface = surface;
        _smartHistory = smartHistory;
        _driveImage = driveImage;
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

    // --- Drive imaging ---
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImageDriveCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelImagingCommand))]
    private bool _isImaging;

    [ObservableProperty] private double _imagingProgress;
    [ObservableProperty] private string _imagingActivity = "No image created yet.";

    // --- SMART history trends ---
    [ObservableProperty] private string _reallocTrend = "";
    [ObservableProperty] private string _pendingTrend = "";
    [ObservableProperty] private string _tempTrend = "";
    [ObservableProperty] private bool _hasTrendData;

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
        ClearTrends();
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
        ImageDriveCommand.NotifyCanExecuteChanged();
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

            if (health.DataAvailable)
                SaveAndComputeTrends(health, drive);
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

    private void SaveAndComputeTrends(DeviceHealth health, DriveModel drive)
    {
        string key = string.IsNullOrEmpty(health.Serial)
            ? $"{health.Model}_{health.CapacityBytes}"
            : health.Serial;

        var prev = _smartHistory.GetHistory(key).LastOrDefault();

        _smartHistory.Append(key, new SmartHistoryEntry
        {
            Timestamp = DateTime.Now,
            DriveLetter = drive.Letter,
            TemperatureC = health.Smart.TemperatureC,
            ReallocatedSectors = health.Smart.ReallocatedSectors,
            PendingSectors = health.Smart.PendingSectors,
            WearPercentUsed = health.Smart.WearPercentUsed,
            PowerOnHours = health.Smart.PowerOnHours,
            HealthScore = health.HealthScore
        });

        if (prev is null) { ClearTrends(); return; }

        ReallocTrend = DeltaLabel(health.Smart.ReallocatedSectors, prev.ReallocatedSectors, warnOnIncrease: true);
        PendingTrend = DeltaLabel(health.Smart.PendingSectors, prev.PendingSectors, warnOnIncrease: true);
        TempTrend = DeltaLabel(health.Smart.TemperatureC, prev.TemperatureC, warnOnIncrease: false);
        HasTrendData = true;
    }

    private void ClearTrends()
    {
        ReallocTrend = "";
        PendingTrend = "";
        TempTrend = "";
        HasTrendData = false;
    }

    private static string DeltaLabel(long? current, long? previous, bool warnOnIncrease)
    {
        if (current is null || previous is null) return "";
        long delta = current.Value - previous.Value;
        if (delta == 0) return "(stable)";
        string sign = delta > 0 ? "+" : "";
        return $"({sign}{delta} since last reading)";
    }

    private static string DeltaLabel(int? current, int? previous, bool warnOnIncrease) =>
        DeltaLabel((long?)current, (long?)previous, warnOnIncrease);

    // --- Surface scan ---
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

    // --- Drive imaging ---
    private bool CanImageDrive() => !IsImaging && _currentDrive is not null;

    [RelayCommand(CanExecute = nameof(CanImageDrive))]
    private async Task ImageDrive()
    {
        if (_currentDrive is not { } drive) return;

        var dialog = new SaveFileDialog
        {
            Title = "Save drive image",
            Filter = "Raw disk image (*.img)|*.img|All files (*.*)|*.*",
            FileName = $"{drive.Letter}_drive_{DateTime.Now:yyyyMMdd_HHmm}.img",
            DefaultExt = ".img"
        };
        if (dialog.ShowDialog(Application.Current.MainWindow) != true) return;

        _imagingCts = new CancellationTokenSource();
        IsImaging = true;
        ImagingProgress = 0;
        ImagingActivity = "Starting drive image…";

        var progress = new Progress<(long written, long total, string activity)>(t =>
        {
            ImagingProgress = t.total > 0 ? t.written * 100.0 / t.total : 0;
            ImagingActivity = t.activity;
        });

        try
        {
            long bytes = await _driveImage.ImageAsync(drive, dialog.FileName, progress, _imagingCts.Token);
            ImagingActivity = $"Image complete — {HumanSize.Format(bytes)} written to {dialog.FileName}";
            ImagingProgress = 100;
        }
        catch (OperationCanceledException)
        {
            ImagingActivity = "Drive image cancelled.";
            try { System.IO.File.Delete(dialog.FileName); } catch { }
        }
        catch (Exception ex)
        {
            _log.Warn($"Drive image failed for {drive.Letter}: {ex.Message}");
            ImagingActivity = $"Drive image failed: {ex.Message}";
        }
        finally
        {
            IsImaging = false;
            _imagingCts?.Dispose();
            _imagingCts = null;
        }
    }

    private bool CanCancelImaging() => IsImaging;

    [RelayCommand(CanExecute = nameof(CanCancelImaging))]
    private void CancelImaging() => _imagingCts?.Cancel();

    private void SurfaceResultReset()
    {
        SurfaceProgress = 0;
        SurfaceActivity = "Surface scan has not been run yet.";
        SurfaceSummary = "";
        HasSurfaceResult = false;
    }
}
