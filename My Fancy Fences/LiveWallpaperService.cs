using System.IO;
using System.Text.Json;
using System.Windows;

namespace My_Fancy_Fences;

public static class LiveWallpaperService
{
    private static readonly string StateFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "My Fancy Fences",
        "live-wallpaper.json");

    private static LiveWallpaperWindow? _currentWindow;

    public static bool IsRunning => _currentWindow is not null;

    public static async Task<bool> TrySetAsync(Uri videoUri)
    {
        try
        {
            Stop();
            var window = new LiveWallpaperWindow(videoUri);
            _currentWindow = window;
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_currentWindow, window))
                    _currentWindow = null;
            };
            window.Show();
            await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            if (window.IsAttached)
            {
                SaveLastLiveWallpaper(videoUri);
                return true;
            }

            Stop();
            return false;
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
}
