using System.Windows;

namespace BookOfTheEnd.Views;

public enum AppDialogKind
{
    Info,
    Success,
    Warning
}

public partial class AppDialogWindow : Window
{
    private bool _confirmed;

    public AppDialogWindow()
    {
        InitializeComponent();
    }

    public static void ShowMessage(
        Window? owner,
        string title,
        string message,
        string? details = null,
        AppDialogKind kind = AppDialogKind.Info,
        string okText = "OK")
    {
        var dialog = Create(owner, title, message, details, kind, confirm: false, okText: okText, cancelText: null);
        dialog.ShowDialog();
    }

    public static bool ShowConfirm(
        Window? owner,
        string title,
        string message,
        string? details = null,
        string confirmText = "Continue",
        string cancelText = "Cancel")
    {
        var dialog = Create(owner, title, message, details, AppDialogKind.Warning, confirm: true,
            okText: confirmText, cancelText: cancelText);
        dialog.ShowDialog();
        return dialog._confirmed;
    }

    private static AppDialogWindow Create(
        Window? owner,
        string title,
        string message,
        string? details,
        AppDialogKind kind,
        bool confirm,
        string okText,
        string? cancelText)
    {
        var dialog = new AppDialogWindow { Owner = owner };
        dialog.Apply(title, message, details, kind, confirm, okText, cancelText);
        return dialog;
    }

    private void Apply(
        string title,
        string message,
        string? details,
        AppDialogKind kind,
        bool confirm,
        string okText,
        string? cancelText)
    {
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;

        if (!string.IsNullOrWhiteSpace(details))
        {
            DetailsText.Text = details;
            DetailsCard.Visibility = Visibility.Visible;
        }

        OkButton.Content = okText;
        CancelButton.Visibility = confirm ? Visibility.Visible : Visibility.Collapsed;
        if (confirm && !string.IsNullOrWhiteSpace(cancelText))
            CancelButton.Content = cancelText;

        switch (kind)
        {
            case AppDialogKind.Success:
                IconGlyph.Text = "\uE73E";
                SetIconColors("SuccessBrush", "AccentMutedBrush");
                break;
            case AppDialogKind.Warning:
                IconGlyph.Text = "\uE7BA";
                SetIconColors("WarningBrush", "SurfaceAltBrush");
                break;
            default:
                IconGlyph.Text = "\uE946";
                SetIconColors("AccentBrush", "AccentMutedBrush");
                break;
        }
    }

    private void SetIconColors(string glyphKey, string badgeKey)
    {
        IconGlyph.SetResourceReference(ForegroundProperty, glyphKey);
        IconBadge.SetResourceReference(BackgroundProperty, badgeKey);
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        _confirmed = true;
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        _confirmed = false;
        DialogResult = false;
        Close();
    }
}
