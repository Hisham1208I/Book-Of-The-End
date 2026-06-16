using System.Windows;

namespace BookOfTheEnd.Services;

public enum AppTheme { Light, Dark }

/// <summary>
/// Swaps the active theme ResourceDictionary at runtime. The theme dictionary is
/// always kept at index 0 of the application's merged dictionaries; shared control
/// styles (index 1+) reference its brushes via DynamicResource so they update live.
/// </summary>
public sealed class ThemeService
{
    public AppTheme Current { get; private set; } = AppTheme.Light;

    public void Apply(AppTheme theme)
    {
        Current = theme;
        var dicts = Application.Current.Resources.MergedDictionaries;
        var themeDict = new ResourceDictionary
        {
            Source = new Uri(
                theme == AppTheme.Light
                    ? "pack://application:,,,/Themes/Light.xaml"
                    : "pack://application:,,,/Themes/Dark.xaml",
                UriKind.Absolute)
        };

        if (dicts.Count == 0)
            dicts.Add(themeDict);
        else
            dicts[0] = themeDict;
    }

    public AppTheme Toggle()
    {
        Apply(Current == AppTheme.Light ? AppTheme.Dark : AppTheme.Light);
        return Current;
    }
}
