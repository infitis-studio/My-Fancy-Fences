using System.IO;
using System.Text.Json;
using System.Windows;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace My_Fancy_Fences;

public static class LiveWallpaperService
{
    private static readonly string StateFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "My Fancy Fences",
        "live-wallpaper.json");

    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "My Fancy Fences",
        "live-wallpaper-settings.json");

    /// <summary>Playback heights offered in settings; 0 plays the downloaded file as it is.</summary>
    public static readonly int[] QualityHeights = [0, 1080, 720, 480, 360];

    private const int DefaultQualityHeight = 720;

    private static LiveWallpaperWindow? _currentWindow;
    private static Uri? _sourceUri;
    private static int _applyVersion;

    static LiveWallpaperService() => QualityHeight = LoadQualityHeight();

    public static bool IsRunning => _currentWindow is not null;

    /// <summary>Height of the video that is played (0 = original). Scaled copies are made once and reused.</summary>
    public static int QualityHeight { get; private set; }

    /// <summary>Progress text for the settings window (empty when nothing is being prepared).</summary>
    public static event Action<string>? StatusChanged;

    /// <param name="videoUri">The downloaded (original) video. The scaled copy is chosen from it.</param>
    public static async Task<bool> TrySetAsync(Uri videoUri)
    {
        try
        {
            _sourceUri = videoUri;
            var version = Interlocked.Increment(ref _applyVersion);

            // Start at once with the scaled copy if it exists, otherwise with the original.
            var scaledPath = GetScaledPath(videoUri, QualityHeight);
            var startUri = scaledPath is not null && File.Exists(scaledPath) ? new Uri(scaledPath) : videoUri;
            if (!await PlayAsync(startUri))
                return false;

            SaveLastLiveWallpaper(videoUri);
            if (scaledPath is not null && !File.Exists(scaledPath))
                _ = PrepareScaledAndSwapAsync(videoUri, scaledPath, QualityHeight, version);
            return true;
        }
        catch
        {
            Stop();
            return false;
        }
    }

    public static async Task RestoreLastLiveWallpaperAsync()
    {
        try
        {
            var state = LoadState();
            if (state is null ||
                string.IsNullOrWhiteSpace(state.VideoUrl) ||
                !Uri.TryCreate(state.VideoUrl, UriKind.Absolute, out var videoUri))
            {
                return;
            }

            await TrySetAsync(videoUri);
        }
        catch
        {
            // Live wallpaper restore must never block normal application startup.
        }
    }

    /// <summary>Changes the playback quality; a running wallpaper switches to the new size when it is ready.</summary>
    public static async Task SetQualityAsync(int height)
    {
        if (!QualityHeights.Contains(height) || height == QualityHeight)
            return;

        QualityHeight = height;
        SaveQualityHeight(height);
        if (_sourceUri is null || _currentWindow is null)
            return;

        var source = _sourceUri;
        var version = Interlocked.Increment(ref _applyVersion);
        var scaledPath = GetScaledPath(source, height);
        if (scaledPath is null)
        {
            await PlayAsync(source);
            return;
        }

        if (File.Exists(scaledPath))
        {
            await PlayAsync(new Uri(scaledPath));
            return;
        }

        await PrepareScaledAndSwapAsync(source, scaledPath, height, version);
    }

    public static void Stop()
    {
        if (_currentWindow is null)
            return;

        var window = _currentWindow;
        _currentWindow = null;
        if (window.IsVisible)
            window.Close();
    }

    public static void Shutdown() => Stop();

    /// <summary>
    /// Called when a regular wallpaper is set: forgets the live wallpaper and stops it, so its video window
    /// and WebView2 do not keep decoding in the background.
    /// </summary>
    public static void ClearState()
    {
        _sourceUri = null;
        Interlocked.Increment(ref _applyVersion);
        Stop();
        try
        {
            if (File.Exists(StateFilePath))
                File.Delete(StateFilePath);
        }
        catch
        {
            // Clearing state is best-effort only.
        }
    }

    private static async Task<bool> PlayAsync(Uri playbackUri)
    {
        Stop();
        var window = new LiveWallpaperWindow(playbackUri);
        _currentWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_currentWindow, window))
                _currentWindow = null;
        };
        window.Show();
        await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (window.IsAttached)
            return true;

        Stop();
        return false;
    }

    private static async Task PrepareScaledAndSwapAsync(Uri source, string scaledPath, int height, int version)
    {
        StatusChanged?.Invoke(LocalizationService.T("Przygotowywanie przeskalowanej wersji…"));
        var created = await TranscodeAsync(source.LocalPath, scaledPath, height);
        StatusChanged?.Invoke(string.Empty);

        // A newer request (another wallpaper or quality) has taken over in the meantime.
        if (version != _applyVersion || !ReferenceEquals(_sourceUri, source) || _currentWindow is null)
            return;

        if (created)
            await PlayAsync(new Uri(scaledPath));
    }

    /// <summary>
    /// Path of the scaled copy, or null when the original is used (original quality, a remote video, or a
    /// video that is not larger than the chosen height).
    /// </summary>
    private static string? GetScaledPath(Uri source, int height)
    {
        if (height <= 0 || !source.IsFile)
            return null;

        var sourceHeight = ReadVideoSize(source.LocalPath).Height;
        if (sourceHeight > 0 && sourceHeight <= height)
            return null;

        var directory = Path.Combine(Path.GetDirectoryName(source.LocalPath)!, "Scaled");
        return Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(source.LocalPath)}.{height}p.mp4");
    }

    private static (int Width, int Height) ReadVideoSize(string path)
    {
        try
        {
            // Run on the thread pool, so the call is safe from the UI thread as well.
            return Task.Run(async () =>
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                var properties = await file.Properties.GetVideoPropertiesAsync();
                return ((int)properties.Width, (int)properties.Height);
            }).GetAwaiter().GetResult();
        }
        catch
        {
            return (0, 0);
        }
    }

    private static async Task<bool> TranscodeAsync(string sourcePath, string targetPath, int height)
    {
        var temporaryPath = targetPath + ".tmp";
        try
        {
            var (sourceWidth, sourceHeight) = await Task.Run(() => ReadVideoSize(sourcePath));
            if (sourceWidth <= 0 || sourceHeight <= 0)
                return false;

            var width = (int)Math.Round(sourceWidth * (double)height / sourceHeight / 2) * 2;
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);

            var source = await StorageFile.GetFileFromPathAsync(sourcePath);
            var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(targetPath)!);
            var target = await folder.CreateFileAsync(
                Path.GetFileName(temporaryPath),
                CreationCollisionOption.ReplaceExisting);

            var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
            profile.Audio = null;
            profile.Video.Width = (uint)width;
            profile.Video.Height = (uint)height;
            profile.Video.Bitrate = height switch { >= 1080 => 6_000_000u, >= 720 => 3_500_000u, >= 480 => 1_800_000u, _ => 1_200_000u };
            profile.Video.FrameRate.Numerator = 30;
            profile.Video.FrameRate.Denominator = 1;

            var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
            var prepared = await transcoder.PrepareFileTranscodeAsync(source, target, profile);
            if (!prepared.CanTranscode)
                return false;

            await prepared.TranscodeAsync();
            File.Move(temporaryPath, targetPath, overwrite: true);
            return true;
        }
        catch
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch
            {
                // The leftover temporary file is replaced on the next attempt.
            }

            return false;
        }
    }

    private static int LoadQualityHeight()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var settings = JsonSerializer.Deserialize<LiveWallpaperSettings>(File.ReadAllText(SettingsFilePath));
                if (settings is not null && QualityHeights.Contains(settings.QualityHeight))
                    return settings.QualityHeight;
            }
        }
        catch
        {
            // Fall back to the default quality.
        }

        return DefaultQualityHeight;
    }

    private static void SaveQualityHeight(int height)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath)!);
            File.WriteAllText(
                SettingsFilePath,
                JsonSerializer.Serialize(new LiveWallpaperSettings(height), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // The quality is a convenience setting; failing to save must not break playback.
        }
    }

    private static void SaveLastLiveWallpaper(Uri videoUri)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StateFilePath)!);
            File.WriteAllText(
                StateFilePath,
                JsonSerializer.Serialize(
                    new LiveWallpaperState(videoUri.ToString()),
                    new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Persisting the last live wallpaper is best-effort only.
        }
    }

    private static LiveWallpaperState? LoadState()
    {
        if (!File.Exists(StateFilePath))
            return null;

        return JsonSerializer.Deserialize<LiveWallpaperState>(
            File.ReadAllText(StateFilePath));
    }

    private sealed record LiveWallpaperState(string VideoUrl);

    private sealed record LiveWallpaperSettings(int QualityHeight);
}
