using System.IO;
using BookOfTheEnd.Models;

namespace BookOfTheEnd.Services;

/// <summary>Maps file extensions to high-level categories used for filtering and previews.</summary>
public static class FileTypeMap
{
    private static readonly Dictionary<string, FileCategory> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        // Images
        [".jpg"] = FileCategory.Image,
        [".jpeg"] = FileCategory.Image,
        [".png"] = FileCategory.Image,
        [".gif"] = FileCategory.Image,
        [".bmp"] = FileCategory.Image,
        [".tif"] = FileCategory.Image,
        [".tiff"] = FileCategory.Image,
        [".webp"] = FileCategory.Image,
        [".cr2"] = FileCategory.Image,
        [".nef"] = FileCategory.Image,
        [".arw"] = FileCategory.Image,
        [".dng"] = FileCategory.Image,
        // Video
        [".mp4"] = FileCategory.Video,
        [".mov"] = FileCategory.Video,
        [".avi"] = FileCategory.Video,
        [".mkv"] = FileCategory.Video,
        [".wmv"] = FileCategory.Video,
        [".flv"] = FileCategory.Video,
        // Audio
        [".mp3"] = FileCategory.Audio,
        [".wav"] = FileCategory.Audio,
        [".flac"] = FileCategory.Audio,
        [".aac"] = FileCategory.Audio,
        [".ogg"] = FileCategory.Audio,
        [".m4a"] = FileCategory.Audio,
        // Documents
        [".pdf"] = FileCategory.Document,
        [".doc"] = FileCategory.Document,
        [".docx"] = FileCategory.Document,
        [".xls"] = FileCategory.Document,
        [".xlsx"] = FileCategory.Document,
        [".ppt"] = FileCategory.Document,
        [".pptx"] = FileCategory.Document,
        [".txt"] = FileCategory.Document,
        [".rtf"] = FileCategory.Document,
        [".csv"] = FileCategory.Document,
        // Archives
        [".zip"] = FileCategory.Archive,
        [".rar"] = FileCategory.Archive,
        [".7z"] = FileCategory.Archive,
        [".gz"] = FileCategory.Archive,
        [".tar"] = FileCategory.Archive,
    };

    public static FileCategory FromExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return FileCategory.Other;
        if (!extension.StartsWith('.')) extension = "." + extension;
        return Map.TryGetValue(extension, out var category) ? category : FileCategory.Other;
    }

    public static FileCategory FromFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return FileCategory.Other;
        return FromExtension(Path.GetExtension(fileName));
    }
}
