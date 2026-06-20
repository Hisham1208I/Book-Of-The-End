using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using BookOfTheEnd.Models;
using BookOfTheEnd.Models.Health;

namespace BookOfTheEnd.Converters;

public sealed class BytesToHumanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is long l ? HumanSize.Format(l) : value is int i ? HumanSize.Format(i) : "-";
    public object ConvertBack(object value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

public sealed class NullableDateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is DateTime dt ? dt.ToString("yyyy-MM-dd HH:mm") : "—";
    public object ConvertBack(object value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool b = value is bool v && v;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool hasValue = value is not null;
        if (Invert) hasValue = !hasValue;
        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Maps a string parameter to Visibility by equality with the bound value's ToString().</summary>
public sealed class EqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

public sealed class CategoryToGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is FileCategory c ? c switch
        {
            FileCategory.Image => "\U0001F5BC",
            FileCategory.Video => "\U0001F3AC",
            FileCategory.Audio => "\U0001F3B5",
            FileCategory.Document => "\U0001F4C4",
            FileCategory.Archive => "\U0001F5DC",
            _ => "\U0001F4E6"
        } : "\U0001F4E6";
    public object ConvertBack(object value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

public sealed class QualityToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = value is RecoveryQuality q ? q switch
        {
            RecoveryQuality.Excellent => "SuccessBrush",
            RecoveryQuality.Good => "SuccessBrush",
            RecoveryQuality.Fair => "WarningBrush",
            RecoveryQuality.Poor => "DangerBrush",
            _ => "TextSecondaryBrush"
        } : "TextSecondaryBrush";
        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }
    public object ConvertBack(object value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

public sealed class DriveStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = value is DriveStatus s ? s switch
        {
            DriveStatus.Online => "StatusOnlineBrush",
            DriveStatus.Formatted => "StatusWarnBrush",
            _ => "StatusOfflineBrush"
        } : "StatusOfflineBrush";
        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }
    public object ConvertBack(object value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

public sealed class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = value is RecoveryStatus s ? s switch
        {
            RecoveryStatus.Recoverable     => "SuccessBrush",
            RecoveryStatus.Recovered       => "AccentBrush",
            RecoveryStatus.Verified        => "SuccessBrush",
            RecoveryStatus.PartialMetadata => "WarningBrush",
            RecoveryStatus.Overwritten     => "WarningBrush",
            RecoveryStatus.Failed          => "DangerBrush",
            RecoveryStatus.Corrupt         => "DangerBrush",
            _ => "TextSecondaryBrush"
        } : "TextSecondaryBrush";
        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }
    public object ConvertBack(object value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

public sealed class HealthStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = value is HealthStatus s ? s switch
        {
            HealthStatus.Excellent => "SuccessBrush",
            HealthStatus.Good => "SuccessBrush",
            HealthStatus.Warning => "WarningBrush",
            HealthStatus.Critical => "DangerBrush",
            HealthStatus.Failing => "DangerBrush",
            _ => "TextSecondaryBrush"
        } : "TextSecondaryBrush";
        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }
    public object ConvertBack(object value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

public sealed class ClonePriorityToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = value is ClonePriority p ? p switch
        {
            ClonePriority.Low => "SuccessBrush",
            ClonePriority.Moderate => "WarningBrush",
            ClonePriority.High => "DangerBrush",
            ClonePriority.Emergency => "DangerBrush",
            _ => "TextSecondaryBrush"
        } : "TextSecondaryBrush";
        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }
    public object ConvertBack(object value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

public sealed class SectorStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = value is SectorStatus s ? s switch
        {
            SectorStatus.Healthy => "SuccessBrush",
            SectorStatus.Slow => "WarningBrush",
            SectorStatus.Bad => "DangerBrush",
            _ => "BorderBrush"
        } : "BorderBrush";
        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }
    public object ConvertBack(object value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Hides a recovery-readiness banner when the drive is ready (RecoveryReady == true).</summary>
public sealed class ReadinessToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is RecoveryReadiness r && !r.RecoveryReady ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Maps FileCategory? (null = All) to/from a ComboBox SelectedIndex.</summary>
public sealed class CategoryIndexConverter : IValueConverter
{
    private static readonly FileCategory?[] Map =
        [null, FileCategory.Image, FileCategory.Video, FileCategory.Audio, FileCategory.Document, FileCategory.Archive];

    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        var cat = value as FileCategory?;
        int idx = Array.IndexOf(Map, cat);
        return idx < 0 ? 0 : idx;
    }

    public object? ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        int idx = value is int i ? i : 0;
        return idx >= 0 && idx < Map.Length ? (object?)Map[idx] : null;
    }
}

/// <summary>Maps a minimum-size long (0, 100K, 1M, 10M, 100M, 1G) to/from a ComboBox SelectedIndex.</summary>
public sealed class SizeIndexConverter : IValueConverter
{
    private static readonly long[] Map =
        [0L, 100L * 1024, 1L * 1024 * 1024, 10L * 1024 * 1024, 100L * 1024 * 1024, 1024L * 1024 * 1024];

    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        long size = value is long l ? l : 0L;
        int idx = Array.IndexOf(Map, size);
        return idx < 0 ? 0 : idx;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        int idx = value is int i ? i : 0;
        return idx >= 0 && idx < Map.Length ? Map[idx] : 0L;
    }
}
