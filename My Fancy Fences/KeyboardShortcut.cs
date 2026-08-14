using System.Windows.Input;

namespace My_Fancy_Fences;

public sealed record KeyboardShortcut(ModifierKeys Modifiers, Key Key)
{
    public string DisplayText
    {
        get
        {
            var parts = new List<string>();
            if (Modifiers.HasFlag(ModifierKeys.Control))
                parts.Add("Ctrl");
            if (Modifiers.HasFlag(ModifierKeys.Alt))
                parts.Add("Alt");
            if (Modifiers.HasFlag(ModifierKeys.Shift))
                parts.Add("Shift");
            if (Modifiers.HasFlag(ModifierKeys.Windows))
                parts.Add("Win");

            parts.Add(GetKeyText(Key));
            return string.Join(" + ", parts);
        }
    }

    public static bool IsAllowed(Key key, ModifierKeys modifiers) =>
        modifiers != ModifierKeys.None && !IsModifierKey(key) && KeyInterop.VirtualKeyFromKey(key) > 0;

    public static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or
            Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or
            Key.LWin or Key.RWin or
            Key.System or Key.None;

    private static string GetKeyText(Key key) => key switch
    {
        Key.D0 => "0",
        Key.D1 => "1",
        Key.D2 => "2",
        Key.D3 => "3",
        Key.D4 => "4",
        Key.D5 => "5",
        Key.D6 => "6",
        Key.D7 => "7",
        Key.D8 => "8",
        Key.D9 => "9",
        Key.OemPlus => "+",
        Key.OemMinus => "-",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemQuestion => "/",
        Key.OemSemicolon => ";",
        Key.OemQuotes => "'",
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        Key.OemBackslash => "\\",
        Key.Space => "Space",
        Key.Return => "Enter",
        Key.Escape => "Esc",
        Key.Prior => "Page Up",
        Key.Next => "Page Down",
        _ => key.ToString()
    };
}
