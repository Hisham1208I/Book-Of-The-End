using System.Windows;
using System.Windows.Threading;
using BookOfTheEnd.Services;
using BookOfTheEnd.ViewModels;

namespace BookOfTheEnd;

public partial class App : Application
{
    private LoggingService? _log;
    private PreviewService? _preview;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _log = new LoggingService();
        _log.Info("Book of the End starting.");

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            _log.Error("Unhandled domain exception", args.ExceptionObject as Exception);

        var theme = new ThemeService();
        theme.Apply(AppTheme.Light);

        var drives = new DriveDetectionService(_log);
        var content = new FileContentService();
        _preview = new PreviewService(content, _log);
        var recovery = new RecoveryService(content, _log);
        var presets = new PresetService(_log);
        var updates = new UpdateService(_log);

        var viewModel = new MainViewModel(drives, _preview, recovery, theme, presets, updates, _log);
        var window = new MainWindow { DataContext = viewModel };
        window.Show();

        viewModel.Initialize();

        // Non-blocking: only surfaces a prompt if a newer release is published.
        _ = viewModel.CheckForUpdatesOnStartupAsync();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _log?.Error("Unhandled UI exception", e.Exception);
        MessageBox.Show(
            $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nThe error has been logged.",
            "Book of the End", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _preview?.Cleanup();
        _log?.Info("Book of the End exiting.");
        base.OnExit(e);
    }
}
