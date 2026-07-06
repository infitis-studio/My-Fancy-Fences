using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace My_Fancy_Fences;

public partial class CreatorWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private IntPtr _windowHandle;
    private bool _isDragging;
    private Point _dragStart;
    private double _startLeft;
    private double _startTop;

    public bool IsHeaderHidden { get; private set; }
    public Color BackgroundColor { get; private set; }
    public double BackgroundOpacity { get; private set; }
    public double IconSize { get; private set; }
    public event EventHandler? CreatorStateChanged;
    public event EventHandler? NewPanelRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler<GlobalAppearanceEventArgs>? GlobalAppearanceChanged;

    public CreatorWindow(
        bool hideHeader,
        double width,
        double height,
        double? left,
        double? top,
        Color backgroundColor,
        double backgroundOpacity)
    {
        InitializeComponent();
        Icon = AppIconProvider.Image;
        Width = 360;
        Height = 160;
        IsHeaderHidden = hideHeader;
        BackgroundColor = backgroundColor;
        BackgroundOpacity = backgroundOpacity;
        IconSize = 30;
        Resources["CreatorIconSize"] = IconSize;
        ApplyBackground(backgroundColor, backgroundOpacity);

        SourceInitialized += (_, _) =>
        {
            _windowHandle = new WindowInteropHelper(this).Handle;
            var style = GetWindowLongPtr(_windowHandle, GwlExStyle).ToInt64();
            SetWindowLongPtr(_windowHandle, GwlExStyle, new IntPtr(style | WsExToolWindow | WsExNoActivate));
        };

        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = left.HasValue
                ? Math.Clamp(left.Value, area.Left, Math.Max(area.Left, area.Right - Width))
                : area.Right - Width - 24;
            Top = top.HasValue
                ? Math.Clamp(top.Value, area.Top, Math.Max(area.Top, area.Bottom - Height))
                : area.Top + 24;
            ApplyHeader();
            _ = StabilizeDesktopLevelAsync();
        };
    }

    private void DragSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _isDragging = false;
            if (DragSurface.IsMouseCaptured)
                DragSurface.ReleaseMouseCapture();

            var settings = new CreatorPanelSettingsWindow(
                IsHeaderHidden,
                BackgroundColor,
                BackgroundOpacity);
            settings.Loaded += (_, _) => BringWindowToFront(settings);
            settings.PreviewChanged += (_, preview) =>
            {
                IsHeaderHidden = preview.HideHeader;
                ApplyBackground(preview.BackgroundColor, preview.BackgroundOpacity);
                ApplyHeader();
            };

            var original = IsHeaderHidden;
            var originalColor = BackgroundColor;
            var originalOpacity = BackgroundOpacity;
            if (settings.ShowDialog() != true)
            {
                IsHeaderHidden = original;
                ApplyBackground(originalColor, originalOpacity);
            }
            else
            {
                IsHeaderHidden = settings.HideHeader;
                ApplyBackground(settings.BackgroundColor, settings.BackgroundOpacity);
            }

            ApplyHeader();
            if (!IsVisible)
                Show();
            SendToDesktopLevel();
            CreatorStateChanged?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        _isDragging = true;
        _dragStart = PointToScreen(e.GetPosition(this));
        _startLeft = Left;
        _startTop = Top;
        DragSurface.CaptureMouse();
        e.Handled = true;
    }

    private void DragSurface_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || e.LeftButton != MouseButtonState.Pressed)
            return;

        var current = PointToScreen(e.GetPosition(this));
        Left = _startLeft + current.X - _dragStart.X;
        Top = _startTop + current.Y - _dragStart.Y;
    }

    private void DragSurface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging)
            return;

        _isDragging = false;
        DragSurface.ReleaseMouseCapture();
        SendToDesktopLevel();
        CreatorStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyHeader()
    {
        HeaderContent.Visibility = IsHeaderHidden ? Visibility.Collapsed : Visibility.Visible;
        HeaderRow.Height = new GridLength(IsHeaderHidden ? 10 : 48);
    }

    private void ApplyBackground(Color color, double opacity)
    {
        BackgroundColor = color;
        BackgroundOpacity = opacity;
        FenceBorder.Background = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Round(opacity * 255),
            color.R,
            color.G,
            color.B));
    }

    private void ApplyIconSize(double iconSize)
    {
        IconSize = Math.Clamp(iconSize, 22, 42);
        Resources["CreatorIconSize"] = IconSize;
    }

    private void AppearanceButton_Click(object sender, RoutedEventArgs e)
    {
        _isDragging = false;
        if (DragSurface.IsMouseCaptured)
            DragSurface.ReleaseMouseCapture();

        var originalHeader = IsHeaderHidden;
        var originalColor = BackgroundColor;
        var originalOpacity = BackgroundOpacity;
        var settings = new CreatorSettingsWindow(
            IsHeaderHidden,
            BackgroundColor,
            BackgroundOpacity);
        settings.Loaded += (_, _) => BringWindowToFront(settings);

        settings.PreviewChanged += (_, preview) =>
        {
            IsHeaderHidden = preview.ApplyHeaderToAll
                ? preview.HideHeader
                : originalHeader;
            ApplyHeader();
            ApplyBackground(
                preview.ApplyColorToAll ? preview.BackgroundColor : originalColor,
                preview.ApplyColorToAll ? preview.BackgroundOpacity : originalOpacity);
            RaiseGlobalAppearance(GlobalAppearancePhase.Preview, preview);
        };

        if (settings.ShowDialog() != true)
        {
            IsHeaderHidden = originalHeader;
            ApplyHeader();
            ApplyBackground(originalColor, originalOpacity);
            GlobalAppearanceChanged?.Invoke(this, new GlobalAppearanceEventArgs(
                GlobalAppearancePhase.Cancel,
                IsHeaderHidden,
                originalColor,
                originalOpacity,
                false,
                false));
        }
        else
        {
            IsHeaderHidden = settings.ApplyHeaderToAll
                ? settings.HideHeader
                : originalHeader;
            ApplyHeader();
            ApplyBackground(
                settings.ApplyColorToAll ? settings.BackgroundColor : originalColor,
                settings.ApplyColorToAll ? settings.BackgroundOpacity : originalOpacity);
            GlobalAppearanceChanged?.Invoke(this, new GlobalAppearanceEventArgs(
                GlobalAppearancePhase.Commit,
                settings.HideHeader,
                settings.BackgroundColor,
                settings.BackgroundOpacity,
                settings.ApplyHeaderToAll,
                settings.ApplyColorToAll));
        }

        CreatorStateChanged?.Invoke(this, EventArgs.Empty);
        if (!IsVisible)
            Show();
        SendToDesktopLevel();
        e.Handled = true;
    }

    private void WallpaperButton_Click(object sender, RoutedEventArgs e)
    {
        var wallpaperWindow = new WallpaperWindow();
        wallpaperWindow.Show();
        BringWindowToFront(wallpaperWindow);
        SendToDesktopLevel();
        e.Handled = true;
    }

    private void NewPanelButton_Click(object sender, RoutedEventArgs e)
    {
        NewPanelRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private static void BringWindowToFront(Window window)
    {
        window.Topmost = true;
        window.Topmost = false;
        window.Activate();
        window.Focus();
    }

    private void RaiseGlobalAppearance(
        GlobalAppearancePhase phase,
        CreatorSettingsPreviewEventArgs preview) =>
        GlobalAppearanceChanged?.Invoke(this, new GlobalAppearanceEventArgs(
            phase,
            preview.HideHeader,
            preview.BackgroundColor,
            preview.BackgroundOpacity,
            preview.ApplyHeaderToAll,
            preview.ApplyColorToAll));

    private void SendToDesktopLevel()
        => MainWindow.SendWindowToDesktopLevel(_windowHandle);

    internal async Task StabilizeDesktopLevelAsync()
    {
        var delays = new[] { 0, 120, 450, 1200, 3000 };
        foreach (var delay in delays)
        {
            if (delay > 0)
                await Task.Delay(delay);

            if (!IsLoaded || !IsVisible)
                return;

            SendToDesktopLevel();
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr newLong);

}

public enum GlobalAppearancePhase
{
    Preview,
    Commit,
    Cancel
}

public sealed record GlobalAppearanceEventArgs(
    GlobalAppearancePhase Phase,
    bool HideHeader,
    Color BackgroundColor,
    double BackgroundOpacity,
    bool ApplyHeaderToAll,
    bool ApplyColorToAll);
