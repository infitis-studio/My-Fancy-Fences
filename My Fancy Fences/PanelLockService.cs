namespace My_Fancy_Fences;

/// <summary>
/// Global "lock all panels" state. While locked, panels can't be dragged
/// or resized by the user; their positions are only changed by the application itself.
/// </summary>
public static class PanelLockService
{
    public static bool IsLocked { get; set; }
}
