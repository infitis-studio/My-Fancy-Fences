using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace My_Fancy_Fences;

/// <summary>
/// What only happens on the very first launch of a fresh installation: the welcome notice and pinning the
/// tray icon next to the clock. The state is kept in first-run.json. An existing installation (settings
/// already present) is treated as already set up, so an update never shows the notice again and never
/// overrides a tray icon the user hid on purpose.
/// </summary>
public static class FirstRunService
{
    private const int MaxPromotionLaunches = 5;
    private static readonly string StatePath = Path.Combine(ShortcutLibraryService.AppDataDirectory, "first-run.json");
    private static readonly string SettingsPath = Path.Combine(ShortcutLibraryService.AppDataDirectory, "settings.json");
    private static State _state = new(true, true, 0);

    private sealed record State(bool NoticeShown, bool TrayIconPromotionDone, int PromotionLaunches);

    /// <summary>The welcome notice has not been shown yet.</summary>
    public static bool NoticePending => !_state.NoticeShown;

    /// <summary>The tray icon still has to be pinned next to the clock.</summary>
    public static bool TrayPromotionPending => !_state.TrayIconPromotionDone;

    /// <summary>Must run at startup, before the application writes its own settings file.</summary>
    public static void Initialize()
    {
        try
        {
            if (File.Exists(StatePath))
            {
                _state = JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath)) ?? _state;
                return;
            }

            _state = File.Exists(SettingsPath)
                ? new State(true, true, 0)
                : new State(false, false, 0);
            Save();
        }
        catch
        {
            // When the state cannot be read, do nothing rather than nag the user on every launch.
            _state = new State(true, true, 0);
        }
    }

    public static void MarkNoticeShown()
    {
        _state = _state with { NoticeShown = true };
        Save();
    }

    /// <summary>
    /// Sets "show icon in the taskbar corner" for this program in Windows 11 (IsPromoted = 1). Windows
    /// creates the entry a moment after the icon first appears, so it retries for a while. It is done once:
    /// afterwards the user can switch the icon off in Windows settings and it stays off.
    /// </summary>
    public static async Task EnsureTrayIconVisibleAsync()
    {
        if (_state.TrayIconPromotionDone || string.IsNullOrWhiteSpace(Environment.ProcessPath))
            return;

        var executable = Environment.ProcessPath;
        _state = _state with { PromotionLaunches = _state.PromotionLaunches + 1 };
        Save();

        for (var attempt = 0; attempt < 10; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            if (TryPromoteTrayIcon(executable))
            {
                _state = _state with { TrayIconPromotionDone = true };
                Save();
                return;
            }
        }

        // No entry after several launches (for example on Windows 10): stop trying.
        if (_state.PromotionLaunches >= MaxPromotionLaunches)
        {
            _state = _state with { TrayIconPromotionDone = true };
            Save();
        }
    }

    private static bool TryPromoteTrayIcon(string executable)
    {
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings");
            if (root is null)
                return false;

            var promoted = false;
            foreach (var name in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(name, writable: true);
                if (key?.GetValue("ExecutablePath") is string path &&
                    string.Equals(path, executable, StringComparison.OrdinalIgnoreCase))
                {
                    key.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                    promoted = true;
                }
            }

            return promoted;
        }
        catch
        {
            return false;
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Persisting this state is best-effort only.
        }
    }
}
