using BookOfTheEnd.Models;

namespace BookOfTheEnd.ViewModels;

/// <summary>Display wrapper for a <see cref="ScanPreset"/>.</summary>
public sealed class ScanPresetViewModel
{
    public ScanPreset Model { get; }

    public ScanPresetViewModel(ScanPreset model) => Model = model;

    public string Name => Model.Name;
    public string Description => Model.Description;
    public string TagLabel => Model.TagLabel;
    public string Glyph => Model.Glyph;
    public string ScanTypeLabel => Model.ScanType == ScanType.Deep ? "Deep" : "Quick";
}
