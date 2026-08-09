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
    double BorderRadius,
    double BorderThickness,
    Color BorderColor,
    double BorderOpacity,
    string FontFamilyName,
    Color FontColor,
    double FontOpacity,
    bool FontBold,
    double LetterSpacing,
    string IconFontFamilyName,
    Color IconFontColor,
    double IconFontOpacity,
    bool IconFontBold,
    double IconLetterSpacing,
    double IconSize);
