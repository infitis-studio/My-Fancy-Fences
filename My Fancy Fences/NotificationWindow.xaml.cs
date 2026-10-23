using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;

namespace My_Fancy_Fences;

/// <summary>
/// Own notification window styled after a Windows 11 toast. It appears in the bottom-right corner of the
/// primary screen above the taskbar, slides in from the right and stays until its close button is clicked.
/// </summary>
public partial class NotificationWindow : Window
{
    // The window is the 364 x 128 card plus a transparent margin that holds the shadow.
    private const double ShadowMargin = 16;
    private const double ScreenMarginX = 16;
    private const double ScreenMarginY = 14;
    private const double SlideDistance = 70;
    private const int CardCornerRadius = 8;
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExNoActivate = 0x08000000;

    private readonly BlurHost _blurHost = new();
    private bool _closing;

    public NotificationWindow()
    {
        InitializeComponent();
        SmallIcon.Source = AppIconProvider.Image;

        SourceInitialized += (_, _) =>
        {
            // Never take the focus from the user's window and stay out of Alt+Tab.
            var handle = new WindowInteropHelper(this).Handle;
            var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
            SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style | WsExToolWindow | WsExNoActivate));
        };
        LocationChanged += (_, _) => KeepOnPrimaryMonitor();
        Closed += (_, _) => _blurHost.Dispose();
    }

    public void SetContent(string title, string message)
    {
        TitleText.Text = title;
        MessageText.Text = message;
    }

    /// <summary>Shows the window if it is hidden and slides it out if it is visible.</summary>
    public void ToggleVisibility()
    {
        if (IsVisible && !_closing)
            SlideOut();
        else
            SlideIn();
    }

    private (double Left, double Top) RestingPosition()
    {
        var area = SystemParameters.WorkArea;
        return (
            area.Right - ScreenMarginX + ShadowMargin - Width,
            area.Bottom - ScreenMarginY + ShadowMargin - Height);
    }

    /// <summary>The height follows the content, so the margins around the text stay exactly as designed.</summary>
    private void FitHeightToContent()
    {
        Card.Measure(new Size(Width, double.PositiveInfinity));
        Height = Math.Ceiling(Card.DesiredSize.Height);
    }

    private void SlideIn()
    {
        _closing = false;
        FitHeightToContent();
        var (left, top) = RestingPosition();
        BeginAnimation(LeftProperty, null);
        Top = top;
        Left = left + SlideDistance;
        if (!IsVisible)
            Show();

        BeginAnimation(LeftProperty, new DoubleAnimation(left + SlideDistance, left, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        KeepOnPrimaryMonitor();
    }

    private void SlideOut()
    {
        _closing = true;
        var (left, _) = RestingPosition();
        var animation = new DoubleAnimation(left, left + SlideDistance, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        animation.Completed += (_, _) =>
        {
            if (!_closing)
                return;

            BeginAnimation(LeftProperty, null);
            Hide();
            _closing = false;
            KeepOnPrimaryMonitor();
        };
        BeginAnimation(LeftProperty, animation);
    }

    private void UnderstoodButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_closing)
            SlideOut();
    }

    /// <summary>
    /// The slide animation moves the window past the screen edge, which on a multi-monitor desktop would
    /// show it on the neighbouring monitor. Only the part inside the primary monitor stays visible: the
    /// window is clipped with a region and the blur window under it is cut to the same rectangle.
    /// </summary>
    private void KeepOnPrimaryMonitor()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
            return;

        var scale = MonitorLayoutService.SystemScale;
        var monitor = MonitorLayoutService.GetPrimary().Bounds;
        var visibleLeft = Math.Max(Left, monitor.Left);
        var visibleTop = Math.Max(Top, monitor.Top);
        var visibleRight = Math.Min(Left + Width, monitor.Right);
        var visibleBottom = Math.Min(Top + Height, monitor.Bottom);
        var fullyInside = visibleLeft <= Left && visibleTop <= Top &&
                          visibleRight >= Left + Width && visibleBottom >= Top + Height;

        if (fullyInside)
        {
            SetWindowRgn(handle, IntPtr.Zero, true);
        }
        else
        {
            var region = CreateRectRgn(
                (int)Math.Round((visibleLeft - Left) * scale),
                (int)Math.Round((visibleTop - Top) * scale),
                (int)Math.Round((Math.Max(visibleLeft, visibleRight) - Left) * scale),
                (int)Math.Round((Math.Max(visibleTop, visibleBottom) - Top) * scale));
            if (SetWindowRgn(handle, region, true) == 0)
                DeleteObject(region);
        }

        var clip = new Int32Rect(
            (int)Math.Round(monitor.Left * scale),
            (int)Math.Round(monitor.Top * scale),
            (int)Math.Round(monitor.Width * scale),
            (int)Math.Round(monitor.Height * scale));
        _blurHost.Sync(handle, (int)Math.Round(ShadowMargin * scale), CardCornerRadius, IsVisible, clip);
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);
}
