using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;

namespace My_Fancy_Fences;

public partial class ShortcutCaptureWindow : Window
{
    private const int WhKeyboardLowLevel = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int VkLeftWin = 0x5B;
    private const int VkRightWin = 0x5C;

    private readonly LowLevelKeyboardProc _keyboardHookProc;
    private IntPtr _keyboardHook;
    private bool _isWindowsModifierPressed;

    public ShortcutCaptureWindow(string layoutName, KeyboardShortcut? currentShortcut = null)
    {
        InitializeComponent();
        Icon = AppIconProvider.Image;
        _keyboardHookProc = KeyboardHookCallback;
        DescriptionText.Text =
            $"{LocalizationService.T("Wciśnij skrót, który ma przełączać na układ")} „{layoutName}”.";
        Shortcut = currentShortcut;
        if (currentShortcut is not null)
        {
            ShortcutText.Text = currentShortcut.DisplayText;
            ConfirmButton.IsEnabled = true;
        }

        Loaded += (_, _) =>
        {
            InstallKeyboardHook();
            Focus();
        };
        Closed += (_, _) => RemoveKeyboardHook();
        Deactivated += (_, _) => ResetPromptNow();
        LostKeyboardFocus += (_, _) => ResetPromptNow();
    }

    public KeyboardShortcut? Shortcut { get; private set; }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.ImeProcessed)
            key = e.ImeProcessedKey;

        var modifiers = Keyboard.Modifiers;
        if (_isWindowsModifierPressed)
            modifiers |= ModifierKeys.Windows;

        if (!KeyboardShortcut.IsAllowed(key, modifiers))
        {
            ShowWaitingForMainKey();
            e.Handled = true;
            return;
        }

        Shortcut = new KeyboardShortcut(modifiers, key);
        ShortcutText.Text = Shortcut.DisplayText;
        ConfirmButton.IsEnabled = true;
        e.Handled = true;
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        if (e.Key is Key.LWin or Key.RWin)
            e.Handled = true;

        ResetPromptAfterKeyReleaseSoon();
        base.OnPreviewKeyUp(e);
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (Shortcut is null)
            return;

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void InstallKeyboardHook()
    {
        if (_keyboardHook != IntPtr.Zero)
            return;

        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        _keyboardHook = SetWindowsHookEx(
            WhKeyboardLowLevel,
            _keyboardHookProc,
            module is null ? IntPtr.Zero : GetModuleHandle(module.ModuleName),
            0);
    }

    private void RemoveKeyboardHook()
    {
        if (_keyboardHook == IntPtr.Zero)
            return;

        UnhookWindowsHookEx(_keyboardHook);
        _keyboardHook = IntPtr.Zero;
        _isWindowsModifierPressed = false;
    }

    private IntPtr KeyboardHookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var message = wParam.ToInt32();
            var keyboard = Marshal.PtrToStructure<KeyboardHookStruct>(lParam);
            if (keyboard.VirtualKey is VkLeftWin or VkRightWin)
            {
                if (message is WmKeyDown or WmSysKeyDown)
                {
                    _isWindowsModifierPressed = true;
                    Dispatcher.BeginInvoke(ShowWaitingForMainKey);
                }
                else if (message is WmKeyUp or WmSysKeyUp)
                {
                    _isWindowsModifierPressed = false;
                    Dispatcher.BeginInvoke(ResetPromptAfterKeyReleaseSoon);
                }

                return new IntPtr(1);
            }
        }

        return CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    private void ShowWaitingForMainKey()
    {
        ShortcutText.Text = LocalizationService.T("Wciśnij jeszcze jeden klawisz");
        ConfirmButton.IsEnabled = false;
        ResetPromptAfterKeyReleaseSoon();
    }

    private async void ResetPromptAfterKeyReleaseSoon()
    {
        await Task.Delay(70);
        if (Keyboard.Modifiers != ModifierKeys.None)
            await Task.Delay(180);

        if (!IsLoaded ||
            _isWindowsModifierPressed ||
            Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        if (Shortcut is null)
        {
            ShortcutText.Text = LocalizationService.T("Dodaj Ctrl, Alt, Shift albo Win");
            ConfirmButton.IsEnabled = false;
        }
        else
        {
            ShortcutText.Text = Shortcut.DisplayText;
            ConfirmButton.IsEnabled = true;
        }
    }

    private void ResetPromptNow()
    {
        _isWindowsModifierPressed = false;
        if (Shortcut is null)
        {
            ShortcutText.Text = LocalizationService.T("Dodaj Ctrl, Alt, Shift albo Win");
            ConfirmButton.IsEnabled = false;
        }
        else
        {
            ShortcutText.Text = Shortcut.DisplayText;
            ConfirmButton.IsEnabled = true;
        }
    }

    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHookStruct
    {
        public int VirtualKey;
        public int ScanCode;
        public int Flags;
        public int Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int hookId,
        LowLevelKeyboardProc callback,
        IntPtr moduleHandle,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hookHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hookHandle,
        int code,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
