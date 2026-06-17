using System.Diagnostics;
using System.IO;
using System.Windows;
using BookOfTheEnd.Services;

namespace BookOfTheEnd.Views;

public partial class SettingsWindow : Window
{
    private readonly ThemeService _theme;
    private readonly LoggingService _log;
    private readonly UpdateService _updates;
    private bool _initialized;

    public SettingsWindow(ThemeService theme, LoggingService log, UpdateService updates)
    {
        _theme = theme;
        _log = log;
        _updates = updates;
        InitializeComponent();

        LightRadio.IsChecked = _theme.Current == AppTheme.Light;
        DarkRadio.IsChecked = _theme.Current == AppTheme.Dark;
        _initialized = true;
    }

    private async void OnCheckForUpdates(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        UpdateStatus.Text = "Checking...";
        try
        {
            await _updates.CheckAndUpdateInteractiveAsync(this, silent: false);
            UpdateStatus.Text = "";
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private void OnThemeChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        _theme.Apply(LightRadio.IsChecked == true ? AppTheme.Light : AppTheme.Dark);
    }

    private void OnOpenLogs(object sender, RoutedEventArgs e) => OpenFolder(_log.LogDirectory);

    private void OnOpenReports(object sender, RoutedEventArgs e)
        => OpenFolder(Path.GetFullPath(Path.Combine(_log.LogDirectory, "..", "reports")));

    private void OnShowDisclaimer(object sender, RoutedEventArgs e)
    {
        var win = new DisclaimerWindow { Owner = this };
        win.ShowDialog();
    }

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.Warn($"Failed to open folder {path}: {ex.Message}");
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
