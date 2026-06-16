using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using BookOfTheEnd.Models;

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
            RecoveryStatus.Recoverable => "SuccessBrush",
            RecoveryStatus.Recovered => "AccentBrush",
            RecoveryStatus.PartialMetadata => "WarningBrush",
            RecoveryStatus.Overwritten => "WarningBrush",
            RecoveryStatus.Failed => "DangerBrush",
            _ => "TextSecondaryBrush"
        } : "TextSecondaryBrush";
        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }
    public object ConvertBack(object value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}
