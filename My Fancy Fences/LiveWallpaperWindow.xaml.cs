using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;

namespace My_Fancy_Fences;

public partial class LiveWallpaperWindow : Window
{
    private static readonly IntPtr HwndBottom = new(1);
    private static readonly IntPtr HwndTop = new(0);
    private const string LocalWallpaperHost = "my-fancy-fences-live.local";

    private const int SwpNoActivate = 0x0010;
    private const int SwpNoZOrder = 0x0004;
    private const int SwpShowWindow = 0x0040;
    private const int SwpNoOwnerZOrder = 0x0200;
    private const int SwShowNoActivate = 4;
    private const int GwlStyle = -16;
    private const long WsChild = 0x40000000L;
    private const long WsPopup = 0x80000000L;
    private const long WsVisible = 0x10000000L;

    private readonly Uri _videoUri;
    private readonly string _logPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "My Fancy Fences",
        "live-wallpaper.log");
    private bool _isAttached;
    private DesktopWallpaperTargetKind _attachedTargetKind;

    public LiveWallpaperWindow(Uri videoUri)
    {
        InitializeComponent();
        _videoUri = videoUri;

        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        Loaded += async (_, _) =>
        {
            try
            {
                Log($"Loaded video={_videoUri}");
                AttachBehindDesktopIcons();
                if (_isAttached)
                    await StartVideoAsync();
                else
                    Log("Attach failed");
            }
            catch (Exception exception)
            {
                Log($"Loaded failed: {exception}");
                Close();
            }
        };
    }

    public bool IsAttached => _isAttached;
    public DesktopWallpaperTargetKind AttachedTargetKind => _attachedTargetKind;

    private async Task StartVideoAsync()
    {
        await WallpaperWebView.EnsureCoreWebView2Async();
        WallpaperWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        WallpaperWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
        WallpaperWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;

        var playbackUri = _videoUri;
        if (_videoUri.IsFile)
        {
            var localDirectory = Path.GetDirectoryName(_videoUri.LocalPath);
            if (!string.IsNullOrWhiteSpace(localDirectory) &&
                Directory.Exists(localDirectory))
            {
                WallpaperWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    LocalWallpaperHost,
                    localDirectory,
                    CoreWebView2HostResourceAccessKind.Allow);
                playbackUri = new Uri($"https://{LocalWallpaperHost}/{Uri.EscapeDataString(Path.GetFileName(_videoUri.LocalPath))}");
                Log($"Mapped local video directory={localDirectory} source={playbackUri}");
            }
        }

        WallpaperWebView.WebMessageReceived += (_, args) =>
            Log($"WebView: {args.TryGetWebMessageAsString()}");
        WallpaperWebView.NavigateToString(CreateVideoHtml(playbackUri));
        Log($"Video started target={_attachedTargetKind}");
    }

    private static string CreateVideoHtml(Uri videoUri)
    {
        var source = JsonSerializer.Serialize(videoUri.ToString());
        var videos = CreateMonitorVideoElements(source);
        return $$"""
            <!doctype html>
            <html>
            <head>
                <meta charset="utf-8">
                <style>
                    html, body {
                        width: 100%;
                        height: 100%;
                        margin: 0;
                        overflow: hidden;
                        background: #000;
                    }
                    .monitor-video {
                        position: absolute;
                        object-fit: cover;
                        background: #000;
                    }
                </style>
            </head>
            <body>
                {{videos}}
                <script>
                    for (const video of document.querySelectorAll('video')) {
                        video.addEventListener('loadedmetadata', () => chrome.webview.postMessage(`loadedmetadata ${video.videoWidth}x${video.videoHeight}`));
                        video.addEventListener('playing', () => chrome.webview.postMessage('playing'));
                        video.addEventListener('error', () => chrome.webview.postMessage(`error ${video.error ? video.error.code : 'unknown'}`));
                        const play = () => video.play().catch(error => chrome.webview.postMessage(`play-failed ${error && error.message ? error.message : error}`));
                        video.addEventListener('canplay', play, { once: true });
                        play();
                    }
                </script>
            </body>
            </html>
            """;
    }

    private static string CreateMonitorVideoElements(string serializedSource)
    {
        var virtualBounds = new NativeRect(
            (int)SystemParameters.VirtualScreenLeft,
            (int)SystemParameters.VirtualScreenTop,
            (int)(SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth),
            (int)(SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight));
        var screens = GetMonitorBounds();
        if (screens.Count == 0)
        {
            return $$"""<video class="monitor-video" style="left:0;top:0;width:100vw;height:100vh;" src={{serializedSource}} autoplay muted loop playsinline></video>""";
        }

        return string.Join(
            Environment.NewLine,
            screens.Select(screen =>
            {
                var left = screen.Left - virtualBounds.Left;
                var top = screen.Top - virtualBounds.Top;
                return $$"""<video class="monitor-video" style="left:{{left}}px;top:{{top}}px;width:{{screen.Width}}px;height:{{screen.Height}}px;" src={{serializedSource}} autoplay muted loop playsinline preload="auto"></video>""";
            }));
    }

    private static List<NativeRect> GetMonitorBounds()
    {
        var monitors = new List<NativeRect>();
        EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            (monitor, _, _, _) =>
            {
                var info = new MonitorInfo
                {
                    Size = Marshal.SizeOf<MonitorInfo>()
                };
                if (GetMonitorInfo(monitor, ref info))
                    monitors.Add(info.Monitor);
                return true;
            },
            IntPtr.Zero);

        return monitors;
    }


    private void AttachBehindDesktopIcons()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
            return;

        var targets = DesktopWorkerWindowFinder.FindTargets();
        Log($"Targets: {string.Join(", ", targets.Select(target => $"{target.Kind}:{target.Handle}/after={target.InsertAfter}"))}");

        foreach (var target in targets)
        {
            if (target.Kind == DesktopWallpaperTargetKind.WorkerW)
            {
                Log($"WorkerW target={target.Handle} visible={IsWindowVisible(target.Handle)}");
                if (!IsWindowVisible(target.Handle))
                    continue;
            }

            Marshal.SetLastPInvokeError(0);
            var previousParent = SetParent(handle, target.Handle);
            if (previousParent == IntPtr.Zero && Marshal.GetLastPInvokeError() != 0)
            {
                Log($"SetParent failed target={target.Kind} handle={target.Handle} error={Marshal.GetLastPInvokeError()}");
                continue;
            }

            ConvertToDesktopChildWindow(handle);

            var insertAfter = target.Kind switch
            {
                DesktopWallpaperTargetKind.Progman => HwndBottom,
                DesktopWallpaperTargetKind.ProgmanBehindIcons when target.InsertAfter != IntPtr.Zero => target.InsertAfter,
                _ => HwndTop
            };
            var flags = SwpNoActivate | SwpShowWindow | SwpNoOwnerZOrder;
            if (target.Kind == DesktopWallpaperTargetKind.WorkerW)
                flags |= SwpNoZOrder;

            _isAttached = SetWindowPos(
                handle,
                insertAfter,
                0,
                0,
                (int)SystemParameters.VirtualScreenWidth,
                (int)SystemParameters.VirtualScreenHeight,
                flags);

            if (_isAttached)
            {
                _attachedTargetKind = target.Kind;
                Log($"Attached target={target.Kind} handle={target.Handle}");
                ShowWindow(handle, SwShowNoActivate);
                Log($"Child visible={IsWindowVisible(handle)}");
                return;
            }

            Log($"SetWindowPos failed target={target.Kind} handle={target.Handle} error={Marshal.GetLastPInvokeError()}");
        }
    }

    private static void ConvertToDesktopChildWindow(IntPtr handle)
    {
        var style = GetWindowLongPtr(handle, GwlStyle).ToInt64();
        style &= ~WsPopup;
        style |= WsChild | WsVisible;
        SetWindowLongPtr(handle, GwlStyle, new IntPtr(style));
    }

    private void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
            File.AppendAllText(
                _logPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never affect wallpaper playback.
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        WallpaperWebView.Dispose();
        base.OnClosed(e);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        int flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr window, int commandShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsWindowVisible(IntPtr window);

    private delegate bool MonitorEnumProc(
        IntPtr monitor,
        IntPtr hdc,
        IntPtr clipRect,
        IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr clipRect,
        MonitorEnumProc callback,
        IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public NativeRect(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }
}
