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
        if (_vm is null) return;
        bool ctrl = (e.KeyboardDevice.Modifiers & ModifierKeys.Control) != 0;
        bool typing = Keyboard.FocusedElement is TextBoxBase;

        switch (e.Key)
        {
            // Ctrl+S — start scan (works from any tab, not while typing)
            case Key.S when ctrl && !typing:
                if (_vm.StartScanCommand.CanExecute(null))
                {
                    _vm.StartScanCommand.Execute(null);
                    e.Handled = true;
                }
                break;

            // Ctrl+R — recover selected files
            case Key.R when ctrl && !typing:
                if (_vm.RecoverSelectedCommand.CanExecute(null))
                {
                    _vm.RecoverSelectedCommand.Execute(null);
                    e.Handled = true;
                }
                break;

            // Ctrl+F — focus the search box on the Results tab
            case Key.F when ctrl:
                _vm.ShowResultsTabCommand.Execute(null);
                SearchBox?.Focus();
                SearchBox?.SelectAll();
                e.Handled = true;
                break;

            // Escape — cancel active scan or close search
            case Key.Escape when !typing:
                if (_vm.IsScanning && _vm.CancelScanCommand.CanExecute(null))
                {
                    _vm.CancelScanCommand.Execute(null);
                    e.Handled = true;
                }
                else if (!string.IsNullOrEmpty(_vm.SearchText))
                {
                    _vm.SearchText = "";
                    e.Handled = true;
                }
                break;

            // Space — start scan on Scan tab, or preview selected on Results tab
            case Key.Space when !typing && !(Keyboard.FocusedElement is ButtonBase):
                if (_vm.IsScanTab && _vm.StartScanCommand.CanExecute(null))
                {
                    _vm.StartScanCommand.Execute(null);
                    e.Handled = true;
                }
                else if (_vm.IsResultsTab && ResultsGrid.SelectedItem is RecoverableFileViewModel file)
                {
                    _ = _vm.PreviewAsync(file);
                    e.Handled = true;
                }
                break;
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

    // ===== Right-click context menu =====
    private void OnResultsPreviewRightClick(object sender, MouseButtonEventArgs e)
    {
        // Select the row under the cursor before the context menu opens so SelectedItem is set.
        var row = FindVisualAncestor<DataGridRow>((DependencyObject)e.OriginalSource);
        if (row is not null) row.IsSelected = true;
    }

    private void OnContextMenuRecover(object sender, RoutedEventArgs e)
    {
        if (_vm is null || ResultsGrid.SelectedItem is not RecoverableFileViewModel file) return;
        _vm.RecoverSingleFileCommand.Execute(file);
    }

    private void OnContextMenuPreview(object sender, RoutedEventArgs e)
    {
        if (_vm is null || ResultsGrid.SelectedItem is not RecoverableFileViewModel file) return;
        _ = _vm.PreviewAsync(file);
    }

    private static T? FindVisualAncestor<T>(DependencyObject d) where T : DependencyObject
    {
        while (d is not null)
        {
            if (d is T t) return t;
            d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        }
        return null;
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
