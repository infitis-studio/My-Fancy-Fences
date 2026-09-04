using System.Runtime.InteropServices;
using System.Text;

namespace My_Fancy_Fences;

public static class DesktopWorkerWindowFinder
{
    private const uint SpawnWorkerWMessage = 0x052C;

    public static IReadOnlyList<DesktopWallpaperTarget> FindTargets()
    {
        var targets = new List<DesktopWallpaperTarget>();
        var progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
            return targets;

        SpawnWorkerW(progman);

        var shellViewHost = IntPtr.Zero;
        var shellViewHandle = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            var shellView = FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (shellView == IntPtr.Zero)
                return true;

            shellViewHost = window;
            shellViewHandle = shellView;
            if (window == progman)
                targets.Add(new DesktopWallpaperTarget(progman, shellView, DesktopWallpaperTargetKind.ProgmanBehindIcons));

            var workerBehindShellViewHost = FindWindowEx(IntPtr.Zero, window, "WorkerW", null);
            if (workerBehindShellViewHost != IntPtr.Zero)
                targets.Add(new DesktopWallpaperTarget(workerBehindShellViewHost, IntPtr.Zero, DesktopWallpaperTargetKind.WorkerW));

            return false;
        }, IntPtr.Zero);

        EnumWindows((window, _) =>
        {
            if (window == shellViewHost)
                return true;

            if (GetClassName(window) == "WorkerW" &&
                !ContainsShellView(window) &&
                targets.All(target => target.Handle != window))
            {
                targets.Add(new DesktopWallpaperTarget(window, IntPtr.Zero, DesktopWallpaperTargetKind.WorkerW));
            }

            return true;
        }, IntPtr.Zero);

        if (shellViewHandle != IntPtr.Zero &&
            targets.All(target => target.Kind != DesktopWallpaperTargetKind.ProgmanBehindIcons))
        {
            targets.Add(new DesktopWallpaperTarget(progman, shellViewHandle, DesktopWallpaperTargetKind.ProgmanBehindIcons));
        }

        targets.Add(new DesktopWallpaperTarget(progman, IntPtr.Zero, DesktopWallpaperTargetKind.Progman));
        return targets;
    }

    private static void SpawnWorkerW(IntPtr progman)
    {
        SendMessageTimeout(
            progman,
            SpawnWorkerWMessage,
            IntPtr.Zero,
            IntPtr.Zero,
            SendMessageTimeoutFlags.Normal,
            1000,
            out _);

        SendMessageTimeout(
            progman,
            SpawnWorkerWMessage,
            new IntPtr(0xD),
            IntPtr.Zero,
            SendMessageTimeoutFlags.Normal,
            1000,
            out _);

        SendMessageTimeout(
            progman,
            SpawnWorkerWMessage,
            new IntPtr(0xD),
            new IntPtr(1),
            SendMessageTimeoutFlags.Normal,
            1000,
            out _);
    }

    private static bool ContainsShellView(IntPtr window) =>
        FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero;

    private static string GetClassName(IntPtr window)
    {
        var buffer = new StringBuilder(256);
        var length = GetClassName(window, buffer, buffer.Capacity);
        return length <= 0
            ? string.Empty
            : buffer.ToString();
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindowEx(
        IntPtr parent,
        IntPtr childAfter,
        string className,
        string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int maxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr window,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        SendMessageTimeoutFlags flags,
        uint timeout,
        out IntPtr result);

    private enum SendMessageTimeoutFlags : uint
    {
        Normal = 0x0000
    }
}

public readonly record struct DesktopWallpaperTarget(
    IntPtr Handle,
    IntPtr InsertAfter,
    DesktopWallpaperTargetKind Kind);

public enum DesktopWallpaperTargetKind
{
    WorkerW,
    ProgmanBehindIcons,
    Progman
}
