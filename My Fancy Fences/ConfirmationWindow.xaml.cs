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
            Height = Math.Max(260, Height);
            DialogIcon.Kind = PackIconLucideKind.Download;
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
