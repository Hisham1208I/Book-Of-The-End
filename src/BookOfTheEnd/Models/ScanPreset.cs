namespace BookOfTheEnd.Models;

/// <summary>A saved, runnable scan configuration.</summary>
public sealed class ScanPreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Untitled preset";
    public string Description { get; set; } = "";
    public ScanType ScanType { get; set; } = ScanType.Quick;
    public List<FileCategory> Categories { get; set; } = new();

    /// <summary>Short tag shown on the card (derived from the primary category).</summary>
    public string TagLabel
    {
        get
        {
            if (Categories.Count == 0) return "All files";
            return Categories[0] switch
            {
                FileCategory.Image => "Images",
                FileCategory.Video => "Video",
                FileCategory.Audio => "Audio",
                FileCategory.Document => "Documents",
                FileCategory.Archive => "Archives",
                _ => "Files"
            };
        }
    }

    /// <summary>Segoe MDL2 glyph for the preset's primary intent.</summary>
    public string Glyph => Categories.Count == 0
        ? "\uE721"
        : Categories[0] switch
        {
            FileCategory.Image => "\uEB9F",
            FileCategory.Video => "\uE714",
            FileCategory.Audio => "\uE8D6",
            FileCategory.Document => "\uE8A5",
            FileCategory.Archive => "\uE7B8",
            _ => "\uE721"
        };
}
