using System.Runtime.InteropServices;
using System.Windows;

namespace My_Fancy_Fences;

public sealed record MonitorSnapshot(string DeviceName, Rect Bounds, Rect WorkArea, bool IsPrimary);

/// <summary>
/// Knows which monitors are active and keeps panel positions inside their visible work areas.
/// All rectangles are in WPF device-independent units (the process is system-DPI aware,
/// so one scale factor maps the whole virtual desktop).
/// </summary>
public static class MonitorLayoutService
{
    private const uint MonitorInfoPrimary = 1;

    private static IReadOnlyList<MonitorSnapshot> _monitors = [];
    private static volatile bool _suppressPositionSync;
    private static volatile HashSet<string> _inactiveDevices = new(StringComparer.OrdinalIgnoreCase);
    private static int _probeStarted;

    public static IReadOnlyList<MonitorSnapshot> Monitors
    {
        get
        {
            if (_monitors.Count == 0)
                Refresh();
            return _monitors;
        }
    }

    public static bool IsMultiMonitor => Monitors.Count >= 2;

    /// <summary>
    /// True while the system is reconfiguring displays. Windows moves windows by itself
    /// at that moment, so those positions must not be saved into any profile.
    /// </summary>
    public static bool IsPositionSyncSuppressed
    {
        get => _suppressPositionSync;
        set => _suppressPositionSync = value;
    }

    public static void Refresh()
    {
        var scale = GetSystemScale();
        var list = new List<MonitorSnapshot>();
        EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            (monitor, _, _, _) =>
            {
                var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
                if (GetMonitorInfo(monitor, ref info))
                {
                    list.Add(new MonitorSnapshot(
                        info.DeviceName ?? string.Empty,
                        ToDipRect(info.Monitor, scale),
                        ToDipRect(info.Work, scale),
                        (info.Flags & MonitorInfoPrimary) != 0));
                }

                return true;
            },
            IntPtr.Zero);

        // A monitor switched off with its own button stays attached to the Windows desktop,
        // so it is detected through DDC/CI and treated as disconnected. Never drop every monitor.
        var inactive = _inactiveDevices;
        if (inactive.Count > 0)
        {
            var active = list.Where(monitor => !inactive.Contains(monitor.DeviceName)).ToList();
            if (active.Count > 0 && active.Count < list.Count)
                list = active;
        }

        if (list.Count == 0)
        {
            var work = SystemParameters.WorkArea;
            list.Add(new MonitorSnapshot(
                "PRIMARY",
                new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight),
                work,
                true));
        }

        _monitors = list;
    }

    /// <summary>Starts a background loop that reads each monitor's DDC/CI power state every few seconds.</summary>
    public static void StartPowerProbe()
    {
        if (Interlocked.Exchange(ref _probeStarted, 1) == 1)
            return;

        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    _inactiveDevices = ProbeInactiveDevices();
                }
                catch
                {
                    // DDC/CI is optional; monitors that do not answer are treated as active.
                }

                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        });
    }

    private static HashSet<string> ProbeInactiveDevices()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            (monitor, _, _, _) =>
            {
                var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
                if (!GetMonitorInfo(monitor, ref info) ||
                    !GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out var count) ||
                    count == 0)
                {
                    return true;
                }

                var physical = new PhysicalMonitor[count];
                if (!GetPhysicalMonitorsFromHMONITOR(monitor, count, physical))
                    return true;

                try
                {
                    foreach (var item in physical)
                    {
                        // VCP 0xD6: 1 = on, 2 = standby, 3 = suspend, 4/5 = off.
                        if (GetVCPFeatureAndVCPFeatureReply(item.Handle, 0xD6, out _, out var current, out _) &&
                            current is >= 2 and <= 5)
                        {
                            result.Add(info.DeviceName ?? string.Empty);
                            break;
                        }
                    }
                }
                finally
                {
                    DestroyPhysicalMonitors(count, physical);
                }

                return true;
            },
            IntPtr.Zero);

        return result;
    }

    public static string GetSignature() =>
        string.Join(
            "; ",
            Monitors.Select(monitor =>
                $"{monitor.DeviceName}{(monitor.IsPrimary ? "*" : string.Empty)} " +
                $"{monitor.Bounds.Left:0},{monitor.Bounds.Top:0} {monitor.Bounds.Width:0}x{monitor.Bounds.Height:0}"));

    public static MonitorSnapshot GetPrimary() =>
        Monitors.FirstOrDefault(monitor => monitor.IsPrimary) ?? Monitors[0];

    /// <summary>Monitor that shows the largest part of <paramref name="rect"/>, or null if none does.</summary>
    public static MonitorSnapshot? FindMonitor(Rect rect)
    {
        MonitorSnapshot? best = null;
        var bestArea = 0.0;
        foreach (var monitor in Monitors)
        {
            var overlap = Rect.Intersect(monitor.Bounds, rect);
            if (overlap.IsEmpty)
                continue;

            var area = overlap.Width * overlap.Height;
            if (area > bestArea)
            {
                best = monitor;
                bestArea = area;
            }
        }

        return best;
    }

    /// <summary>
    /// Returns a position that is fully inside the work area of an active monitor.
    /// A panel that is not visible on any monitor is moved to the preferred monitor
    /// (when it is still connected) or to the primary monitor.
    /// </summary>
    public static (double Left, double Top) EnsureVisible(
        double left,
        double top,
        double width,
        double height,
        string? preferredDevice = null)
    {
        if (double.IsNaN(left) || double.IsNaN(top))
            return GetDefaultPosition(width);

        var monitor = FindMonitor(new Rect(left, top, width, height));
        if (monitor is null && !string.IsNullOrWhiteSpace(preferredDevice))
        {
            monitor = Monitors.FirstOrDefault(item =>
                string.Equals(item.DeviceName, preferredDevice, StringComparison.OrdinalIgnoreCase));
        }

        monitor ??= GetPrimary();
        var area = monitor.WorkArea;
        return (
            Math.Clamp(left, area.Left, Math.Max(area.Left, area.Right - width)),
            Math.Clamp(top, area.Top, Math.Max(area.Top, area.Bottom - height)));
    }

    public static (double Left, double Top) GetDefaultPosition(double width)
    {
        var area = GetPrimary().WorkArea;
        return (area.Right - width - 24, area.Top + 24);
    }

    private static double GetSystemScale()
    {
        try
        {
            var dpi = GetDpiForSystem();
            return dpi > 0 ? dpi / 96.0 : 1.0;
        }
        catch
        {
            return 1.0;
        }
    }

    private static Rect ToDipRect(NativeRect rect, double scale) =>
        new(rect.Left / scale, rect.Top / scale, (rect.Right - rect.Left) / scale, (rect.Bottom - rect.Top) / scale);

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("dxva2.dll")]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);

    [DllImport("dxva2.dll")]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] PhysicalMonitor[] monitors);

    [DllImport("dxva2.dll")]
    private static extern bool GetVCPFeatureAndVCPFeatureReply(
        IntPtr monitor,
        byte code,
        out uint type,
        out uint current,
        out uint maximum);

    [DllImport("dxva2.dll")]
    private static extern bool DestroyPhysicalMonitors(uint count, PhysicalMonitor[] monitors);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PhysicalMonitor
    {
        public IntPtr Handle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string? Description;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string? DeviceName;
    }
}
