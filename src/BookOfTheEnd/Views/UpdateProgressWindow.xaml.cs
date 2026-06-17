using System.Windows;

namespace BookOfTheEnd.Views;

public partial class UpdateProgressWindow : Window
{
    public UpdateProgressWindow()
    {
        InitializeComponent();
    }

    public void SetProgress(double percent)
    {
        Bar.IsIndeterminate = percent <= 0;
        Bar.Value = percent;
        PercentText.Text = percent <= 0 ? "Starting..." : $"{percent:0}%";
    }
}
