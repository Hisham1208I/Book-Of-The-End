using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
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

public sealed partial class MainViewModel : ObservableObject
{
    private readonly DriveDetectionService _driveService;
    private readonly PreviewService _previewService;
    private readonly RecoveryService _recoveryService;
    private readonly ThemeService _themeService;
    private readonly PresetService _presetService;
    private readonly UpdateService _updateService;
    private readonly StorageHealthService _storageHealth;
    private readonly SessionService _sessionService;
    private readonly LoggingService _log;

    private ScanController? _controller;
    private readonly object _pendingGate = new();
    private readonly List<RecoverableFile> _pending = new();
    private readonly DispatcherTimer _flushTimer;
    private CancellationTokenSource? _previewCts;
    private CancellationTokenSource? _recoveryCts;

    private readonly ObservableCollection<RecoverableFileViewModel> _results = new();
    private IReadOnlyList<RecoverableFileViewModel> _selectedFiles = Array.Empty<RecoverableFileViewModel>();

    /// <summary>
    /// Upper bound on results kept in memory. System drives (e.g. C:) can hold a huge
    /// number of deleted MFT records; without a cap the result set exhausts memory and
    /// the process dies. When reached, the scan is stopped gracefully.
    /// </summary>
    private const int MaxResults = 50_000;
    private int _totalFound;
    private bool _capReached;

    public ObservableCollection<DriveModel> Drives { get; } = new();
    public ICollectionView DrivesView { get; }
    public ICollectionView ResultsView { get; }
    public ObservableCollection<ScanPresetViewModel> Presets { get; } = new();

    public HealthViewModel Health { get; }

    public MainViewModel(
        DriveDetectionService driveService,
        PreviewService previewService,
        RecoveryService recoveryService,
        ThemeService themeService,
        PresetService presetService,
        UpdateService updateService,
        StorageHealthService storageHealth,
        SurfaceScanService surfaceScan,
        SessionService sessionService,
        SmartHistoryService smartHistory,
        DriveImageService driveImage,
        DriveSpeedService driveSpeed,
        LoggingService log)
    {
        _driveService = driveService;
        _previewService = previewService;
        _recoveryService = recoveryService;
        _themeService = themeService;
        _presetService = presetService;
        _updateService = updateService;
        _storageHealth = storageHealth;
        _sessionService = sessionService;
        _log = log;

        Health = new HealthViewModel(storageHealth, surfaceScan, smartHistory, driveImage, driveSpeed, log);

        DrivesView = CollectionViewSource.GetDefaultView(Drives);
        DrivesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DriveModel.GroupName)));

        ResultsView = CollectionViewSource.GetDefaultView(_results);
        ResultsView.Filter = FilterResult;

        _flushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _flushTimer.Tick += (_, _) => FlushPending();
    }

    // --- Tabs ---
    [ObservableProperty] private AppTab _activeTab = AppTab.Scan;

    public bool IsScanTab => ActiveTab == AppTab.Scan;
    public bool IsResultsTab => ActiveTab == AppTab.Results;
    public bool IsHealthTab => ActiveTab == AppTab.Health;

    partial void OnActiveTabChanged(AppTab value)
    {
        OnPropertyChanged(nameof(IsScanTab));
        OnPropertyChanged(nameof(IsResultsTab));
        OnPropertyChanged(nameof(IsHealthTab));
    }

    [RelayCommand] private void ShowScanTab() => ActiveTab = AppTab.Scan;
    [RelayCommand] private void ShowResultsTab() => ActiveTab = AppTab.Results;
    [RelayCommand] private void ShowHealthTab() => ActiveTab = AppTab.Health;

    // --- Drive selection / scan config ---
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartScanCommand))]
    [NotifyPropertyChangedFor(nameof(ShowSelectDriveHint))]
    private DriveModel? _selectedDrive;

    public bool ShowSelectDriveHint => SelectedDrive is null;

    partial void OnSelectedDriveChanged(DriveModel? value) => Health.SetDrive(value);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartButtonText))]
    private bool _isQuickScan = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartButtonText))]
    private bool _isDeepScan;

    public string StartButtonText => IsDeepScan ? "Start Deep Scan" : "Start Quick Scan";
    public string QuickEstimate => "Est. 2-8 min";
    public string DeepEstimate => "Est. 20-90 min";

    public string DrivesDetectedText => $"{Drives.Count} drives detected · WinAPI";

    [ObservableProperty] private bool _includeImages = true;
    [ObservableProperty] private bool _includeVideos = true;
    [ObservableProperty] private bool _includeAudio = true;
    [ObservableProperty] private bool _includeDocuments = true;
    [ObservableProperty] private bool _includeArchives = true;
    [ObservableProperty] private bool _includeOther = true;

    [ObservableProperty] private string _searchText = "";

    // --- Result filters ---
    [ObservableProperty] private FileCategory? _filterCategory = null;
    [ObservableProperty] private long _filterMinSize = 0;
    [ObservableProperty] private DateTime? _filterDateFrom = null;
    [ObservableProperty] private DateTime? _filterDateTo = null;

    partial void OnFilterCategoryChanged(FileCategory? value) => ResultsView.Refresh();
    partial void OnFilterMinSizeChanged(long value) => ResultsView.Refresh();
    partial void OnFilterDateFromChanged(DateTime? value) => ResultsView.Refresh();
    partial void OnFilterDateToChanged(DateTime? value) => ResultsView.Refresh();

    // --- Session restore ---
    [ObservableProperty] private bool _hasLastSession;
    [ObservableProperty] private string _lastSessionSummary = "";

    // --- Scan state ---
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(PauseResumeCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(RecoverSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(RecoverAllCommand))]
    private bool _isScanning;

    [ObservableProperty] private bool _isPaused;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelRecoveryCommand))]
    private bool _isRecovering;
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private string _currentActivity = "Ready.";
    [ObservableProperty] private string _etaText = "—";
    [ObservableProperty] private string _elapsedText = "00:00";
    [ObservableProperty] private int _filesFound;
    [ObservableProperty] private string _statusMessage = "Select a drive and start a scan.";

    // --- Preview ---
    [ObservableProperty] private PreviewResult? _currentPreview;

    public bool IsLightTheme => _themeService.Current == AppTheme.Light;

    public int ResultCount => _results.Count;

    partial void OnSearchTextChanged(string value) => ResultsView.Refresh();

    partial void OnIsQuickScanChanged(bool value) { if (value) IsDeepScan = false; }
    partial void OnIsDeepScanChanged(bool value) { if (value) IsQuickScan = false; }

    public void Initialize()
    {
        RefreshDrives();
        SelectedDrive = Drives.FirstOrDefault(d => d.IsReady) ?? Drives.FirstOrDefault();
        LoadPresets();
        RefreshSessionBanner();
        StatusMessage = "Ready. Select a drive and choose Quick or Deep scan.";
    }

    private void RefreshSessionBanner()
    {
        var session = _sessionService.HasSession ? _sessionService.Load() : null;
        if (session is null) { HasLastSession = false; return; }
        HasLastSession = true;
        LastSessionSummary =
            $"{session.TotalFound:N0} files from {session.DriveLetter}:  ·  " +
            $"saved {session.SavedAt:ddd d MMM, h:mm tt}  ·  {session.ScanType} scan";
    }

    [RelayCommand]
    private async Task RestoreSession()
    {
        StatusMessage = "Loading session…";
        SessionData? session;
        IReadOnlyList<RecoverableFile> files;
        try
        {
            // Deserialize on a pool thread to keep the UI responsive
            (session, files) = await Task.Run(() =>
            {
                var s = _sessionService.Load();
                return s is null
                    ? (null, (IReadOnlyList<RecoverableFile>)Array.Empty<RecoverableFile>())
                    : (s, _sessionService.ToFiles(s));
            });
        }
        catch (Exception ex)
        {
            _log.Warn($"Session restore failed: {ex.Message}");
            StatusMessage = "Failed to load last session.";
            return;
        }

        if (session is null) return;

        _results.Clear();
        lock (_pendingGate) _pending.Clear();
        _totalFound = 0;
        OnPropertyChanged(nameof(ResultCount));

        foreach (var f in files)
            _results.Add(new RecoverableFileViewModel(f));

        FilesFound = _results.Count;
        OnPropertyChanged(nameof(ResultCount));
        ActiveTab = AppTab.Results;
        StatusMessage = $"Session restored: {_results.Count:N0} file(s) from {session.DriveLetter}: ({session.ScanType} scan, {session.SavedAt:d MMM yyyy}).";
    }

    [RelayCommand]
    private void DismissSession()
    {
        _sessionService.Delete();
        HasLastSession = false;
        LastSessionSummary = "";
    }

    private void LoadPresets()
    {
        Presets.Clear();
        foreach (var p in _presetService.Load())
            Presets.Add(new ScanPresetViewModel(p));
    }

    [RelayCommand]
    private void RefreshDrives()
    {
        try
        {
            string? prev = SelectedDrive?.Letter;
            Drives.Clear();
            foreach (var d in _driveService.GetDrives())
                Drives.Add(d);
            SelectedDrive = Drives.FirstOrDefault(d => d.Letter == prev)
                            ?? Drives.FirstOrDefault(d => d.IsReady)
                            ?? Drives.FirstOrDefault();
            OnPropertyChanged(nameof(DrivesDetectedText));
        }
        catch (Exception ex)
        {
            _log.Error("Drive refresh failed", ex);
            StatusMessage = $"Failed to enumerate drives: {ex.Message}";
        }
    }

    private bool CanStartScan() => SelectedDrive is not null && !IsScanning;

    [RelayCommand(CanExecute = nameof(CanStartScan))]
    private async Task StartScan()
    {
        if (SelectedDrive is not { } drive) return;
        if (!drive.IsReady)
        {
            StatusMessage = $"Drive {drive.Letter}: is not ready.";
            return;
        }

        if (!await ConfirmRecoverySafetyAsync(drive)) return;

        _results.Clear();
        lock (_pendingGate) _pending.Clear();
        _totalFound = 0;
        _capReached = false;
        OnPropertyChanged(nameof(ResultCount));
        CurrentPreview = null;
        FilesFound = 0;
        ProgressPercent = 0;

        var options = BuildOptions();
        IScanEngine engine = IsDeepScan ? new DeepScanEngine(_log) : new QuickScanEngine(_log);

        _controller = new ScanController();
        IsScanning = true;
        IsPaused = false;
        ActiveTab = AppTab.Results;
        _flushTimer.Start();

        var progress = new Progress<ScanProgress>(OnProgress);
        var scanWatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            _log.Info($"{engine.ScanType} scan started on {drive.Letter}: ({drive.FileSystem}).");
            await engine.ScanAsync(drive, options, _controller, progress, OnFileFound);
            scanWatch.Stop();
            string dur = scanWatch.Elapsed.TotalHours >= 1
                ? scanWatch.Elapsed.ToString(@"h\:mm\:ss")
                : scanWatch.Elapsed.ToString(@"m\:ss");
            StatusMessage = $"{engine.ScanType} scan finished in {dur}. {_results.Count + _pending.Count} item(s) found.";
        }
        catch (OperationCanceledException)
        {
            if (_capReached)
            {
                StatusMessage = $"Stopped at {MaxResults:N0} results (limit reached). Narrow the file types or use a smaller drive to see more.";
                CurrentActivity = $"Result limit ({MaxResults:N0}) reached.";
            }
            else
            {
                StatusMessage = "Scan cancelled.";
                CurrentActivity = "Scan cancelled.";
            }
        }
        catch (Exception ex)
        {
            _log.Error("Scan failed", ex);
            StatusMessage = $"Scan error: {ex.Message}";
        }
        finally
        {
            FlushPending();
            _flushTimer.Stop();
            IsScanning = false;
            IsPaused = false;
            _controller?.Dispose();
            _controller = null;
            OnPropertyChanged(nameof(ResultCount));

            if (_results.Count > 0 && SelectedDrive is { } savedDrive)
            {
                // Capture before going async so the UI can't change them underneath us
                string letter = savedDrive.Letter;
                ScanType scanType = IsDeepScan ? ScanType.Deep : ScanType.Quick;
                var models = _results.Select(vm => vm.Model).ToList();
                _ = Task.Run(() => _sessionService.Save(letter, scanType, models))
                        .ContinueWith(_ => RefreshSessionBanner(),
                            System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
            }
        }
    }

    /// <summary>
    /// Warns the user before scanning a drive that shows signs of failure, since reading
    /// from failing media can worsen the damage. Returns false if the user cancels.
    /// </summary>
    private async Task<bool> ConfirmRecoverySafetyAsync(DriveModel drive)
    {
        var health = Health.CurrentHealth;
        if (health is null || !string.Equals(health.DriveLetter, drive.Letter, StringComparison.OrdinalIgnoreCase))
        {
            try { health = await _storageHealth.EvaluateAsync(drive); }
            catch (Exception ex) { _log.Warn($"Pre-scan health check failed: {ex.Message}"); return true; }
        }

        bool risky = health.DataAvailable &&
                     (health.ClonePriority is ClonePriority.High or ClonePriority.Emergency
                      || health.Status is HealthStatus.Critical or HealthStatus.Failing);
        if (!risky) return true;

        bool proceed = Views.AppDialogWindow.ShowConfirm(
            Application.Current.MainWindow,
            "Drive shows signs of failure",
            health.Readiness.Recommendation,
            $"{health.Readiness.Reason}\n\nReading from a failing drive can worsen the damage. " +
            "The safest approach is to clone/image the drive first and recover from the copy.",
            confirmText: "Scan anyway",
            cancelText: "Cancel");

        if (!proceed)
            StatusMessage = "Scan cancelled. Cloning the drive first is the safest option.";
        return proceed;
    }

    private bool CanControlScan() => IsScanning;

    [RelayCommand(CanExecute = nameof(CanControlScan))]
    private void PauseResume()
    {
        if (_controller is null) return;
        if (_controller.IsPaused)
        {
            _controller.Resume();
            IsPaused = false;
            CurrentActivity = "Resuming...";
        }
        else
        {
            _controller.Pause();
            IsPaused = true;
            CurrentActivity = "Paused.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanControlScan))]
    private void CancelScan() => _controller?.Cancel();

    private bool CanCancelRecovery() => IsRecovering;

    [RelayCommand(CanExecute = nameof(CanCancelRecovery))]
    private void CancelRecovery() => _recoveryCts?.Cancel();

    private void OnProgress(ScanProgress p)
    {
        ProgressPercent = p.Percent;
        if (!string.IsNullOrEmpty(p.CurrentActivity)) CurrentActivity = p.CurrentActivity;
        EtaText = p.EstimatedRemaining is null && p.Percent >= 100 ? "Done" : p.EtaDisplay;
        ElapsedText = p.ElapsedDisplay;
    }

    private void OnFileFound(RecoverableFile file)
    {
        bool capJustReached = false;
        lock (_pendingGate)
        {
            if (_capReached) return;
            _pending.Add(file);
            _totalFound++;
            if (_totalFound >= MaxResults)
            {
                _capReached = true;
                capJustReached = true;
            }
        }

        if (capJustReached)
        {
            // Stop the scan to avoid exhausting memory on very large (system) drives.
            _controller?.Cancel();
        }
    }

    private void FlushPending()
    {
        RecoverableFile[] batch;
        lock (_pendingGate)
        {
            if (_pending.Count == 0) return;
            batch = _pending.ToArray();
            _pending.Clear();
        }
        foreach (var f in batch)
            _results.Add(new RecoverableFileViewModel(f));
        FilesFound = _results.Count;
        OnPropertyChanged(nameof(ResultCount));
    }

    // --- Recovery ---
    private bool CanRecoverSelected() => !IsScanning && _selectedFiles.Count > 0;

    [RelayCommand(CanExecute = nameof(CanRecoverSelected))]
    private Task RecoverSelected() => RecoverFiles(_selectedFiles.ToList());

    private bool CanRecoverAll() => !IsScanning && _results.Count > 0;

    [RelayCommand(CanExecute = nameof(CanRecoverAll))]
    private Task RecoverAll() => RecoverFiles(_results.ToList());

    [RelayCommand]
    private Task RecoverSingleFile(RecoverableFileViewModel? file) =>
        file is null ? Task.CompletedTask : RecoverFiles(new List<RecoverableFileViewModel> { file });

    private async Task RecoverFiles(List<RecoverableFileViewModel> selected)
    {
        if (selected.Count == 0) return;

        var dialog = new OpenFolderDialog
        {
            Title = "Choose recovery destination",
            Multiselect = false,
            InitialDirectory = LoadLastRecoveryPath() ?? ""
        };
        if (dialog.ShowDialog() != true) return;
        string destination = dialog.FolderName;
        SaveLastRecoveryPath(destination);

        var models = selected.Select(s => s.Model).ToList();

        if (RecoveryService.IsSameDrive(models, destination))
        {
            if (!Views.AppDialogWindow.ShowConfirm(
                    Application.Current.MainWindow,
                    "Unsafe destination",
                    "The destination is on the same drive you are recovering from. This can overwrite the data you are trying to recover.",
                    "It is strongly recommended to save recovered files to a different drive.",
                    confirmText: "Continue anyway",
                    cancelText: "Choose another folder"))
                return;
        }

        _recoveryCts = new CancellationTokenSource();
        IsRecovering = true;
        ProgressPercent = 0;

        var progress = new Progress<(int done, int total, string current)>(t =>
        {
            ProgressPercent = t.total == 0 ? 0 : t.done * 100.0 / t.total;
            CurrentActivity = $"Recovering {t.done}/{t.total}: {t.current}";
        });

        try
        {
            StatusMessage = $"Recovering {models.Count} file(s)...";
            var results = await _recoveryService.RecoverAsync(models, destination, preserveStructure: true, progress, _recoveryCts.Token);
            foreach (var vm in selected) vm.Refresh();

            int ok = results.Count(r => r.Success);
            int fail = results.Count - ok;
            StatusMessage = $"Recovery complete: {ok} succeeded, {fail} failed. Report saved to logs folder.";

            bool openFolder = Views.AppDialogWindow.ShowMessageWithAction(
                Application.Current.MainWindow,
                fail == 0 ? "Recovery complete" : "Recovery finished with errors",
                fail == 0
                    ? $"{ok} file{(ok == 1 ? "" : "s")} recovered successfully."
                    : $"{ok} file{(ok == 1 ? "" : "s")} recovered, {fail} failed.",
                $"Destination:\n{destination}",
                fail == 0 ? Views.AppDialogKind.Success : Views.AppDialogKind.Warning,
                actionText: "Open Folder");

            if (openFolder)
                System.Diagnostics.Process.Start("explorer.exe", destination);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Recovery cancelled.";
        }
        catch (Exception ex)
        {
            _log.Error("Recovery failed", ex);
            StatusMessage = $"Recovery error: {ex.Message}";
        }
        finally
        {
            IsRecovering = false;
            _recoveryCts?.Dispose();
            _recoveryCts = null;
            ProgressPercent = 0;
            CurrentActivity = "Ready.";
        }
    }

    // --- Theme ---
    [RelayCommand]
    private void ToggleTheme()
    {
        _themeService.Toggle();
        OnPropertyChanged(nameof(IsLightTheme));
    }

    [RelayCommand]
    private void ShowDisclaimer()
    {
        var win = new Views.DisclaimerWindow { Owner = Application.Current.MainWindow };
        win.ShowDialog();
    }

    [RelayCommand]
    private void ShowSettings()
    {
        var win = new Views.SettingsWindow(_themeService, _log, _updateService)
        {
            Owner = Application.Current.MainWindow
        };
        win.ShowDialog();
        OnPropertyChanged(nameof(IsLightTheme));
    }

    /// <summary>Background check on launch; only prompts the user if an update exists.</summary>
    public async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            await _updateService.CheckAndUpdateInteractiveAsync(Application.Current.MainWindow, silent: true);
        }
        catch (Exception ex)
        {
            _log.Warn($"Startup update check failed: {ex.Message}");
        }
    }

    // --- Scan presets ---
    [RelayCommand]
    private async Task RunPreset(ScanPresetViewModel? preset)
    {
        if (preset is null || IsScanning) return;
        if (SelectedDrive is null)
        {
            ActiveTab = AppTab.Scan;
            StatusMessage = "Select a drive first, then run the preset.";
            return;
        }

        ApplyPreset(preset.Model);
        if (StartScanCommand.CanExecute(null))
            await StartScanCommand.ExecuteAsync(null);
    }

    private void ApplyPreset(ScanPreset preset)
    {
        IsDeepScan = preset.ScanType == ScanType.Deep;
        IsQuickScan = !IsDeepScan;

        bool all = preset.Categories.Count == 0;
        IncludeImages = all || preset.Categories.Contains(FileCategory.Image);
        IncludeVideos = all || preset.Categories.Contains(FileCategory.Video);
        IncludeAudio = all || preset.Categories.Contains(FileCategory.Audio);
        IncludeDocuments = all || preset.Categories.Contains(FileCategory.Document);
        IncludeArchives = all || preset.Categories.Contains(FileCategory.Archive);
        IncludeOther = all || preset.Categories.Contains(FileCategory.Other);
    }

    [RelayCommand]
    private void NewPreset()
    {
        var editor = new Views.PresetEditorWindow { Owner = Application.Current.MainWindow };
        if (editor.ShowDialog() == true && editor.Result is { } created)
        {
            Presets.Add(new ScanPresetViewModel(created));
            PersistPresets();
            StatusMessage = $"Preset '{created.Name}' saved.";
        }
    }

    [RelayCommand]
    private void DeletePreset(ScanPresetViewModel? preset)
    {
        if (preset is null) return;
        Presets.Remove(preset);
        PersistPresets();
        StatusMessage = $"Preset '{preset.Name}' deleted.";
    }

    private void PersistPresets() => _presetService.Save(Presets.Select(p => p.Model));

    // --- Selection / preview (driven from the view) ---
    public void UpdateSelection(IReadOnlyList<RecoverableFileViewModel> files)
    {
        _selectedFiles = files;
        RecoverSelectedCommand.NotifyCanExecuteChanged();
        RecoverAllCommand.NotifyCanExecuteChanged();
        StatusMessage = files.Count switch
        {
            0 => $"{_results.Count} item(s). Select files to preview or recover.",
            1 => $"Selected: {files[0].FileName} ({files[0].SizeDisplay}).",
            _ => $"{files.Count} files selected ({HumanSize.Format(files.Sum(f => f.Size))})."
        };
    }

    public async Task PreviewAsync(RecoverableFileViewModel? file)
    {
        _previewCts?.Cancel();
        if (file is null) { CurrentPreview = null; return; }

        _previewCts = new CancellationTokenSource();
        var token = _previewCts.Token;
        try
        {
            CurrentPreview = new PreviewResult { Kind = PreviewKind.None, Message = "Loading preview..." };
            var result = await _previewService.CreateAsync(file.Model, token);
            if (!token.IsCancellationRequested)
                CurrentPreview = result;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            CurrentPreview = new PreviewResult { Kind = PreviewKind.None, Message = $"Preview failed: {ex.Message}" };
        }
    }

    private ScanOptions BuildOptions()
    {
        var categories = new HashSet<FileCategory>();
        if (IncludeImages) categories.Add(FileCategory.Image);
        if (IncludeVideos) categories.Add(FileCategory.Video);
        if (IncludeAudio) categories.Add(FileCategory.Audio);
        if (IncludeDocuments) categories.Add(FileCategory.Document);
        if (IncludeArchives) categories.Add(FileCategory.Archive);
        if (IncludeOther) categories.Add(FileCategory.Other);

        bool all = categories.Count == 6;
        return new ScanOptions
        {
            ScanType = IsDeepScan ? ScanType.Deep : ScanType.Quick,
            Categories = all ? new HashSet<FileCategory>() : categories
        };
    }

    private bool FilterResult(object obj)
    {
        if (obj is not RecoverableFileViewModel vm) return false;

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            string q = SearchText.Trim();
            if (!vm.FileName.Contains(q, StringComparison.OrdinalIgnoreCase)
                && !vm.Extension.Contains(q, StringComparison.OrdinalIgnoreCase)
                && !vm.OriginalPath.Contains(q, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (FilterCategory is { } cat && vm.Category != cat) return false;
        if (FilterMinSize > 0 && vm.Size < FilterMinSize) return false;

        if (FilterDateFrom is { } from && vm.Modified is { } modFrom && modFrom < from) return false;
        if (FilterDateTo   is { } to   && vm.Modified is { } modTo   && modTo   > to.AddDays(1))  return false;

        return true;
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = "";
        FilterCategory = null;
        FilterMinSize = 0;
        FilterDateFrom = null;
        FilterDateTo = null;
    }

    [RelayCommand]
    private void ExportCsv()
    {
        var visible = ResultsView.Cast<RecoverableFileViewModel>().ToList();
        if (visible.Count == 0)
        {
            StatusMessage = "No results to export.";
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export results as CSV",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = $"BookOfTheEnd_{DateTime.Now:yyyyMMdd_HHmm}.csv",
            DefaultExt = ".csv"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            using var writer = new System.IO.StreamWriter(dialog.FileName, false, System.Text.Encoding.UTF8);
            writer.WriteLine("Name,Extension,Size (bytes),Category,Status,Quality,Modified,Deleted,Source,Original Path");
            foreach (var vm in visible)
            {
                writer.WriteLine(string.Join(",",
                    CsvEscape(vm.FileName),
                    CsvEscape(vm.Extension),
                    vm.Size.ToString(),
                    vm.Category.ToString(),
                    vm.StatusDisplay,
                    vm.Quality.ToString(),
                    vm.Modified?.ToString("yyyy-MM-dd HH:mm") ?? "",
                    vm.Deleted?.ToString("yyyy-MM-dd HH:mm") ?? "",
                    vm.SourceDisplay,
                    CsvEscape(vm.OriginalPath)));
            }
            StatusMessage = $"Exported {visible.Count} results to {dialog.FileName}";
        }
        catch (Exception ex)
        {
            _log.Error("CSV export failed", ex);
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    private static string CsvEscape(string s)
    {
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    // --- Recovery destination memory ---
    private static string RecoverySettingsPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Book of the End", "recovery-settings.json");

    private static string? LoadLastRecoveryPath()
    {
        try
        {
            if (!System.IO.File.Exists(RecoverySettingsPath)) return null;
            var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(RecoverySettingsPath));
            return doc.RootElement.TryGetProperty("lastDestination", out var p) ? p.GetString() : null;
        }
        catch { return null; }
    }

    private static void SaveLastRecoveryPath(string path)
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(RecoverySettingsPath)!);
            System.IO.File.WriteAllText(RecoverySettingsPath,
                System.Text.Json.JsonSerializer.Serialize(new { lastDestination = path }));
        }
        catch { }
    }
}
