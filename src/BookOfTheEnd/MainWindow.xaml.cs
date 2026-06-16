using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using BookOfTheEnd.Services;
using BookOfTheEnd.ViewModels;

namespace BookOfTheEnd;

public partial class MainWindow : Window
{
    private MainViewModel? _vm;

    public MainWindow()
    {
        InitializeComponent();
        TrySetWindowIcon();
        DataContextChanged += OnDataContextChanged;
    }

    private void TrySetWindowIcon()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "app.ico");
            if (File.Exists(path))
                Icon = BitmapFrame.Create(new Uri(path, UriKind.Absolute));
        }
        catch
        {
            // Non-fatal: ApplicationIcon still provides the taskbar icon.
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnViewModelPropertyChanged;
        _vm = DataContext as MainViewModel;
        if (_vm is not null) _vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentPreview))
            RenderPreview(_vm?.CurrentPreview);
        else if (e.PropertyName is nameof(MainViewModel.IsScanTab) or nameof(MainViewModel.IsResultsTab))
            RenderPreview(_vm?.CurrentPreview);
    }

    // ===== Window chrome =====
    private void OnMinimize(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void OnMaximizeRestore(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnWindowStateChanged(object sender, EventArgs e)
    {
        // Avoid content clipping under the screen edges when maximized with custom chrome.
        Padding = WindowState == WindowState.Maximized ? new Thickness(8) : new Thickness(0);
        if (MaxButton is not null)
        {
            MaxButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
            MaxButton.ToolTip = WindowState == WindowState.Maximized ? "Restore" : "Maximize";
        }
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || _vm is null) return;
        // Don't hijack Space when typing or when a clickable control has focus.
        if (Keyboard.FocusedElement is TextBoxBase or ButtonBase) return;
        if (!_vm.IsScanTab) return;
        if (_vm.StartScanCommand.CanExecute(null))
        {
            _vm.StartScanCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ===== Results selection / preview =====
    private void OnResultsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm is null) return;
        var selected = ResultsGrid.SelectedItems.Cast<RecoverableFileViewModel>().ToList();
        _vm.UpdateSelection(selected);
        _ = _vm.PreviewAsync(selected.LastOrDefault());
    }

    private void RenderPreview(PreviewResult? preview)
    {
        StopMedia();
        ImageView.Visibility = Visibility.Collapsed;
        ImageView.Source = null;
        TextView.Visibility = Visibility.Collapsed;
        MediaView.Visibility = Visibility.Collapsed;
        MediaControls.Visibility = Visibility.Collapsed;
        PdfView.Visibility = Visibility.Collapsed;
        PreviewMessage.Visibility = Visibility.Collapsed;
        ScanTabHint.Visibility = Visibility.Collapsed;

        if (preview is null)
        {
            if (_vm?.IsResultsTab == true)
            {
                PreviewMessage.Text = "Select a file in Results to preview it before recovery.";
                PreviewMessage.Visibility = Visibility.Visible;
            }
            else
            {
                ScanTabHint.Visibility = Visibility.Visible;
            }
            return;
        }

        switch (preview.Kind)
        {
            case PreviewKind.Image:
                ShowImage(preview);
                break;
            case PreviewKind.Text:
                TextView.Text = preview.Text ?? "";
                TextView.Visibility = Visibility.Visible;
                break;
            case PreviewKind.Media:
                ShowMedia(preview);
                break;
            case PreviewKind.Pdf:
                _ = ShowPdfAsync(preview);
                break;
            default:
                PreviewMessage.Text = string.IsNullOrEmpty(preview.Message)
                    ? "No preview available for this file type."
                    : preview.Message;
                PreviewMessage.Visibility = Visibility.Visible;
                break;
        }
    }

    private void ShowImage(PreviewResult preview)
    {
        try
        {
            if (preview.ImageBytes is null || preview.ImageBytes.Length == 0)
                throw new InvalidOperationException("No image data.");
            using var ms = new MemoryStream(preview.ImageBytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            ImageView.Source = bmp;
            ImageView.Visibility = Visibility.Visible;
        }
        catch
        {
            PreviewMessage.Text = "This image could not be decoded — the recovered data may be incomplete or corrupt.";
            PreviewMessage.Visibility = Visibility.Visible;
        }
    }

    private void ShowMedia(PreviewResult preview)
    {
        if (string.IsNullOrEmpty(preview.TempFilePath) || !File.Exists(preview.TempFilePath))
        {
            PreviewMessage.Text = "Media preview unavailable.";
            PreviewMessage.Visibility = Visibility.Visible;
            return;
        }
        MediaView.Source = new Uri(preview.TempFilePath);
        MediaView.Visibility = Visibility.Visible;
        MediaControls.Visibility = Visibility.Visible;
        MediaView.Play();
    }

    private async Task ShowPdfAsync(PreviewResult preview)
    {
        if (string.IsNullOrEmpty(preview.TempFilePath) || !File.Exists(preview.TempFilePath))
        {
            PreviewMessage.Text = "PDF preview unavailable.";
            PreviewMessage.Visibility = Visibility.Visible;
            return;
        }
        try
        {
            PdfView.Visibility = Visibility.Visible;
            await PdfView.EnsureCoreWebView2Async();
            PdfView.CoreWebView2.Navigate(new Uri(preview.TempFilePath).AbsoluteUri);
        }
        catch
        {
            PdfView.Visibility = Visibility.Collapsed;
            PreviewMessage.Text = "PDF preview requires the WebView2 runtime. The file can still be recovered.";
            PreviewMessage.Visibility = Visibility.Visible;
        }
    }

    private void StopMedia()
    {
        try
        {
            MediaView.Stop();
            MediaView.Source = null;
        }
        catch { /* ignore */ }
    }

    private void OnMediaOpened(object sender, RoutedEventArgs e) { }
    private void OnMediaPlay(object sender, RoutedEventArgs e) => MediaView.Play();
    private void OnMediaPause(object sender, RoutedEventArgs e) => MediaView.Pause();
    private void OnMediaStop(object sender, RoutedEventArgs e) => MediaView.Stop();
}
