using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace My_Fancy_Fences;

/// <summary>
/// A bare, click-through window that sits directly under a panel and carries the Windows
/// blur-behind effect. The compositor draws the blur, so it follows an animated wallpaper at no cost
/// to the application, but its strength is fixed. It is a separate window because the effect always
/// fills the whole window of a transparent (layered) panel, including its transparent margin. A plain
/// popup window can cover exactly the panel's rectangle, and Windows 11 rounds its corners.
/// </summary>
internal sealed class BlurHost : IDisposable
{
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExTransparent = 0x00000020;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpHideWindow = 0x0080;
    private const int AccentEnableBlurBehind = 3;
    private const int WcaAccentPolicy = 19;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int WmNcHitTest = 0x0084;
    private const int WmMouseActivate = 0x0021;
    private const int HtTransparent = -1;
    private const int MaNoActivate = 3;

    private HwndSource? _source;
    private int _cornerPreference = -1;

    /// <summary>
    /// Covers the panel's rounded rectangle: the panel window shrunk by <paramref name="insetPx"/> on
    /// every side. Placed directly below <paramref name="panelWindow"/> in the z-order.
    /// </summary>
    public void Sync(IntPtr panelWindow, int insetPx, int radiusDip, bool visible, Int32Rect? clip = null)
    {
        if (panelWindow == IntPtr.Zero || !GetWindowRect(panelWindow, out var rect))
            return;

        var x = rect.Left + insetPx;
        var y = rect.Top + insetPx;
        var width = Math.Max(1, rect.Right - rect.Left - insetPx * 2);
        var height = Math.Max(1, rect.Bottom - rect.Top - insetPx * 2);

        // Optional clip (for example one monitor): only the part inside it gets blurred.
        if (clip is Int32Rect bounds)
        {
            var left = Math.Max(x, bounds.X);
            var top = Math.Max(y, bounds.Y);
            var right = Math.Min(x + width, bounds.X + bounds.Width);
            var bottom = Math.Min(y + height, bounds.Y + bounds.Height);
            if (right <= left || bottom <= top)
                visible = false;
            else
                (x, y, width, height) = (left, top, right - left, bottom - top);
        }

        if (!visible)
        {
            if (_source is not null)
                SetWindowPos(_source.Handle, IntPtr.Zero, 0, 0, 0, 0, SwpNoActivate | SwpHideWindow | 0x0001 | 0x0002 | 0x0004);
            return;
        }

        EnsureCreated(x, y, width, height);
        var handle = _source!.Handle;
        SetWindowPos(handle, panelWindow, x, y, width, height, SwpNoActivate | SwpShowWindow);

        // Windows 11 offers square, 4 px and 8 px corners; the panel's radius (0, 4 or 8 DIP) picks one.
        var preference = radiusDip <= 0 ? 1 : radiusDip <= 5 ? 3 : 2;
        if (preference != _cornerPreference)
        {
            _cornerPreference = preference;
            DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref preference, sizeof(int));
        }
    }

    private void EnsureCreated(int x, int y, int width, int height)
    {
        if (_source is not null)
            return;

        _source = new HwndSource(new HwndSourceParameters("MyFancyFences.BlurHost")
        {
            PositionX = x,
            PositionY = y,
            Width = width,
            Height = height,
            WindowStyle = WsPopup,
            ExtendedWindowStyle = WsExToolWindow | WsExNoActivate | WsExTransparent
        });
        _source.CompositionTarget.BackgroundColor = Colors.Transparent;
        _source.AddHook(HostProc);

        var noBorder = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE: no thin system border around the host
        DwmSetWindowAttribute(_source.Handle, DwmwaBorderColor, ref noBorder, sizeof(int));

        var policy = new AccentPolicy { AccentState = AccentEnableBlurBehind };
        var size = Marshal.SizeOf(policy);
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, buffer, false);
            var data = new CompositionAttributeData { Attribute = WcaAccentPolicy, Data = buffer, SizeOfData = size };
            SetWindowCompositionAttribute(_source.Handle, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Mouse clicks must go through to the panel and the desktop, and never raise this window.</summary>
    private static IntPtr HostProc(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmNcHitTest)
        {
            handled = true;
            return new IntPtr(HtTransparent);
        }

        if (message == WmMouseActivate)
        {
            handled = true;
            return new IntPtr(MaNoActivate);
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        _source?.Dispose();
        _source = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr window, ref CompositionAttributeData data);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
