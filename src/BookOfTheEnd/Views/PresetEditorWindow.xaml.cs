using System.Windows;
using BookOfTheEnd.Models;

namespace BookOfTheEnd.Views;

public partial class PresetEditorWindow : Window
{
    public ScanPreset? Result { get; private set; }

    public PresetEditorWindow()
    {
        InitializeComponent();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        string name = NameBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Please enter a preset name.", "New Preset",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var categories = new List<FileCategory>();
        if (CbImages.IsChecked == true) categories.Add(FileCategory.Image);
        if (CbVideos.IsChecked == true) categories.Add(FileCategory.Video);
        if (CbAudio.IsChecked == true) categories.Add(FileCategory.Audio);
        if (CbDocuments.IsChecked == true) categories.Add(FileCategory.Document);
        if (CbArchives.IsChecked == true) categories.Add(FileCategory.Archive);
        if (CbOther.IsChecked == true) categories.Add(FileCategory.Other);

        Result = new ScanPreset
        {
            Name = name,
            Description = DescBox.Text?.Trim() ?? "",
            ScanType = DeepRadio.IsChecked == true ? ScanType.Deep : ScanType.Quick,
            Categories = categories
        };

        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
