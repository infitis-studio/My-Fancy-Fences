using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using PackIconLucideKind = MahApps.Metro.IconPacks.PackIconLucideKind;

namespace My_Fancy_Fences;

/// <summary>
/// Toast-style window asking the user to support the author. It sits in the bottom-right corner of the
/// primary screen, stays above other windows and never takes the focus from the user's work.
/// </summary>
public partial class SupportReminderWindow : Window
{
    private const double ShadowMargin = 16;
    private const double ScreenMarginX = 16;
    private const double ScreenMarginY = 14;
    private const int CardCornerRadius = 8;
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExNoActivate = 0x08000000;

    private readonly BlurHost _blurHost = new();

    public SupportReminderWindow()
    {
        InitializeComponent();
        SmallIcon.Source = AppIconProvider.Image;
        TitleText.Text = LocalizationService.T("Podoba Ci się My Fancy Fences?");
        MessageText.Text = LocalizationService.T(
"Tworzę ją sam i stale ulepszam. Jeśli ułatwia Ci porządek na pulpicie, możesz postawić mi wirtualną kawę – to najlepsza motywacja do dalszego rozwoju. Dziękuję!");
        NeverRemindCheckBox.Content = LocalizationService.T("Nie przypominaj więcej");
        SupportButtonText.Text = LocalizationService.T("Wesprzyj mnie");
        UpdateLaterButton();

        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
            SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style | WsExToolWindow | WsExNoActivate));
        };
        Loaded += (_, _) => PlaceAndBlur();
        SizeChanged += (_, _) => PlaceAndBlur();
        IsVisibleChanged += (_, _) => PlaceAndBlur();
        Closed += (_, _) => _blurHost.Dispose();
    }

    private bool NeverRemindChecked => NeverRemindCheckBox.IsChecked == true;

    // "Przypomnij później" turns into "Zamknij" once "Nie przypominaj więcej" is ticked.
    private void UpdateLaterButton()
    {
        LaterButtonText.Text = LocalizationService.T(NeverRemindChecked ? "Zamknij" : "Przypomnij później");
        LaterIcon.Kind = NeverRemindChecked ? PackIconLucideKind.X : PackIconLucideKind.Clock;
    }

    private void PlaceAndBlur()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
            return;

        Card.Measure(new Size(Width, double.PositiveInfinity));
        var height = Math.Ceiling(Card.DesiredSize.Height);
        if (Math.Abs(Height - height) > 0.5)
        {
            Height = height;
            return;
        }

        var area = SystemParameters.WorkArea;
        Left = area.Right - ScreenMarginX + ShadowMargin - Width;
        Top = area.Bottom - ScreenMarginY + ShadowMargin - Height;
        _blurHost.Sync(handle, (int)Math.Round(ShadowMargin * MonitorLayoutService.SystemScale), CardCornerRadius, IsVisible);
    }

    private void NeverRemindCheckBox_Changed(object sender, RoutedEventArgs e) => UpdateLaterButton();

    private void SupportButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(SupportReminderService.SupportUrl) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            ApplicationUpdater.AppendLog($"Could not open support page: {exception.Message}");
        }

        // Someone who just supported the author is not reminded again.
        SupportReminderService.NeverRemind();
        Close();
    }

    private void LaterButton_Click(object sender, RoutedEventArgs e)
    {
        if (NeverRemindChecked)
            SupportReminderService.NeverRemind();
        else
            SupportReminderService.RemindLater();
        Close();
    }

    // X is not a decision: the window returns after a day.
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        SupportReminderService.RemindTomorrow();
        Close();
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
}
