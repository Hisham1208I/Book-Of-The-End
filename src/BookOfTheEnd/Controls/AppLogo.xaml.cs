using System.Windows;
using System.Windows.Controls;

namespace BookOfTheEnd.Controls;

public partial class AppLogo : UserControl
{
    public static readonly DependencyProperty LogoSizeProperty =
        DependencyProperty.Register(nameof(LogoSize), typeof(double), typeof(AppLogo), new PropertyMetadata(18.0));

    public double LogoSize
    {
        get => (double)GetValue(LogoSizeProperty);
        set => SetValue(LogoSizeProperty, value);
    }

    public AppLogo() => InitializeComponent();
}
