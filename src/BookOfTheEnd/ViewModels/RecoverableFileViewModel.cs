using BookOfTheEnd.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BookOfTheEnd.ViewModels;

/// <summary>Observable wrapper around a <see cref="RecoverableFile"/> for the results grid.</summary>
public sealed partial class RecoverableFileViewModel : ObservableObject
{
    public RecoverableFile Model { get; }

    public RecoverableFileViewModel(RecoverableFile model) => Model = model;

    public string FileName => Model.FileName;
    public bool IsNameSynthesized => Model.IsNameSynthesized;
    public string Extension => string.IsNullOrEmpty(Model.Extension) ? "—" : Model.Extension;
    public FileCategory Category => Model.Category;
    public long Size => Model.Size;
    public string SizeDisplay => Model.SizeDisplay;
    public DateTime? Modified => Model.Modified;
    public DateTime? Deleted => Model.Deleted;
    public string OriginalPath => Model.OriginalPath ?? (Model.Source == RecoverySource.Carved ? "Unallocated space" : "—");
    public RecoverySource Source => Model.Source;
    public string SourceDisplay => Model.Source switch
    {
        RecoverySource.RecycleBin => "Recycle Bin",
        RecoverySource.MasterFileTable => "File table",
        RecoverySource.Fat32Directory => "FAT32",
        RecoverySource.Carved => "Carved",
        _ => "—"
    };

    public RecoveryStatus Status => Model.Status;
    public RecoveryQuality Quality => Model.Quality;

    public string StatusDisplay => Model.Status switch
    {
        RecoveryStatus.Recoverable => "Recoverable",
        RecoveryStatus.PartialMetadata => "No metadata",
        RecoveryStatus.Overwritten => "May be overwritten",
        RecoveryStatus.Recovered => "Recovered",
        RecoveryStatus.Failed => "Failed",
        _ => Model.Status.ToString()
    };

    /// <summary>Pushes model state changes (e.g. after recovery) to the UI.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusDisplay));
        OnPropertyChanged(nameof(Quality));
        OnPropertyChanged(nameof(FileName));
    }
}
