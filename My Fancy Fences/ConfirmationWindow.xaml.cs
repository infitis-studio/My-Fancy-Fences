using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MahApps.Metro.IconPacks;

namespace My_Fancy_Fences;

public partial class ConfirmationWindow : Window
{
    public ConfirmationWindow(
        string title,
        string message,
        string confirmText,
        string? cancelText = null,
        bool positiveConfirm = false)
    {
        InitializeComponent();
        Icon = AppIconProvider.Image;
        Title = title;
        DialogTitleText.Text = title;
        DialogMessageText.Text = message;
        ConfirmButton.Content = confirmText;
        Height = Math.Clamp(220 + message.Length * 0.35, 240, 340);
        if (!string.IsNullOrWhiteSpace(cancelText))
            CancelButton.Content = cancelText;

        if (positiveConfirm)
        {
            Width = 440;
            // Header (46) + footer (58) + margins (36) plus one 20px line per ~52 characters of the message.
            MinHeight = 170;
            var lines = Math.Max(1, message.Split('\n').Sum(line => Math.Ceiling(line.Length / 52.0)));
            Height = Math.Clamp(146 + 20 * lines, 190, 300);
            DialogIcon.Kind = confirmText.Contains("Dodaj", StringComparison.CurrentCultureIgnoreCase)
                ? PackIconLucideKind.Plus
                : PackIconLucideKind.Download;
            DialogIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x82, 0xD3, 0xA3));
            HeaderBorder.Background = new LinearGradientBrush(
                Color.FromRgb(0x20, 0x35, 0x29),
                Color.FromRgb(0x17, 0x1B, 0x19),
                0);
            ConfirmButton.Style = (Style)FindResource("PositiveConfirmButtonStyle");
        }
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}
