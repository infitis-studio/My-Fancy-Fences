using System.IO;
using System.Text.Json;

namespace My_Fancy_Fences;

/// <summary>
/// Decides when to remind the user about supporting the author. The state is kept in support-reminder.json:
/// the first launch date, the date of the next reminder and the "never remind again" choice. Closing the
/// window with X brings it back after a day, "remind later" after 30 days.
/// </summary>
public static class SupportReminderService
{
    /// <summary>Opened by the "Wesprzyj mnie" button.</summary>
    public const string SupportUrl = "https://ko-fi.com/infitisstudio#linkModal";

    private const int DaysBeforeFirstReminder = 14;
    private const int RemindLaterDays = 30;
    private static readonly string StatePath = Path.Combine(ShortcutLibraryService.AppDataDirectory, "support-reminder.json");

    private static State _state = new(DateTime.Now, null, true);

    private sealed record State(DateTime FirstLaunchDate, DateTime? NextReminderDate, bool NeverRemind);

    public static void Initialize()
    {
        try
        {
            if (File.Exists(StatePath))
            {
                _state = JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath)) ?? _state;
                return;
            }

            _state = new State(DateTime.Now, null, false);
            Save();
        }
        catch
        {
            // When the state cannot be read, do nothing rather than nag the user on every launch.
            _state = new State(DateTime.Now, null, true);
        }
    }

    public static bool ShouldShow()
    {
        if (_state.NeverRemind)
            return false;

        var now = DateTime.Now;
        return _state.NextReminderDate is { } next
            ? now >= next
            : now >= _state.FirstLaunchDate.AddDays(DaysBeforeFirstReminder);
    }

    public static void RemindLater()
    {
        _state = _state with { NextReminderDate = DateTime.Now.AddDays(RemindLaterDays) };
        Save();
    }

    /// <summary>Closed with X: the window comes back after a day.</summary>
    public static void RemindTomorrow()
    {
        _state = _state with { NextReminderDate = DateTime.Now.AddDays(1) };
        Save();
    }

    public static void NeverRemind()
    {
        _state = _state with { NeverRemind = true };
        Save();
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(ShortcutLibraryService.AppDataDirectory);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // The reminder is a convenience; failing to save must not break the application.
        }
    }
}
