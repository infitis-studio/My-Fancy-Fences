using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace My_Fancy_Fences;

/// <summary>
/// Toast-style window announcing a newer release. It appears in the bottom-right corner of the primary
/// screen, stays above other windows and never takes the focus from the user's work.
/// </summary>
public partial class UpdateNoticeWindow : Window
{
    private const double ShadowMargin = 16;
    private const double ScreenMarginX = 16;
    private const double ScreenMarginY = 14;
    private const int CardCornerRadius = 8;
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExNoActivate = 0x08000000;
    private const string ReleasesUrl = "https://github.com/infitis-studio/My-Fancy-Fences/releases";

    private readonly UpdateCheckResult _update;
    private readonly BlurHost _blurHost = new();
    private bool _updating;

    public UpdateNoticeWindow(UpdateCheckResult update)
    {
        _update = update;
        InitializeComponent();
        SmallIcon.Source = AppIconProvider.Image;

        var installed = FormatVersion(UpdateService.CurrentVersion);
        var available = update.LatestVersion is null ? update.LatestTag.TrimStart('v', 'V') : FormatVersion(update.LatestVersion);
        TitleText.Text = LocalizationService.T("Dostępna jest nowa wersja programu.");
        MessageText.Text = string.Format(
            LocalizationService.T("Czy chcesz zaktualizować program z wersji {0} do {1}?"),
            installed,
            available);
        UpdateButtonText.Text = LocalizationService.T("Aktualizuj");
        LaterButtonText.Text = LocalizationService.T("Przypomnij później");
        ChangelogButton.Content = LocalizationService.T("Zobacz changelog");

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

    // The version shown here is the same value the update check compares: major.minor.patch.
    private static string FormatVersion(Version version) => UpdateService.FormatVersion(version).TrimStart('v');

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

        var scale = MonitorLayoutService.SystemScale;
        _blurHost.Sync(handle, (int)Math.Round(ShadowMargin * scale), CardCornerRadius, IsVisible);
    }

    /// <summary>Shows a status line and grows the window so the whole text stays visible.</summary>
    private void SetStatus(string text)
    {
        StatusText.Text = text;
        StatusText.Visibility = Visibility.Visible;
        PlaceAndBlur();
    }

    private void LaterButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_updating)
            Close();
    }

    private void ChangelogButton_Click(object sender, RoutedEventArgs e)
    {
        // The release page shows the release notes; fall back to the list of releases when it is unknown.
        var url = string.IsNullOrWhiteSpace(_update.ReleaseUrl) ? ReleasesUrl : _update.ReleaseUrl;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            ApplicationUpdater.AppendLog($"Could not open changelog {url}: {exception.Message}");
        }
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_updating)
            return;

        _updating = true;
        UpdateButton.IsEnabled = false;
        LaterButton.IsEnabled = false;
        SetStatus(LocalizationService.T("Pobieranie aktualizacji…"));
        try
        {
            var progress = new Progress<double>(value =>
                SetStatus($"{LocalizationService.T("Pobieranie aktualizacji…")} {value:P0}"));
            await ApplicationUpdater.PrepareUpdateAsync(_update, progress);
            SetStatus(LocalizationService.T("Instalowanie aktualizacji…"));
            Application.Current.Shutdown();
        }
        catch (Exception exception)
        {
            ApplicationUpdater.AppendLog($"Update from notice failed: {exception}");
            _updating = false;
            UpdateButton.IsEnabled = true;
            LaterButton.IsEnabled = true;
            SetStatus($"{LocalizationService.T("Nie udało się zainstalować aktualizacji.")} {exception.Message}");
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
}
