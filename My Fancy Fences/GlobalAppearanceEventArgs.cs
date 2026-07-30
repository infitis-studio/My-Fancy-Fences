using System.Windows.Media;

namespace My_Fancy_Fences;

public enum GlobalAppearancePhase
{
    Preview,
    Commit,
    Cancel
}

public sealed record GlobalAppearanceEventArgs(
    GlobalAppearancePhase Phase,
    bool HideHeader,
    Color BackgroundColor,
    double BackgroundOpacity,
    bool ApplyHeaderToAll,
    bool ApplyColorToAll);
