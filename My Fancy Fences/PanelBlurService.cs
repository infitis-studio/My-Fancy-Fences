using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace My_Fancy_Fences;

/// <summary>
/// Builds the blurred copy of the desktop wallpaper that panels show behind their (semi-transparent)
/// background. The wallpaper is laid out the way Windows draws it on one monitor, blurred once at a
/// quarter of the resolution (cheap, and a blur hides the lost detail) and cached. Panels then only
/// pick the part of that bitmap that lies under them, so moving a panel never re-renders the blur.
/// </summary>
public static class PanelBlurService
{
    /// <summary>The cached bitmap is 1/Scale of the monitor size in each direction.</summary>
    public const int Scale = 4;

    private const int MaxCachedImages = 8;
    private const uint SpiGetDeskWallpaper = 0x0073;

    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, BitmapSource> Cache = new();
    private static readonly Queue<string> CacheOrder = new();
    private static bool _hooked;

    /// <summary>Raised on the UI thread when the wallpaper or the display layout changed.</summary>
    public static event EventHandler? SourceChanged;

    public static void EnsureHooks()
    {
        if (_hooked)
            return;

        _hooked = true;
        SystemEvents.UserPreferenceChanged += (_, args) =>
        {
            if (args.Category == UserPreferenceCategory.Desktop)
                Invalidate();
        };
        SystemEvents.DisplaySettingsChanged += (_, _) => Invalidate();
    }

    private static void Invalidate()
    {
        lock (CacheLock)
        {
            Cache.Clear();
            CacheOrder.Clear();
        }

        Application.Current?.Dispatcher.BeginInvoke(() => SourceChanged?.Invoke(null, EventArgs.Empty));
    }

    /// <summary>
    /// Blurred wallpaper for a monitor of the given size (in DIPs); null when no wallpaper image is set.
    /// Must be called on the UI thread. One bitmap pixel covers <see cref="Scale"/> DIPs.
    /// </summary>
    public static BitmapSource? GetBlurredMonitorImage(Size monitorSize, int radius)
    {
        var wallpaper = ReadWallpaper();
        if (wallpaper is null || monitorSize.Width < 1 || monitorSize.Height < 1)
            return null;

        var key = $"{wallpaper.Path}|{wallpaper.Stamp}|{wallpaper.Style}|{wallpaper.Tile}|" +
                  $"{(int)monitorSize.Width}x{(int)monitorSize.Height}|{radius}";
        lock (CacheLock)
        {
            if (Cache.TryGetValue(key, out var cached))
                return cached;
        }

        BitmapSource? image;
        try
        {
            image = Render(wallpaper, monitorSize, radius);
        }
        catch
        {
            // A wallpaper that cannot be decoded simply means no blur backdrop.
            return null;
        }

        lock (CacheLock)
        {
            Cache[key] = image;
            CacheOrder.Enqueue(key);
            while (CacheOrder.Count > MaxCachedImages)
                Cache.Remove(CacheOrder.Dequeue());
        }

        return image;
    }

    private static BitmapSource Render(WallpaperInfo wallpaper, Size monitorSize, int radius)
    {
        var frame = BitmapFrame.Create(
            new Uri(wallpaper.Path),
            BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile,
            BitmapCacheOption.None);
        double imageWidth = frame.PixelWidth;
        double imageHeight = frame.PixelHeight;

        var width = monitorSize.Width;
        var height = monitorSize.Height;
        var systemScale = MonitorLayoutService.SystemScale;

        // Where Windows draws the picture on this monitor, in monitor DIPs.
        Rect target;
        var tile = false;
        switch (wallpaper.Style)
        {
            case 2: // stretch
                target = new Rect(0, 0, width, height);
                break;
            case 6: // fit
            {
                var factor = Math.Min(width / imageWidth, height / imageHeight);
                target = Center(imageWidth * factor, imageHeight * factor, width, height);
                break;
            }
            case 0: // center or tile
                target = Center(imageWidth / systemScale, imageHeight / systemScale, width, height);
                tile = wallpaper.Tile;
                break;
            default: // fill (10) and span (22)
            {
                var factor = Math.Max(width / imageWidth, height / imageHeight);
                target = Center(imageWidth * factor, imageHeight * factor, width, height);
                break;
            }
        }

        var quarterWidth = Math.Max(1, (int)Math.Round(width / Scale));
        var quarterHeight = Math.Max(1, (int)Math.Round(height / Scale));
        var quarterTarget = new Rect(
            target.X / Scale,
            target.Y / Scale,
            Math.Max(1, target.Width / Scale),
            Math.Max(1, target.Height / Scale));

        // Decode only as many pixels as the quarter-size layout needs.
        var source = new BitmapImage();
        source.BeginInit();
        source.UriSource = new Uri(wallpaper.Path);
        source.CacheOption = BitmapCacheOption.OnLoad;
        source.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        source.DecodePixelWidth = (int)Math.Clamp(Math.Ceiling(quarterTarget.Width), 16, imageWidth);
        source.EndInit();
        source.Freeze();

        // Stage 1: the wallpaper laid out on a quarter-size monitor canvas.
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.Black, null, new Rect(0, 0, quarterWidth, quarterHeight));
            if (tile)
            {
                var tileSize = new Size(
                    Math.Max(1, imageWidth / systemScale / Scale),
                    Math.Max(1, imageHeight / systemScale / Scale));
                var brush = new ImageBrush(source)
                {
                    TileMode = TileMode.Tile,
                    ViewportUnits = BrushMappingMode.Absolute,
                    Viewport = new Rect(quarterTarget.X, quarterTarget.Y, tileSize.Width, tileSize.Height)
                };
                context.DrawRectangle(brush, null, new Rect(0, 0, quarterWidth, quarterHeight));
            }
            else
            {
                context.DrawImage(source, quarterTarget);
            }
        }

        var layout = new RenderTargetBitmap(quarterWidth, quarterHeight, 96, 96, PixelFormats.Pbgra32);
        layout.Render(visual);
        layout.Freeze();

        // Stage 2: blur. Mirrored padding around the layout keeps the monitor edges from fading out.
        var blurRadius = Math.Max(0.5, radius / (double)Scale);
        var padding = (int)Math.Ceiling(blurRadius * 3) + 2;
        var paddedWidth = quarterWidth + padding * 2;
        var paddedHeight = quarterHeight + padding * 2;
        var host = new Grid
        {
            Width = paddedWidth,
            Height = paddedHeight,
            Effect = new BlurEffect
            {
                Radius = blurRadius,
                KernelType = KernelType.Gaussian,
                RenderingBias = RenderingBias.Performance
            }
        };
        host.Children.Add(new Rectangle
        {
            Fill = new ImageBrush(layout)
            {
                TileMode = TileMode.FlipXY,
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(padding, padding, quarterWidth, quarterHeight)
            }
        });
        host.Measure(new Size(paddedWidth, paddedHeight));
        host.Arrange(new Rect(0, 0, paddedWidth, paddedHeight));
        host.UpdateLayout();

        var blurred = new RenderTargetBitmap(paddedWidth, paddedHeight, 96, 96, PixelFormats.Pbgra32);
        blurred.Render(host);

        var result = new CroppedBitmap(blurred, new Int32Rect(padding, padding, quarterWidth, quarterHeight));
        result.Freeze();
        return result;
    }

    private static Rect Center(double width, double height, double areaWidth, double areaHeight) =>
        new((areaWidth - width) / 2, (areaHeight - height) / 2, width, height);

    private static WallpaperInfo? ReadWallpaper()
    {
        try
        {
            var buffer = new StringBuilder(520);
            if (!SystemParametersInfo(SpiGetDeskWallpaper, (uint)buffer.Capacity, buffer, 0))
                return null;

            var path = buffer.ToString();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;

            const string desktopKey = @"HKEY_CURRENT_USER\Control Panel\Desktop";
            _ = int.TryParse(Registry.GetValue(desktopKey, "WallpaperStyle", "10") as string, out var style);
            var tile = (Registry.GetValue(desktopKey, "TileWallpaper", "0") as string) == "1";
            return new WallpaperInfo(path, File.GetLastWriteTimeUtc(path).Ticks, style, tile);
        }
        catch
        {
            return null;
        }
    }

    private sealed record WallpaperInfo(string Path, long Stamp, int Style, bool Tile);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, StringBuilder text, uint winIni);
}
