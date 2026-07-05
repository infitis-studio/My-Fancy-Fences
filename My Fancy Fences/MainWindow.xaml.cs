using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MahApps.Metro.IconPacks;
using Microsoft.Win32;

namespace My_Fancy_Fences;

public partial class MainWindow : Window
{
    private const int MaxIconCacheEntries = 192;
    private static int _newPanelCount;
    private static readonly ConcurrentDictionary<string, ImageSource> IconCache =
        new(StringComparer.OrdinalIgnoreCase);
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsVisible = 0x10000000;
    private const uint GwHwndPrevious = 3;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint WmTrayIcon = 0x8001;
    private const uint WmRightButtonUp = 0x0205;
    private const uint WmContextMenu = 0x007B;
    private const uint WmNull = 0x0000;
    private const uint NimAdd = 0x00000000;
    private const uint NimDelete = 0x00000002;
    private const uint NimSetVersion = 0x00000004;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NifGuid = 0x00000020;
    private const uint NotifyIconVersion4 = 4;
    private const uint MfString = 0x00000000;
    private const uint MfChecked = 0x00000008;
    private const uint MfSeparator = 0x00000800;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCommand = 0x0100;
    private const uint TrayExitCommand = 1;
    private const uint TrayCreatorCommand = 2;
    private const uint TrayAutoStartCommand = 3;
    private const uint TraySettingsCommand = 4;
    private const uint TrayWallpapersCommand = 5;
    private const uint TrayNewPanelCommand = 6;
    private const int WhMouseLowLevel = 14;
    private const int WmLeftButtonDown = 0x0201;
    private const int WmRightButtonDown = 0x0204;
    private const int WmMiddleButtonDown = 0x0207;
    private const string AutoStartRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AutoStartRegistryValueName = "My Fancy Fences";
    private static readonly IntPtr HwndNotTopmost = new(-2);
    private static readonly Guid TrayIconGuid = new("D84061E2-1F10-4F94-8A7F-A674EAE38E31");

    private IntPtr _windowHandle;
    private IntPtr _trayWindowHandle;
    private HwndSource? _trayMessageSource;
    private uint _taskbarCreatedMessage;
    private string _sourceFolder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    private bool _hideFolders;
    private Color _backgroundColor = Color.FromRgb(0x0B, 0x0E, 0x12);
    private double _backgroundOpacity = 0.58;
    private double _iconSize = 42;
    private string _fontFamilyName = "Segoe UI Variable Text";
    private double _borderRadius = 11;
    private double _borderThickness;
    private Color _borderColor = Colors.White;
    private double _borderOpacity;
    private Color _fontColor = Color.FromRgb(0xF7, 0xF9, 0xFC);
    private double _fontOpacity = 1;
    private bool _fontBold;
    private double _letterSpacing;
    private string _iconFontFamilyName = "Segoe UI Variable Text";
    private Color _iconFontColor = Color.FromRgb(0xF7, 0xF9, 0xFC);
    private double _iconFontOpacity = 1;
    private bool _iconFontBold;
    private double _iconLetterSpacing;
    private bool _useDoubleClickToOpen;
    private bool _isHeaderHidden;
    private double? _savedLeft;
    private double? _savedTop;
    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "My Fancy Fences",
        "settings.json");
    private bool _isDragging;
    private Point _dragStartScreenPosition;
    private double _dragStartLeft;
    private double _dragStartTop;
    private bool _isResizing;
    private Point _resizeStartScreenPosition;
    private double _resizeStartWidth;
    private double _resizeStartHeight;
    private NotifyIconData _trayIconData;
    private IntPtr _trayIconHandle;
    private CreatorWindow? _creatorWindow;
    private PanelsWindow? _panelsWindow;
    private bool _showMainPanel = true;
    private bool _showCreatorPanel;
    private bool _creatorHideHeader;
    private double? _creatorLeft;
    private double? _creatorTop;
    private const double CreatorPanelWidth = 360;
    private const double CreatorPanelHeight = 160;
    private Color _creatorBackgroundColor = Color.FromRgb(0x0B, 0x0E, 0x12);
    private double _creatorBackgroundOpacity = 0.58;
    private bool? _globalPreviewOriginalHeaderHidden;
    private Color? _globalPreviewOriginalBackgroundColor;
    private double? _globalPreviewOriginalBackgroundOpacity;
    private readonly bool _isPrimaryWindow;
    private readonly int _newPanelIndex;
    private readonly Action? _requestHostSave;
    private readonly List<MainWindow> _additionalPanels = [];
    private List<SavedPanelSettings> _savedAdditionalPanels = [];
    private bool _isApplicationClosing;
    private bool _isPendingCreation;
    private FileSystemWatcher? _sourceFolderWatcher;
    private string? _contextItemPath;
    private IntPtr _itemPopupMouseHook;
    private LowLevelMouseProc? _itemPopupMouseHookProc;
    private CancellationTokenSource? _itemsLoadCancellation;
    private readonly System.Windows.Threading.DispatcherTimer _folderRefreshTimer;

    public MainWindow() : this(true)
    {
    }

    private MainWindow(bool isPrimaryWindow, Action? requestHostSave = null)
    {
        _isPrimaryWindow = isPrimaryWindow;
        _requestHostSave = requestHostSave;
        _newPanelIndex = isPrimaryWindow ? 0 : ++_newPanelCount;
        _folderRefreshTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        _folderRefreshTimer.Tick += (_, _) =>
        {
            _folderRefreshTimer.Stop();
            LoadDesktopItems();
        };
        InitializeComponent();
        Icon = AppIconProvider.Image;
        ApplyIconSize(_iconSize);
        ApplyPanelFont(_fontFamilyName);
        ApplyBorderAppearance(_borderRadius, _borderThickness, _borderColor, _borderOpacity);
        ApplyTextAppearance(_fontColor, _fontOpacity, _fontBold, _letterSpacing);
        ApplyIconFont(_iconFontFamilyName);
        ApplyIconTextAppearance(_iconFontColor, _iconFontOpacity, _iconFontBold, _iconLetterSpacing);
        if (_isPrimaryWindow)
            LoadSavedSettings();
        SourceInitialized += MainWindow_SourceInitialized;
        Loaded += MainWindow_Loaded;
        Closing += (_, _) =>
        {
            _folderRefreshTimer.Stop();
            _itemsLoadCancellation?.Cancel();
            _itemsLoadCancellation?.Dispose();
            _itemsLoadCancellation = null;
            _sourceFolderWatcher?.Dispose();
            _sourceFolderWatcher = null;
            RemoveItemPopupMouseHook();
            if (_isPrimaryWindow)
            {
                _isApplicationClosing = true;
                SaveSettings();
                SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
                RemoveTrayIcon();
                _trayMessageSource?.RemoveHook(WindowMessageHook);
                _trayMessageSource?.Dispose();
                _trayMessageSource = null;
                _trayWindowHandle = IntPtr.Zero;
            }
        };
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isPrimaryWindow)
            PositionAtTopRight();
        else
            PositionNewPanel();
        _ = StabilizeDesktopLevelAsync();
        LoadDesktopItems();
        ApplyHeaderPresentation(_isHeaderHidden);
        if (_isPrimaryWindow)
        {
            if (!_showMainPanel)
                Hide();

            RestoreAdditionalPanels();

            await Task.Delay(250);
            if (IsLoaded)
                UpdateCreatorPanelVisibility();

            _ = CompactMemoryAfterStartupAsync();
        }
    }

    private async Task CompactMemoryAfterStartupAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(8));
        if (!IsLoaded)
            return;

        await Dispatcher.InvokeAsync(
            () =>
            {
                GCSettings.LargeObjectHeapCompactionMode =
                    GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            },
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private async Task StabilizeDesktopLevelAsync()
    {
        // Explorer can rebuild its desktop windows during sign-in and temporarily
        // move newly created widgets above normal applications. Reapply the
        // desktop-level position a few times while the shell settles.
        var delays = new[] { 100, 400, 1200, 3000 };
        foreach (var delay in delays)
        {
            await Task.Delay(delay);
            if (!IsLoaded)
                return;

            if (IsVisible)
                SendToDesktopLevel();
        }
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _windowHandle = handle;
        var extendedStyle = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(extendedStyle | WsExToolWindow | WsExNoActivate));

        if (_isPrimaryWindow)
        {
            _trayMessageSource = new HwndSource(new HwndSourceParameters("MyFancyFences.TrayWindow")
            {
                PositionX = -32000,
                PositionY = -32000,
                Width = 1,
                Height = 1,
                WindowStyle = WsPopup | WsVisible,
                ExtendedWindowStyle = WsExToolWindow
            });
            _trayMessageSource.AddHook(WindowMessageHook);
            _trayWindowHandle = _trayMessageSource.Handle;
            _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
            SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
            AddTrayIcon();
        }
    }

    private IntPtr WindowMessageHook(
        IntPtr hWnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (_taskbarCreatedMessage != 0 && message == _taskbarCreatedMessage)
        {
            _ = RestoreTrayIconAfterShellChangeAsync();
            return IntPtr.Zero;
        }

        var trayNotification = unchecked((uint)lParam.ToInt64()) & 0xFFFF;
        if (message == WmTrayIcon &&
            (trayNotification == WmRightButtonUp || trayNotification == WmContextMenu))
        {
            ShowTrayMenu();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume)
            return;

        _ = Dispatcher.BeginInvoke(async () =>
        {
            await RestoreTrayIconAfterShellChangeAsync();
            if (_showCreatorPanel)
                UpdateCreatorPanelVisibility();

            _ = StabilizeDesktopLevelAsync();
            foreach (var panel in _additionalPanels.Where(panel => panel.IsLoaded && panel.IsVisible))
                _ = panel.StabilizeDesktopLevelAsync();
        });
    }

    private async Task RestoreTrayIconAfterShellChangeAsync()
    {
        await Task.Delay(800);
        if (_isApplicationClosing || _trayWindowHandle == IntPtr.Zero)
            return;

        RemoveTrayIcon();
        AddTrayIcon();
    }

    private void AddTrayIcon()
    {
        _trayIconHandle = CreateFenceTrayIcon();
        if (_trayIconHandle == IntPtr.Zero)
            _trayIconHandle = LoadIcon(IntPtr.Zero, new IntPtr(32512));

        _trayIconData = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            WindowHandle = _trayWindowHandle,
            Id = 1,
            Flags = NifMessage | NifIcon | NifTip | NifGuid,
            CallbackMessage = WmTrayIcon,
            IconHandle = _trayIconHandle,
            Tip = "My Fancy Fences",
            GuidItem = TrayIconGuid
        };

        ShellNotifyIcon(NimAdd, ref _trayIconData);
        _trayIconData.TimeoutOrVersion = NotifyIconVersion4;
        ShellNotifyIcon(NimSetVersion, ref _trayIconData);
    }

    private static IntPtr CreateFenceTrayIcon()
    {
        const int size = 32;
        var colorBitmap = new byte[size * size * 4];
        var maskBitmap = new byte[size * size / 8];
        Array.Fill(maskBitmap, (byte)0xFF);

        DrawRoundedTile(colorBitmap, size, 4, 4, 10, 10);
        DrawRoundedTile(colorBitmap, size, 18, 4, 10, 10);
        DrawRoundedTile(colorBitmap, size, 4, 18, 10, 10);
        DrawRoundedTile(colorBitmap, size, 18, 18, 10, 10);

        return CreateIcon(
            IntPtr.Zero,
            size,
            size,
            1,
            32,
            maskBitmap,
            colorBitmap);
    }

    private static void DrawRoundedTile(
        byte[] pixels,
        int stridePixels,
        int left,
        int top,
        int width,
        int height)
    {
        for (var y = top; y < top + height; y++)
        {
            for (var x = left; x < left + width; x++)
            {
                var localX = x - left;
                var localY = y - top;
                var isCutCorner =
                    (localX == 0 && (localY == 0 || localY == height - 1)) ||
                    (localX == width - 1 && (localY == 0 || localY == height - 1));

                if (isCutCorner)
                    continue;

                var index = (y * stridePixels + x) * 4;
                pixels[index] = 0xD2;
                pixels[index + 1] = 0xD8;
                pixels[index + 2] = 0xE1;
                pixels[index + 3] = 0xFF;
            }
        }
    }

    private void RemoveTrayIcon()
    {
        if (_trayIconData.WindowHandle != IntPtr.Zero)
            ShellNotifyIcon(NimDelete, ref _trayIconData);

        if (_trayIconHandle != IntPtr.Zero)
        {
            DestroyIcon(_trayIconHandle);
            _trayIconHandle = IntPtr.Zero;
        }
    }

    private void ShowTrayMenu()
    {
        if (!GetCursorPos(out var cursorPosition))
            return;

        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
            return;

        try
        {
            AppendMenu(menu, MfString, TraySettingsCommand, LocalizationService.T("Ustawienia"));
            AppendMenu(menu, MfString, TrayWallpapersCommand, LocalizationService.T("Tapety"));
            AppendMenu(menu, MfString, TrayNewPanelCommand, LocalizationService.T("Dodaj nowy panel"));
            AppendMenu(menu, MfSeparator, 0, string.Empty);
            AppendMenu(
                menu,
                MfString | (_showCreatorPanel ? MfChecked : 0),
                TrayCreatorCommand,
                LocalizationService.T("Pokaż panel kreatora"));
            AppendMenu(
                menu,
                MfString | (IsAutoStartEnabled() ? MfChecked : 0),
                TrayAutoStartCommand,
                LocalizationService.T("Uruchamiaj przy starcie systemu"));
            AppendMenu(menu, MfSeparator, 0, string.Empty);
            AppendMenu(menu, MfString, TrayExitCommand, LocalizationService.T("Zamknij"));

            var menuOwner = _trayWindowHandle;
            SetForegroundWindow(menuOwner);

            var command = TrackPopupMenu(
                menu,
                TpmRightButton | TpmReturnCommand,
                cursorPosition.X,
                cursorPosition.Y,
                0,
                menuOwner,
                IntPtr.Zero);

            PostMessage(menuOwner, WmNull, IntPtr.Zero, IntPtr.Zero);

            if (command == TraySettingsCommand)
                OpenPanelsWindow();
            else if (command == TrayWallpapersCommand)
                OpenWallpaperWindow();
            else if (command == TrayNewPanelCommand)
                CreateNewPanelFromTray();
            else if (command == TrayCreatorCommand)
            {
                _showCreatorPanel = !_showCreatorPanel;
                UpdateCreatorPanelVisibility();
                SaveSettings();
            }
            else if (command == TrayAutoStartCommand)
                SetAutoStartEnabled(!IsAutoStartEnabled());
            else if (command == TrayExitCommand)
                Application.Current.Shutdown();
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private void OpenWallpaperWindow()
    {
        var wallpaperWindow = new WallpaperWindow();
        wallpaperWindow.Show();
        wallpaperWindow.Topmost = true;
        wallpaperWindow.Topmost = false;
        wallpaperWindow.Activate();
        wallpaperWindow.Focus();
    }

    private void CreateNewPanelFromTray()
    {
        var panel = CreateAdditionalPanel(pendingCreation: true);
        panel.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            () => panel.OpenSettings(true));
    }

    private static bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutoStartRegistryPath, writable: false);
            var value = key?.GetValue(AutoStartRegistryValueName) as string;

            return string.Equals(
                NormalizeAutoStartPath(value),
                NormalizeAutoStartPath(GetCurrentExecutablePath()),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void SetAutoStartEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutoStartRegistryPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(AutoStartRegistryPath, writable: true);

            if (key is null)
                return;

            if (enabled)
                key.SetValue(AutoStartRegistryValueName, $"\"{GetCurrentExecutablePath()}\"", RegistryValueKind.String);
            else
                key.DeleteValue(AutoStartRegistryValueName, throwOnMissingValue: false);
        }
        catch
        {
            // Autostart is a convenience feature; failing to change it should not crash the widget.
        }
    }

    private static string GetCurrentExecutablePath()
    {
        return Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? typeof(MainWindow).Assembly.Location;
    }

    private static string NormalizeAutoStartPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var normalized = path.Trim();
        if (normalized.Length >= 2 && normalized[0] == '"' && normalized[^1] == '"')
            normalized = normalized[1..^1];

        try
        {
            return Path.GetFullPath(normalized);
        }
        catch
        {
            return normalized;
        }
    }

    private void PositionAtTopRight()
    {
        var workArea = SystemParameters.WorkArea;
        Left = _savedLeft.HasValue
            ? Math.Clamp(_savedLeft.Value, workArea.Left, Math.Max(workArea.Left, workArea.Right - Width))
            : workArea.Right - Width - 24;
        Top = _savedTop.HasValue
            ? Math.Clamp(_savedTop.Value, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - Height))
            : workArea.Top + 24;
        SendToDesktopLevel();
    }

    private void PositionNewPanel()
    {
        var workArea = SystemParameters.WorkArea;
        var offset = 34 * ((_newPanelIndex - 1) % 7);
        Left = Math.Clamp(
            _savedLeft ?? workArea.Left + 70 + offset,
            workArea.Left,
            Math.Max(workArea.Left, workArea.Right - Width));
        Top = Math.Clamp(
            _savedTop ?? workArea.Top + 70 + offset,
            workArea.Top,
            Math.Max(workArea.Top, workArea.Bottom - Height));
        SendToDesktopLevel();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        SendToDesktopLevel();
    }

    private void LoadDesktopItems()
    {
        StartWatchingSourceFolder();

        _itemsLoadCancellation?.Cancel();
        _itemsLoadCancellation?.Dispose();
        _itemsLoadCancellation = new CancellationTokenSource();
        _ = LoadDesktopItemsAsync(_sourceFolder, _itemsLoadCancellation.Token);
    }

    private async Task LoadDesktopItemsAsync(string sourceFolder, CancellationToken cancellationToken)
    {
        try
        {
            var entries = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var items = Directory.EnumerateFileSystemEntries(sourceFolder)
                    .Select(path => Directory.Exists(path)
                        ? (FileSystemInfo)new DirectoryInfo(path)
                        : new FileInfo(path))
                    .Where(item => !_hideFolders || item is not DirectoryInfo)
                    .OrderByDescending(item => item is DirectoryInfo)
                    .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Take(24)
                    .ToList();

                return items.Select(CreateDesktopItem).ToList();
            }, cancellationToken);

            if (!cancellationToken.IsCancellationRequested &&
                string.Equals(sourceFolder, _sourceFolder, StringComparison.OrdinalIgnoreCase))
            {
                DesktopItemsControl.ItemsSource = entries;
                await LoadDesktopItemIconsAsync(entries, sourceFolder, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            if (!cancellationToken.IsCancellationRequested &&
                string.Equals(sourceFolder, _sourceFolder, StringComparison.OrdinalIgnoreCase))
            {
                DesktopItemsControl.ItemsSource = Array.Empty<DesktopItem>();
            }
        }
    }

    private async Task LoadDesktopItemIconsAsync(
        IReadOnlyCollection<DesktopItem> entries,
        string sourceFolder,
        CancellationToken cancellationToken)
    {
        foreach (var item in entries.Where(item => item.Icon is null))
        {
            cancellationToken.ThrowIfCancellationRequested();

            ImageSource? icon;
            try
            {
                var iconTask = Task.Run(() => GetShellIcon(item.Path), cancellationToken);
                var completedTask = await Task.WhenAny(
                    iconTask,
                    Task.Delay(TimeSpan.FromMilliseconds(1500), cancellationToken));
                cancellationToken.ThrowIfCancellationRequested();

                if (completedTask == iconTask)
                {
                    icon = await iconTask;
                }
                else
                {
                    LogIconLoadError(
                        item.Path,
                        new TimeoutException("Przekroczono 1500 ms podczas pobierania ikony."));
                    icon = await Task.Run(
                        () => GetGenericShellIcon(item.Path),
                        cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogIconLoadError(item.Path, exception);
                continue;
            }

            if (icon is null || cancellationToken.IsCancellationRequested)
                continue;

            item.Icon = icon;
            await Task.Yield();
        }
    }

    private static void LogIconLoadError(string path, Exception exception)
    {
        try
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "My Fancy Fences");
            Directory.CreateDirectory(logDirectory);
            File.AppendAllText(
                Path.Combine(logDirectory, "icon-errors.log"),
                $"{DateTimeOffset.Now:O}\t{exception.GetType().Name}\t{path}\t{exception.Message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostyka nie może przerwać ładowania następnych ikon.
        }
    }

    private void StartWatchingSourceFolder()
    {
        _sourceFolderWatcher?.Dispose();
        _sourceFolderWatcher = null;

        if (!Directory.Exists(_sourceFolder))
            return;

        try
        {
            var watcher = new FileSystemWatcher(_sourceFolder)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                EnableRaisingEvents = true
            };

            watcher.Created += SourceFolder_Changed;
            watcher.Deleted += SourceFolder_Changed;
            watcher.Renamed += SourceFolder_Changed;
            watcher.Error += SourceFolderWatcher_Error;
            _sourceFolderWatcher = watcher;
        }
        catch
        {
            // Brak dostępu do obserwowania folderu nie powinien zatrzymać panelu.
        }
    }

    private void SourceFolder_Changed(object sender, FileSystemEventArgs e) =>
        ScheduleFolderRefresh();

    private void SourceFolderWatcher_Error(object sender, ErrorEventArgs e) =>
        ScheduleFolderRefresh();

    private void ScheduleFolderRefresh()
    {
        _ = Dispatcher.BeginInvoke(() =>
        {
            _folderRefreshTimer.Stop();
            _folderRefreshTimer.Start();
        });
    }

    private static DesktopItem CreateDesktopItem(FileSystemInfo item)
    {
        var isDirectory = item is DirectoryInfo;
        var displayName = isDirectory ? item.Name : Path.GetFileNameWithoutExtension(item.Name);
        IconCache.TryGetValue(item.FullName, out var cachedIcon);
        return new DesktopItem(displayName, item.FullName, cachedIcon);
    }

    private static ImageSource? GetShellIcon(string path)
    {
        if (IconCache.TryGetValue(path, out var cachedIcon))
            return cachedIcon;

        if (string.Equals(Path.GetExtension(path), ".url", StringComparison.OrdinalIgnoreCase))
        {
            var shortcutIcon = TryGetInternetShortcutIcon(path);
            if (shortcutIcon is not null)
            {
                CacheIcon(path, shortcutIcon);
                return shortcutIcon;
            }
        }

        var icon = GetShellIconCore(path, useFileAttributes: false);
        if (icon is not null)
            CacheIcon(path, icon);
        return icon;
    }

    private static void CacheIcon(string path, ImageSource icon)
    {
        if (IconCache.Count >= MaxIconCacheEntries && !IconCache.ContainsKey(path))
            IconCache.Clear();

        IconCache[path] = icon;
    }

    private static ImageSource? GetGenericShellIcon(string path) =>
        GetShellIconCore(path, useFileAttributes: true);

    private static ImageSource? GetShellIconCore(string path, bool useFileAttributes)
    {
        const uint shgfiIcon = 0x000000100;
        const uint shgfiLargeIcon = 0x000000000;
        const uint shgfiUseFileAttributes = 0x000000010;
        const uint fileAttributeNormal = 0x00000080;

        var result = SHGetFileInfo(
            path,
            useFileAttributes ? fileAttributeNormal : 0,
            out var fileInfo,
            (uint)Marshal.SizeOf<ShFileInfo>(),
            shgfiIcon | shgfiLargeIcon | (useFileAttributes ? shgfiUseFileAttributes : 0));

        if (result == IntPtr.Zero || fileInfo.IconHandle == IntPtr.Zero)
            return null;

        try
        {
            var icon = Imaging.CreateBitmapSourceFromHIcon(
                fileInfo.IconHandle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(48, 48));
            icon.Freeze();
            return icon;
        }
        finally
        {
            DestroyIcon(fileInfo.IconHandle);
        }
    }

    private static ImageSource? TryGetInternetShortcutIcon(string shortcutPath)
    {
        try
        {
            var iconLine = File.ReadLines(shortcutPath)
                .FirstOrDefault(line => line.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(iconLine))
                return null;

            var iconPath = Environment.ExpandEnvironmentVariables(iconLine["IconFile=".Length..].Trim().Trim('"'));
            if (!File.Exists(iconPath))
                return null;

            var decoder = BitmapDecoder.Create(
                new Uri(iconPath, UriKind.Absolute),
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            var frame = decoder.Frames
                .OrderBy(candidate => Math.Abs(candidate.PixelWidth - 48))
                .FirstOrDefault();
            frame?.Freeze();
            return frame;
        }
        catch
        {
            return null;
        }
    }

    private void DesktopItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string path })
            return;

        if ((_useDoubleClickToOpen && e.ClickCount != 2) ||
            (!_useDoubleClickToOpen && e.ClickCount != 1))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            e.Handled = true;
        }
        catch (Exception)
        {
            // A missing or inaccessible shortcut should not close the desktop widget.
        }
    }

    private void DesktopItem_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string path })
            return;

        ItemActionsPopup.IsOpen = false;
        _contextItemPath = path;
        ItemActionsPopup.IsOpen = true;
        InstallItemPopupMouseHook();
        e.Handled = true;
    }

    private void InstallItemPopupMouseHook()
    {
        RemoveItemPopupMouseHook();
        _itemPopupMouseHookProc = ItemPopupMouseHookCallback;
        _itemPopupMouseHook = SetWindowsHookEx(
            WhMouseLowLevel,
            _itemPopupMouseHookProc,
            IntPtr.Zero,
            0);
    }

    private IntPtr ItemPopupMouseHookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && ItemActionsPopup.IsOpen &&
            (wParam.ToInt32() == WmLeftButtonDown ||
             wParam.ToInt32() == WmRightButtonDown ||
             wParam.ToInt32() == WmMiddleButtonDown) &&
            !IsPointerInsideItemPopup())
        {
            Dispatcher.BeginInvoke(() => ItemActionsPopup.IsOpen = false);
        }

        return CallNextHookEx(_itemPopupMouseHook, code, wParam, lParam);
    }

    private bool IsPointerInsideItemPopup()
    {
        if (ItemActionsPopup.Child is not FrameworkElement child ||
            !GetCursorPos(out var cursor))
        {
            return false;
        }

        var topLeft = child.PointToScreen(new Point(0, 0));
        var bottomRight = child.PointToScreen(new Point(child.ActualWidth, child.ActualHeight));
        return cursor.X >= topLeft.X && cursor.X <= bottomRight.X &&
               cursor.Y >= topLeft.Y && cursor.Y <= bottomRight.Y;
    }

    private void ItemActionsPopup_Closed(object sender, EventArgs e)
    {
        RemoveItemPopupMouseHook();
        _contextItemPath = null;
    }

    private void RemoveItemPopupMouseHook()
    {
        if (_itemPopupMouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_itemPopupMouseHook);
            _itemPopupMouseHook = IntPtr.Zero;
        }

        _itemPopupMouseHookProc = null;
    }

    private void DeleteDesktopItemMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var path = _contextItemPath;
        ItemActionsPopup.IsOpen = false;
        _contextItemPath = null;

        if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
        {
            return;
        }

        var itemName = Path.GetFileName(
            path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var confirmation = new ConfirmationWindow(
            "Usunąć ikonę?",
            $"Czy przenieść „{itemName}” do Kosza?\n\nElement zniknie z tego panelu.",
            "Usuń",
            "Anuluj")
        {
            Owner = this
        };

        confirmation.Loaded += (_, _) =>
        {
            confirmation.Topmost = true;
            confirmation.Topmost = false;
            confirmation.Activate();
            confirmation.Focus();
        };

        if (confirmation.ShowDialog() != true)
            return;

        try
        {
            if (Directory.Exists(path))
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                    path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }
            else
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                    path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }

            IconCache.TryRemove(path, out _);
            LoadDesktopItems();
        }
        catch (Exception exception)
        {
            var error = new ConfirmationWindow(
                "Nie udało się usunąć ikony",
                exception.Message,
                "OK")
            {
                Owner = this
            };
            error.ShowDialog();
        }
    }

    private void Panel_DragEnter(object sender, DragEventArgs e) => UpdatePanelDropEffect(e);

    private void Panel_DragOver(object sender, DragEventArgs e) => UpdatePanelDropEffect(e);

    private void Panel_DragLeave(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private static void UpdatePanelDropEffect(DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Panel_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;

        if (!e.Data.GetDataPresent(DataFormats.FileDrop) ||
            e.Data.GetData(DataFormats.FileDrop) is not string[] droppedPaths ||
            !Directory.Exists(_sourceFolder))
        {
            return;
        }

        var paths = droppedPaths
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Where(path => !IsAlreadyInPanelFolder(path, _sourceFolder))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (paths.Length == 0)
            return;

        var panelName = string.IsNullOrWhiteSpace(TitleText.Text)
            ? "bez nazwy"
            : TitleText.Text.Trim();
        var itemDescription = paths.Length == 1
            ? $"element „{Path.GetFileName(paths[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}”"
            : $"{paths.Length} elementy";
        var confirmation = new ConfirmationWindow(
            $"Dodać do panelu „{panelName}”?",
            $"Czy przenieść {itemDescription} do panelu „{panelName}”?\n\nElementy zostaną przeniesione z obecnego miejsca do folderu panelu.",
            "Przenieś")
        {
            Owner = this
        };
        confirmation.Loaded += (_, _) =>
        {
            confirmation.Topmost = true;
            confirmation.Topmost = false;
            confirmation.Activate();
            confirmation.Focus();
        };

        if (confirmation.ShowDialog() != true)
            return;

        e.Effects = DragDropEffects.Move;
        var failures = await Task.Run(() => MoveDroppedItems(paths, _sourceFolder));
        LoadDesktopItems();

        if (failures.Count == 0)
            return;

        var message = failures.Count == 1
            ? $"Nie udało się dodać elementu „{failures[0]}”."
            : $"Nie udało się dodać {failures.Count} elementów.";
        var error = new ConfirmationWindow("Nie udało się dodać", message, "OK")
        {
            Owner = this
        };
        error.ShowDialog();
    }

    private static bool IsAlreadyInPanelFolder(string path, string panelFolder)
    {
        var sourceParent = Path.GetDirectoryName(
            path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.Equals(
            Path.GetFullPath(sourceParent ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(panelFolder).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> MoveDroppedItems(IEnumerable<string> paths, string panelFolder)
    {
        var failures = new List<string>();
        foreach (var sourcePath in paths)
        {
            try
            {
                var destinationPath = GetUniqueDestinationPath(panelFolder, sourcePath);
                if (Directory.Exists(sourcePath))
                {
                    var sourceFullPath = Path.GetFullPath(sourcePath)
                        .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    var destinationFullPath = Path.GetFullPath(destinationPath)
                        .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    if (destinationFullPath.StartsWith(sourceFullPath, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Nie można kopiować folderu do jego podfolderu.");

                    MoveDirectory(sourcePath, destinationPath);
                }
                else
                {
                    MoveFile(sourcePath, destinationPath);
                }
            }
            catch
            {
                failures.Add(Path.GetFileName(
                    sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
            }
        }

        return failures;
    }

    private static void MoveFile(string sourcePath, string destinationPath)
    {
        try
        {
            File.Move(sourcePath, destinationPath);
        }
        catch (IOException) when (!string.Equals(
            Path.GetPathRoot(sourcePath),
            Path.GetPathRoot(destinationPath),
            StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(sourcePath, destinationPath, overwrite: false);
            File.Delete(sourcePath);
        }
    }

    private static void MoveDirectory(string sourceDirectory, string destinationDirectory)
    {
        try
        {
            Directory.Move(sourceDirectory, destinationDirectory);
        }
        catch (IOException) when (!string.Equals(
            Path.GetPathRoot(sourceDirectory),
            Path.GetPathRoot(destinationDirectory),
            StringComparison.OrdinalIgnoreCase))
        {
            if ((File.GetAttributes(sourceDirectory) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Nie można bezpiecznie przenieść dowiązania między dyskami.");

            try
            {
                CopyDirectory(sourceDirectory, destinationDirectory);
            }
            catch
            {
                if (Directory.Exists(destinationDirectory))
                    Directory.Delete(destinationDirectory, recursive: true);
                throw;
            }

            Directory.Delete(sourceDirectory, recursive: true);
        }
    }

    private static string GetUniqueDestinationPath(string panelFolder, string sourcePath)
    {
        var trimmedSource = sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmedSource);
        var destination = Path.Combine(panelFolder, name);
        if (!File.Exists(destination) && !Directory.Exists(destination))
            return destination;

        var isDirectory = Directory.Exists(sourcePath);
        var extension = isDirectory ? string.Empty : Path.GetExtension(name);
        var baseName = isDirectory ? name : Path.GetFileNameWithoutExtension(name);
        for (var index = 2; ; index++)
        {
            destination = Path.Combine(panelFolder, $"{baseName} ({index}){extension}");
            if (!File.Exists(destination) && !Directory.Exists(destination))
                return destination;
        }
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var filePath in Directory.EnumerateFiles(sourceDirectory))
            File.Copy(filePath, Path.Combine(destinationDirectory, Path.GetFileName(filePath)), overwrite: false);

        foreach (var directoryPath in Directory.EnumerateDirectories(sourceDirectory))
        {
            if ((File.GetAttributes(directoryPath) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Folder zawiera dowiązanie, którego nie można bezpiecznie przenieść między dyskami.");

            CopyDirectory(
                directoryPath,
                Path.Combine(destinationDirectory, Path.GetFileName(directoryPath)));
        }
    }

    private void DragSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _isDragging = false;
            DragSurface.ReleaseMouseCapture();
            OpenSettings();
            e.Handled = true;
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            _isDragging = true;
            _dragStartScreenPosition = PointToScreen(e.GetPosition(this));
            _dragStartLeft = Left;
            _dragStartTop = Top;
            DragSurface.CaptureMouse();
            e.Handled = true;
        }
    }

    private void DragSurface_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || e.LeftButton != MouseButtonState.Pressed)
            return;

        var currentScreenPosition = PointToScreen(e.GetPosition(this));
        Left = _dragStartLeft + currentScreenPosition.X - _dragStartScreenPosition.X;
        Top = _dragStartTop + currentScreenPosition.Y - _dragStartScreenPosition.Y;
    }

    private void DragSurface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging)
            return;

        _isDragging = false;
        DragSurface.ReleaseMouseCapture();
        SendToDesktopLevel();
        SaveSettings();
        e.Handled = true;
    }

    private void OpenSettings(bool isNewPanel = false)
    {
        if (isNewPanel)
        {
            OpenNewPanelSettings();
            return;
        }

        var originalTitle = TitleText.Text;
        var originalIcon = HeaderIcon.Kind;
        var originalWidth = Width;
        var originalHeight = Height;
        var originalSourceFolder = _sourceFolder;
        var originalHideFolders = _hideFolders;
        var originalBackgroundColor = _backgroundColor;
        var originalBackgroundOpacity = _backgroundOpacity;
        var originalHeaderHidden = _isHeaderHidden;
        var originalIconSize = _iconSize;
        var originalFontFamily = _fontFamilyName;
        var originalBorderRadius = _borderRadius;
        var originalBorderThickness = _borderThickness;
        var originalBorderColor = _borderColor;
        var originalBorderOpacity = _borderOpacity;
        var originalFontColor = _fontColor;
        var originalFontOpacity = _fontOpacity;
        var originalFontBold = _fontBold;
        var originalLetterSpacing = _letterSpacing;
        var originalIconFontFamily = _iconFontFamilyName;
        var originalIconFontColor = _iconFontColor;
        var originalIconFontOpacity = _iconFontOpacity;
        var originalIconFontBold = _iconFontBold;
        var originalIconLetterSpacing = _iconLetterSpacing;

        var settingsWindow = new SettingsWindow(
            TitleText.Text,
            HeaderIcon.Kind,
            Width,
            Height,
            _sourceFolder,
            _backgroundColor,
            _backgroundOpacity,
            _isHeaderHidden,
            _hideFolders,
            _iconSize,
            _fontFamilyName,
            _borderRadius,
            _borderThickness,
            _borderColor,
            _borderOpacity,
            _fontColor,
            _fontOpacity,
            _fontBold,
            _letterSpacing,
            _iconFontFamilyName,
            _iconFontColor,
            _iconFontOpacity,
            _iconFontBold,
            _iconLetterSpacing,
            isNewPanel);
        settingsWindow.Loaded += (_, _) =>
        {
            settingsWindow.Topmost = true;
            settingsWindow.Topmost = false;
            settingsWindow.Activate();
            settingsWindow.Focus();
        };

        settingsWindow.PreviewChanged += (_, preview) =>
        {
            TitleText.Text = preview.Title;
            HeaderIcon.Kind = preview.Icon;
            ApplyHeaderPresentation(preview.HideHeader);
            if (_hideFolders != preview.HideFolders)
            {
                _hideFolders = preview.HideFolders;
                LoadDesktopItems();
            }
            ApplyIconSize(preview.IconSize);
            ApplyPanelFont(preview.FontFamilyName);
            ApplyBorderAppearance(preview.BorderRadius, preview.BorderThickness, preview.BorderColor, preview.BorderOpacity);
            ApplyTextAppearance(preview.FontColor, preview.FontOpacity, preview.FontBold, preview.LetterSpacing);
            ApplyIconFont(preview.IconFontFamilyName);
            ApplyIconTextAppearance(preview.IconFontColor, preview.IconFontOpacity, preview.IconFontBold, preview.IconLetterSpacing);
            Width = preview.Width;
            Height = preview.Height;
            ApplyBackground(preview.BackgroundColor, preview.BackgroundOpacity);

            if (Directory.Exists(preview.SourceFolder) &&
                !string.Equals(_sourceFolder, preview.SourceFolder, StringComparison.OrdinalIgnoreCase))
            {
                _sourceFolder = preview.SourceFolder;
                LoadDesktopItems();
            }
        };

        if (settingsWindow.ShowDialog() != true)
        {
            TitleText.Text = originalTitle;
            HeaderIcon.Kind = originalIcon;
            ApplyHeaderPresentation(originalHeaderHidden);
            ApplyIconSize(originalIconSize);
            ApplyPanelFont(originalFontFamily);
            ApplyBorderAppearance(originalBorderRadius, originalBorderThickness, originalBorderColor, originalBorderOpacity);
            ApplyTextAppearance(originalFontColor, originalFontOpacity, originalFontBold, originalLetterSpacing);
            ApplyIconFont(originalIconFontFamily);
            ApplyIconTextAppearance(originalIconFontColor, originalIconFontOpacity, originalIconFontBold, originalIconLetterSpacing);
            Width = originalWidth;
            Height = originalHeight;
            _sourceFolder = originalSourceFolder;
            _hideFolders = originalHideFolders;
            ApplyBackground(originalBackgroundColor, originalBackgroundOpacity);
            LoadDesktopItems();
            return;
        }

        if (settingsWindow.DeleteRequested)
        {
            TitleText.Text = originalTitle;
            HeaderIcon.Kind = originalIcon;
            ApplyHeaderPresentation(originalHeaderHidden);
            ApplyIconSize(originalIconSize);
            ApplyPanelFont(originalFontFamily);
            ApplyBorderAppearance(originalBorderRadius, originalBorderThickness, originalBorderColor, originalBorderOpacity);
            ApplyTextAppearance(originalFontColor, originalFontOpacity, originalFontBold, originalLetterSpacing);
            ApplyIconFont(originalIconFontFamily);
            ApplyIconTextAppearance(originalIconFontColor, originalIconFontOpacity, originalIconFontBold, originalIconLetterSpacing);
            Width = originalWidth;
            Height = originalHeight;
            _sourceFolder = originalSourceFolder;
            _hideFolders = originalHideFolders;
            ApplyBackground(originalBackgroundColor, originalBackgroundOpacity);
            if (_isPrimaryWindow)
            {
                _showMainPanel = false;
                Hide();
                SaveSettings();
            }
            else
            {
                Close();
            }
            return;
        }

        TitleText.Text = settingsWindow.FenceTitle;
        HeaderIcon.Kind = settingsWindow.FenceIcon;
        ApplyHeaderPresentation(settingsWindow.HideHeader);
        ApplyIconSize(settingsWindow.IconSize);
        ApplyPanelFont(settingsWindow.FenceFontFamily);
        ApplyBorderAppearance(
            settingsWindow.FenceBorderRadius,
            settingsWindow.FenceBorderThickness,
            settingsWindow.FenceBorderColor,
            settingsWindow.FenceBorderOpacity);
        ApplyTextAppearance(
            settingsWindow.FenceFontColor,
            settingsWindow.FenceFontOpacity,
            settingsWindow.FenceFontBold,
            settingsWindow.FenceLetterSpacing);
        ApplyIconFont(settingsWindow.FenceIconFontFamily);
        ApplyIconTextAppearance(
            settingsWindow.FenceIconFontColor,
            settingsWindow.FenceIconFontOpacity,
            settingsWindow.FenceIconFontBold,
            settingsWindow.FenceIconLetterSpacing);
        Width = settingsWindow.FenceWidth;
        Height = settingsWindow.FenceHeight;
        _sourceFolder = settingsWindow.SourceFolder;
        _hideFolders = settingsWindow.HideFolders;
        ApplyBackground(settingsWindow.BackgroundColor, settingsWindow.BackgroundOpacity);
        LoadDesktopItems();
        SendToDesktopLevel();
        SaveSettings();
    }

    private void OpenNewPanelSettings()
    {
        var settingsWindow = new SettingsWindow(
            TitleText.Text,
            HeaderIcon.Kind,
            Width,
            Height,
            _sourceFolder,
            _backgroundColor,
            _backgroundOpacity,
            _isHeaderHidden,
            _hideFolders,
            _iconSize,
            _fontFamilyName,
            _borderRadius,
            _borderThickness,
            _borderColor,
            _borderOpacity,
            _fontColor,
            _fontOpacity,
            _fontBold,
            _letterSpacing,
            _iconFontFamilyName,
            _iconFontColor,
            _iconFontOpacity,
            _iconFontBold,
            _iconLetterSpacing,
            true);

        SizeChangedEventHandler panelSizeChanged = (_, _) =>
            settingsWindow.UpdatePanelDimensions(ActualWidth, ActualHeight);
        SizeChanged += panelSizeChanged;

        settingsWindow.PreviewChanged += (_, preview) =>
        {
            TitleText.Text = preview.Title;
            HeaderIcon.Kind = preview.Icon;
            ApplyHeaderPresentation(preview.HideHeader);
            if (_hideFolders != preview.HideFolders)
            {
                _hideFolders = preview.HideFolders;
                LoadDesktopItems();
            }
            ApplyIconSize(preview.IconSize);
            ApplyPanelFont(preview.FontFamilyName);
            ApplyBorderAppearance(preview.BorderRadius, preview.BorderThickness, preview.BorderColor, preview.BorderOpacity);
            ApplyTextAppearance(preview.FontColor, preview.FontOpacity, preview.FontBold, preview.LetterSpacing);
            ApplyIconFont(preview.IconFontFamilyName);
            ApplyIconTextAppearance(preview.IconFontColor, preview.IconFontOpacity, preview.IconFontBold, preview.IconLetterSpacing);
            Width = preview.Width;
            Height = preview.Height;
            ApplyBackground(preview.BackgroundColor, preview.BackgroundOpacity);

            if (Directory.Exists(preview.SourceFolder) &&
                !string.Equals(_sourceFolder, preview.SourceFolder, StringComparison.OrdinalIgnoreCase))
            {
                _sourceFolder = preview.SourceFolder;
                LoadDesktopItems();
            }
        };

        settingsWindow.Closed += (_, _) =>
        {
            SizeChanged -= panelSizeChanged;
            if (!settingsWindow.Accepted)
            {
                Close();
                return;
            }

            TitleText.Text = settingsWindow.FenceTitle;
            HeaderIcon.Kind = settingsWindow.FenceIcon;
            ApplyHeaderPresentation(settingsWindow.HideHeader);
            ApplyIconSize(settingsWindow.IconSize);
            ApplyPanelFont(settingsWindow.FenceFontFamily);
            ApplyBorderAppearance(
                settingsWindow.FenceBorderRadius,
                settingsWindow.FenceBorderThickness,
                settingsWindow.FenceBorderColor,
                settingsWindow.FenceBorderOpacity);
            ApplyTextAppearance(
                settingsWindow.FenceFontColor,
                settingsWindow.FenceFontOpacity,
                settingsWindow.FenceFontBold,
                settingsWindow.FenceLetterSpacing);
            ApplyIconFont(settingsWindow.FenceIconFontFamily);
            ApplyIconTextAppearance(
                settingsWindow.FenceIconFontColor,
                settingsWindow.FenceIconFontOpacity,
                settingsWindow.FenceIconFontBold,
                settingsWindow.FenceIconLetterSpacing);
            Width = settingsWindow.FenceWidth;
            Height = settingsWindow.FenceHeight;
            _sourceFolder = settingsWindow.SourceFolder;
            _hideFolders = settingsWindow.HideFolders;
            ApplyBackground(settingsWindow.BackgroundColor, settingsWindow.BackgroundOpacity);
            LoadDesktopItems();
            SendToDesktopLevel();
            _isPendingCreation = false;
            SaveSettings();
        };

        settingsWindow.Loaded += (_, _) =>
        {
            settingsWindow.Topmost = true;
            settingsWindow.Topmost = false;
            settingsWindow.Activate();
            settingsWindow.Focus();
        };
        settingsWindow.Show();
    }

    private void ApplyHeaderPresentation(bool hideHeader)
    {
        _isHeaderHidden = hideHeader;
        HeaderContent.Visibility = hideHeader ? Visibility.Collapsed : Visibility.Visible;
        HeaderRow.Height = new GridLength(hideHeader ? 16 : 48);
        HeaderIcon.Margin = string.IsNullOrWhiteSpace(TitleText.Text)
            ? new Thickness(0)
            : new Thickness(0, 0, 10, 0);
    }

    private void ApplyBackground(Color color, double opacity)
    {
        _backgroundColor = color;
        _backgroundOpacity = opacity;
        FenceBorder.Background = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Round(opacity * 255),
            color.R,
            color.G,
            color.B));
    }

    private void ApplyIconSize(double iconSize)
    {
        _iconSize = Math.Clamp(iconSize, 24, 64);
        var scale = _iconSize / 42;

        Resources["DesktopIconSize"] = _iconSize;
        Resources["DesktopTileWidth"] = Math.Round(50 + 28 * scale);
        Resources["DesktopTileHeight"] = Math.Round(62 + 42 * scale);
        Resources["DesktopIconRowHeight"] = new GridLength(Math.Round(_iconSize + 14));
        Resources["DesktopTileMargin"] = new Thickness(scale < 0.8 ? 1 : 3);
        Resources["DesktopLabelMargin"] = new Thickness(2, scale < 0.8 ? 1 : 4, 2, 0);
        Resources["DesktopLabelFontSize"] = Math.Clamp(Math.Round(9 + 2 * scale, 1), 9, 12);
        Resources["DesktopLabelHeight"] = Math.Round(20 + 10 * scale);
    }

    private void ApplyPanelFont(string? fontFamilyName)
    {
        var selectedName = string.IsNullOrWhiteSpace(fontFamilyName)
            ? "Segoe UI Variable Text"
            : fontFamilyName;

        try
        {
            TitleText.FontFamily = new FontFamily(selectedName);
            _fontFamilyName = selectedName;
        }
        catch
        {
            TitleText.FontFamily = new FontFamily("Segoe UI");
            _fontFamilyName = "Segoe UI";
        }
    }

    private void ApplyIconFont(string? fontFamilyName)
    {
        var selectedName = string.IsNullOrWhiteSpace(fontFamilyName)
            ? "Segoe UI Variable Text"
            : fontFamilyName;

        try
        {
            Resources["IconFontFamily"] = new FontFamily(selectedName);
            _iconFontFamilyName = selectedName;
        }
        catch
        {
            Resources["IconFontFamily"] = new FontFamily("Segoe UI");
            _iconFontFamilyName = "Segoe UI";
        }
    }

    private void ApplyBorderAppearance(
        double radius,
        double thickness,
        Color color,
        double opacity)
    {
        _borderRadius = Math.Clamp(radius, 0, 30);
        _borderThickness = Math.Clamp(thickness, 0, 8);
        _borderColor = color;
        _borderOpacity = Math.Clamp(opacity, 0, 1);

        FenceBorder.CornerRadius = new CornerRadius(_borderRadius);
        FenceBorder.BorderThickness = new Thickness(_borderThickness);
        FenceBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Round(_borderOpacity * 255),
            color.R,
            color.G,
            color.B));
    }

    private void ApplyTextAppearance(
        Color color,
        double opacity,
        bool bold,
        double letterSpacing)
    {
        _fontColor = color;
        _fontOpacity = Math.Clamp(opacity, 0, 1);
        _fontBold = bold;
        _letterSpacing = Math.Clamp(letterSpacing, -2, 8);

        Resources["HeaderTextPrimary"] = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Round(_fontOpacity * 255),
            color.R,
            color.G,
            color.B));
        TitleText.FontWeight = bold ? FontWeights.Bold : FontWeights.SemiBold;
        TitleText.LetterSpacing = _letterSpacing;
    }

    private void ApplyIconTextAppearance(
        Color color,
        double opacity,
        bool bold,
        double letterSpacing)
    {
        _iconFontColor = color;
        _iconFontOpacity = Math.Clamp(opacity, 0, 1);
        _iconFontBold = bold;
        _iconLetterSpacing = Math.Clamp(letterSpacing, -2, 8);

        Resources["IconTextPrimary"] = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Round(_iconFontOpacity * 255),
            color.R,
            color.G,
            color.B));
        Resources["IconFontWeight"] = bold ? FontWeights.Bold : FontWeights.Normal;
        Resources["IconLetterSpacing"] = _iconLetterSpacing;
    }

    private void LoadSavedSettings()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
                return;

            var json = File.ReadAllText(SettingsFilePath);
            var settings = JsonSerializer.Deserialize<SavedFenceSettings>(json, JsonOptions);
            if (settings is null)
                return;

            TitleText.Text = settings.Title;
            HeaderIcon.Kind = settings.Icon;
            Width = Math.Max(MinWidth, settings.Width);
            Height = Math.Max(MinHeight, settings.Height);
            _sourceFolder = Directory.Exists(settings.SourceFolder)
                ? settings.SourceFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            _hideFolders = settings.HideFolders ?? false;
            _useDoubleClickToOpen = settings.UseDoubleClickToOpen ?? false;
            _isHeaderHidden = settings.HideHeader;
            ApplyIconSize(settings.IconSize > 0 ? settings.IconSize : 42);
            _savedLeft = settings.Left;
            _savedTop = settings.Top;
            _showCreatorPanel = settings.ShowCreatorPanel;
            _showMainPanel = settings.ShowMainPanel ?? true;
            _creatorHideHeader = settings.CreatorHideHeader;
            _creatorLeft = settings.CreatorLeft;
            _creatorTop = settings.CreatorTop;
            if (!string.IsNullOrWhiteSpace(settings.CreatorBackgroundHex))
            {
                _creatorBackgroundColor = (Color)ColorConverter.ConvertFromString(settings.CreatorBackgroundHex);
                _creatorBackgroundOpacity = Math.Clamp(settings.CreatorBackgroundOpacity, 0, 1);
            }
            var savedColor = (Color)ColorConverter.ConvertFromString(settings.BackgroundHex);
            ApplyBackground(savedColor, settings.BackgroundOpacity);
            ApplyPanelFont(settings.FontFamilyName);
            var savedBorderColor = string.IsNullOrWhiteSpace(settings.BorderColorHex)
                ? Colors.White
                : (Color)ColorConverter.ConvertFromString(settings.BorderColorHex);
            ApplyBorderAppearance(
                settings.BorderRadius ?? 11,
                settings.BorderThickness ?? 0,
                savedBorderColor,
                settings.BorderOpacity ?? 0);
            var savedFontColor = string.IsNullOrWhiteSpace(settings.FontColorHex)
                ? Color.FromRgb(0xF7, 0xF9, 0xFC)
                : (Color)ColorConverter.ConvertFromString(settings.FontColorHex);
            ApplyTextAppearance(
                savedFontColor,
                settings.FontOpacity ?? 1,
                settings.FontBold ?? false,
                settings.LetterSpacing ?? 0);
            var savedIconFontColor = string.IsNullOrWhiteSpace(settings.IconFontColorHex)
                ? savedFontColor
                : (Color)ColorConverter.ConvertFromString(settings.IconFontColorHex);
            ApplyIconFont(settings.IconFontFamilyName ?? settings.FontFamilyName);
            ApplyIconTextAppearance(
                savedIconFontColor,
                settings.IconFontOpacity ?? settings.FontOpacity ?? 1,
                settings.IconFontBold ?? settings.FontBold ?? false,
                settings.IconLetterSpacing ?? 0);
            _savedAdditionalPanels = settings.AdditionalPanels ?? [];
        }
        catch
        {
            // Invalid local settings should never prevent the widget from starting.
        }
    }

    private void SaveSettings()
    {
        if (!_isPrimaryWindow)
        {
            _requestHostSave?.Invoke();
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(SettingsFilePath);
            if (directory is not null)
                Directory.CreateDirectory(directory);

            var settings = new SavedFenceSettings(
                TitleText.Text,
                HeaderIcon.Kind,
                Width,
                Height,
                _sourceFolder,
                $"#{_backgroundColor.R:X2}{_backgroundColor.G:X2}{_backgroundColor.B:X2}",
                _backgroundOpacity,
                _isHeaderHidden,
                _iconSize,
                Left,
                Top,
                _showCreatorPanel,
                _creatorWindow?.IsHeaderHidden ?? _creatorHideHeader,
                _creatorWindow?.Left ?? _creatorLeft,
                _creatorWindow?.Top ?? _creatorTop,
                CreatorPanelWidth,
                CreatorPanelHeight,
                _creatorWindow is null
                    ? $"#{_creatorBackgroundColor.R:X2}{_creatorBackgroundColor.G:X2}{_creatorBackgroundColor.B:X2}"
                    : $"#{_creatorWindow.BackgroundColor.R:X2}{_creatorWindow.BackgroundColor.G:X2}{_creatorWindow.BackgroundColor.B:X2}",
                _creatorWindow?.BackgroundOpacity ?? _creatorBackgroundOpacity,
                _showMainPanel,
                _additionalPanels
                    .Where(panel => panel.IsLoaded && !panel._isPendingCreation)
                    .Select(panel => panel.CapturePanelSettings())
                    .ToList(),
                _fontFamilyName,
                _borderRadius,
                _borderThickness,
                $"#{_borderColor.R:X2}{_borderColor.G:X2}{_borderColor.B:X2}",
                _borderOpacity,
                $"#{_fontColor.R:X2}{_fontColor.G:X2}{_fontColor.B:X2}",
                _fontOpacity,
                _fontBold,
                _letterSpacing,
                _iconFontFamilyName,
                $"#{_iconFontColor.R:X2}{_iconFontColor.G:X2}{_iconFontColor.B:X2}",
                _iconFontOpacity,
                _iconFontBold,
                _iconLetterSpacing,
                _hideFolders,
                _useDoubleClickToOpen);

            if (File.Exists(SettingsFilePath))
            {
                var backupPath = Path.Combine(directory!, "settings.backup.json");
                File.Copy(SettingsFilePath, backupPath, overwrite: true);
            }

            File.WriteAllText(
                SettingsFilePath,
                JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
            // Saving preferences is optional and must not interrupt the desktop widget.
        }

        if (_panelsWindow?.IsVisible == true)
            _panelsWindow.UpdatePanels(CreatePanelOverviewList());
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed record SavedFenceSettings(
        string Title,
        PackIconLucideKind Icon,
        double Width,
        double Height,
        string SourceFolder,
        string BackgroundHex,
        double BackgroundOpacity,
        bool HideHeader,
        double IconSize,
        double Left,
        double Top,
        bool ShowCreatorPanel,
        bool CreatorHideHeader,
        double? CreatorLeft,
        double? CreatorTop,
        double CreatorWidth,
        double CreatorHeight,
        string? CreatorBackgroundHex,
        double CreatorBackgroundOpacity,
        bool? ShowMainPanel,
        List<SavedPanelSettings>? AdditionalPanels,
        string? FontFamilyName,
        double? BorderRadius,
        double? BorderThickness,
        string? BorderColorHex,
        double? BorderOpacity,
        string? FontColorHex,
        double? FontOpacity,
        bool? FontBold,
        double? LetterSpacing,
        string? IconFontFamilyName,
        string? IconFontColorHex,
        double? IconFontOpacity,
        bool? IconFontBold,
        double? IconLetterSpacing,
        bool? HideFolders,
        bool? UseDoubleClickToOpen);

    private sealed record SavedPanelSettings(
        string Title,
        PackIconLucideKind Icon,
        double Width,
        double Height,
        string SourceFolder,
        string BackgroundHex,
        double BackgroundOpacity,
        bool HideHeader,
        double IconSize,
        double Left,
        double Top,
        string? FontFamilyName,
        double? BorderRadius,
        double? BorderThickness,
        string? BorderColorHex,
        double? BorderOpacity,
        string? FontColorHex,
        double? FontOpacity,
        bool? FontBold,
        double? LetterSpacing,
        string? IconFontFamilyName,
        string? IconFontColorHex,
        double? IconFontOpacity,
        bool? IconFontBold,
        double? IconLetterSpacing,
        bool? HideFolders,
        bool? IsVisible);

    private SavedPanelSettings CapturePanelSettings() => new(
        TitleText.Text,
        HeaderIcon.Kind,
        Width,
        Height,
        _sourceFolder,
        $"#{_backgroundColor.R:X2}{_backgroundColor.G:X2}{_backgroundColor.B:X2}",
        _backgroundOpacity,
        _isHeaderHidden,
        _iconSize,
        Left,
        Top,
        _fontFamilyName,
        _borderRadius,
        _borderThickness,
        $"#{_borderColor.R:X2}{_borderColor.G:X2}{_borderColor.B:X2}",
        _borderOpacity,
        $"#{_fontColor.R:X2}{_fontColor.G:X2}{_fontColor.B:X2}",
        _fontOpacity,
        _fontBold,
        _letterSpacing,
        _iconFontFamilyName,
        $"#{_iconFontColor.R:X2}{_iconFontColor.G:X2}{_iconFontColor.B:X2}",
        _iconFontOpacity,
        _iconFontBold,
        _iconLetterSpacing,
        _hideFolders,
        IsVisible);

    private void ApplySavedPanelSettings(SavedPanelSettings settings)
    {
        TitleText.Text = settings.Title;
        HeaderIcon.Kind = settings.Icon;
        Width = Math.Max(MinWidth, settings.Width);
        Height = Math.Max(MinHeight, settings.Height);
        _sourceFolder = Directory.Exists(settings.SourceFolder)
            ? settings.SourceFolder
            : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        _hideFolders = settings.HideFolders ?? false;
        _savedLeft = settings.Left;
        _savedTop = settings.Top;
        ApplyIconSize(settings.IconSize > 0 ? settings.IconSize : 42);
        ApplyPanelFont(settings.FontFamilyName);
        ApplyHeaderPresentation(settings.HideHeader);

        var borderColor = string.IsNullOrWhiteSpace(settings.BorderColorHex)
            ? Colors.White
            : (Color)ColorConverter.ConvertFromString(settings.BorderColorHex);
        ApplyBorderAppearance(
            settings.BorderRadius ?? 11,
            settings.BorderThickness ?? 0,
            borderColor,
            settings.BorderOpacity ?? 0);
        var fontColor = string.IsNullOrWhiteSpace(settings.FontColorHex)
            ? Color.FromRgb(0xF7, 0xF9, 0xFC)
            : (Color)ColorConverter.ConvertFromString(settings.FontColorHex);
        ApplyTextAppearance(
            fontColor,
            settings.FontOpacity ?? 1,
            settings.FontBold ?? false,
            settings.LetterSpacing ?? 0);
        var iconFontColor = string.IsNullOrWhiteSpace(settings.IconFontColorHex)
            ? fontColor
            : (Color)ColorConverter.ConvertFromString(settings.IconFontColorHex);
        ApplyIconFont(settings.IconFontFamilyName ?? settings.FontFamilyName);
        ApplyIconTextAppearance(
            iconFontColor,
            settings.IconFontOpacity ?? settings.FontOpacity ?? 1,
            settings.IconFontBold ?? settings.FontBold ?? false,
            settings.IconLetterSpacing ?? 0);

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(settings.BackgroundHex);
            ApplyBackground(color, settings.BackgroundOpacity);
        }
        catch
        {
            ApplyBackground(Color.FromRgb(0x0B, 0x0E, 0x12), 0.58);
        }
    }

    private MainWindow CreateAdditionalPanel(
        SavedPanelSettings? settings = null,
        bool pendingCreation = false)
    {
        var panel = new MainWindow(false, SaveSettings);
        panel._isPendingCreation = pendingCreation;
        panel._useDoubleClickToOpen = _useDoubleClickToOpen;
        if (settings is not null)
            panel.ApplySavedPanelSettings(settings);

        _additionalPanels.Add(panel);
        panel.Closed += (_, _) =>
        {
            if (_isApplicationClosing)
                return;

            _additionalPanels.Remove(panel);
            SaveSettings();
        };
        panel.Show();
        if (settings?.IsVisible == false)
            panel.Hide();
        else
            panel.SendToDesktopLevel();
        return panel;
    }

    private void RestoreAdditionalPanels()
    {
        foreach (var settings in _savedAdditionalPanels)
            CreateAdditionalPanel(settings);

        _savedAdditionalPanels = [];
    }

    private void UpdateCreatorPanelVisibility()
    {
        if (_showCreatorPanel)
        {
            if (_creatorWindow is null)
            {
                _creatorWindow = new CreatorWindow(
                    _creatorHideHeader,
                    CreatorPanelWidth,
                    CreatorPanelHeight,
                    _creatorLeft,
                    _creatorTop,
                    _creatorBackgroundColor,
                    _creatorBackgroundOpacity);
                _creatorWindow.CreatorStateChanged += (_, _) => SaveSettings();
                _creatorWindow.NewPanelRequested += (_, _) =>
                {
                    var panel = CreateAdditionalPanel(pendingCreation: true);
                    panel.Dispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                        () => panel.OpenSettings(true));
                };
                _creatorWindow.SettingsRequested += (_, _) => OpenPanelsWindow();
                _creatorWindow.GlobalAppearanceChanged += CreatorWindow_GlobalAppearanceChanged;
            }

            if (!_creatorWindow.IsVisible)
                _creatorWindow.Show();
        }
        else if (_creatorWindow?.IsVisible == true)
        {
            _creatorWindow.Hide();
        }
    }

    private void OpenPanelsWindow()
    {
        var panels = CreatePanelOverviewList();

        if (_panelsWindow is null)
        {
            _panelsWindow = new PanelsWindow(panels, _useDoubleClickToOpen);
            _panelsWindow.PanelVisibilityChanged += PanelsWindow_PanelVisibilityChanged;
            _panelsWindow.EditPanelRequested += PanelsWindow_EditPanelRequested;
            _panelsWindow.RefreshIconsRequested += (_, _) => RefreshAllPanelIcons();
            _panelsWindow.ActivationModeChanged += (_, args) =>
                SetDoubleClickActivation(args.UseDoubleClickToOpen);
            _panelsWindow.Closed += (_, _) => _panelsWindow = null;
        }
        else
        {
            _panelsWindow.UpdatePanels(panels);
        }

        if (!_panelsWindow.IsVisible)
            _panelsWindow.Show();

        _panelsWindow.Topmost = true;
        _panelsWindow.Topmost = false;
        _panelsWindow.Activate();
        _panelsWindow.Focus();
    }

    private void RefreshAllPanelIcons()
    {
        IconCache.Clear();
        RefreshPanelIcons(this);
        foreach (var panel in _additionalPanels.Where(panel => panel.IsLoaded))
            RefreshPanelIcons(panel);
    }

    private void SetDoubleClickActivation(bool useDoubleClickToOpen)
    {
        _useDoubleClickToOpen = useDoubleClickToOpen;
        foreach (var panel in _additionalPanels)
            panel._useDoubleClickToOpen = useDoubleClickToOpen;
        SaveSettings();
    }

    private static void RefreshPanelIcons(MainWindow panel)
    {
        if (panel.DesktopItemsControl.ItemsSource is IEnumerable<DesktopItem> items)
        {
            foreach (var item in items)
                item.Icon = null;
        }

        panel.LoadDesktopItems();
    }

    private List<PanelOverviewItem> CreatePanelOverviewList()
    {
        var panels = new List<PanelOverviewItem>
        {
            CreatePanelOverview(this, LocalizationService.T("Panel główny"))
        };

        panels.AddRange(_additionalPanels
            .Where(panel => panel.IsLoaded && !panel._isPendingCreation)
            .Select((panel, index) => CreatePanelOverview(panel, $"Panel {index + 2}")));
        return panels;
    }

    private void PanelsWindow_PanelVisibilityChanged(object? sender, PanelVisibilityChangedEventArgs e)
    {
        if (!int.TryParse(e.PanelKey, out var panelIndex))
            return;

        var panel = panelIndex == 0
            ? this
            : _additionalPanels.FirstOrDefault(item => item._newPanelIndex == panelIndex);
        if (panel is null)
            return;

        if (panel._isPrimaryWindow)
            _showMainPanel = !e.IsHidden;

        if (e.IsHidden)
        {
            panel.Hide();
        }
        else
        {
            panel.Show();
            panel.SendToDesktopLevel();
        }

        SaveSettings();
        _panelsWindow?.UpdatePanels(CreatePanelOverviewList());
    }

    private void PanelsWindow_EditPanelRequested(object? sender, PanelEditRequestedEventArgs e)
    {
        if (!int.TryParse(e.PanelKey, out var panelIndex))
            return;

        var panel = panelIndex == 0
            ? this
            : _additionalPanels.FirstOrDefault(item => item._newPanelIndex == panelIndex);
        panel?.OpenSettings();
    }

    private static PanelOverviewItem CreatePanelOverview(MainWindow panel, string fallbackTitle)
    {
        var title = string.IsNullOrWhiteSpace(panel.TitleText.Text)
            ? fallbackTitle
            : panel.TitleText.Text;
        var width = Math.Round(panel.ActualWidth > 0 ? panel.ActualWidth : panel.Width);
        var height = Math.Round(panel.ActualHeight > 0 ? panel.ActualHeight : panel.Height);

        return new PanelOverviewItem(
            panel._newPanelIndex.ToString(),
            title,
            panel.HeaderIcon.Kind,
            panel._sourceFolder,
            $"{width:0} × {height:0} px · {LocalizationService.T("ikony")} {panel._iconSize:0} px",
            panel.IsVisible ? LocalizationService.T("Widoczny") : LocalizationService.T("Ukryty"),
            !panel.IsVisible);
    }

    private void CreatorWindow_GlobalAppearanceChanged(object? sender, GlobalAppearanceEventArgs e)
    {
        if (_globalPreviewOriginalHeaderHidden is null)
        {
            _globalPreviewOriginalHeaderHidden = _isHeaderHidden;
            _globalPreviewOriginalBackgroundColor = _backgroundColor;
            _globalPreviewOriginalBackgroundOpacity = _backgroundOpacity;
        }

        if (e.Phase == GlobalAppearancePhase.Cancel)
        {
            RestoreGlobalAppearancePreview();
            return;
        }

        ApplyHeaderPresentation(
            e.ApplyHeaderToAll
                ? e.HideHeader
                : _globalPreviewOriginalHeaderHidden.Value);

        if (e.ApplyColorToAll)
        {
            ApplyBackground(e.BackgroundColor, e.BackgroundOpacity);
        }
        else
        {
            ApplyBackground(
                _globalPreviewOriginalBackgroundColor!.Value,
                _globalPreviewOriginalBackgroundOpacity!.Value);
        }

        if (e.Phase == GlobalAppearancePhase.Commit)
        {
            if (e.ApplyHeaderToAll)
                _isHeaderHidden = e.HideHeader;

            if (e.ApplyColorToAll)
            {
                _backgroundColor = e.BackgroundColor;
                _backgroundOpacity = e.BackgroundOpacity;
            }

            _globalPreviewOriginalHeaderHidden = null;
            _globalPreviewOriginalBackgroundColor = null;
            _globalPreviewOriginalBackgroundOpacity = null;
        }
    }

    private void RestoreGlobalAppearancePreview()
    {
        if (_globalPreviewOriginalHeaderHidden is null)
            return;

        ApplyHeaderPresentation(_globalPreviewOriginalHeaderHidden.Value);
        ApplyBackground(
            _globalPreviewOriginalBackgroundColor!.Value,
            _globalPreviewOriginalBackgroundOpacity!.Value);

        _globalPreviewOriginalHeaderHidden = null;
        _globalPreviewOriginalBackgroundColor = null;
        _globalPreviewOriginalBackgroundOpacity = null;
    }

    private void ResizeArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
            return;

        _isResizing = true;
        _resizeStartScreenPosition = PointToScreen(e.GetPosition(this));
        _resizeStartWidth = ActualWidth;
        _resizeStartHeight = ActualHeight;
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    private void ResizeArea_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isResizing || e.LeftButton != MouseButtonState.Pressed)
            return;

        var currentScreenPosition = PointToScreen(e.GetPosition(this));
        Width = Math.Max(MinWidth, _resizeStartWidth + currentScreenPosition.X - _resizeStartScreenPosition.X);
        Height = Math.Max(MinHeight, _resizeStartHeight + currentScreenPosition.Y - _resizeStartScreenPosition.Y);
    }

    private void ResizeArea_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isResizing)
            return;

        _isResizing = false;
        ((UIElement)sender).ReleaseMouseCapture();
        e.Handled = true;
    }

    private void SendToDesktopLevel()
    {
        if (_windowHandle == IntPtr.Zero)
            return;

        var insertAfter = FindBottomMostApplicationWindow();

        SetWindowPos(
            _windowHandle,
            insertAfter,
            0,
            0,
            0,
            0,
            SwpNoActivate | SwpNoMove | SwpNoSize);
    }

    private IntPtr FindBottomMostApplicationWindow()
    {
        var desktopHost = GetShellWindow();
        EnumWindows((window, _) =>
        {
            if (FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero)
                return true;

            desktopHost = window;
            return false;
        }, IntPtr.Zero);

        if (desktopHost == IntPtr.Zero)
            return HwndNotTopmost;

        var nearestWindowAboveDesktop = GetWindow(desktopHost, GwHwndPrevious);
        var candidate = nearestWindowAboveDesktop;
        var currentProcessId = (uint)Environment.ProcessId;
        while (candidate != IntPtr.Zero)
        {
            GetWindowThreadProcessId(candidate, out var processId);
            var extendedStyle = GetWindowLongPtr(candidate, GwlExStyle).ToInt64();
            if (processId != currentProcessId &&
                IsWindowVisible(candidate) &&
                (extendedStyle & WsExToolWindow) == 0 &&
                GetWindowTextLength(candidate) > 0)
            {
                return candidate;
            }

            candidate = GetWindow(candidate, GwHwndPrevious);
        }

        return nearestWindowAboveDesktop != IntPtr.Zero && nearestWindowAboveDesktop != _windowHandle
            ? nearestWindowAboveDesktop
            : HwndNotTopmost;
    }

    private sealed class DesktopItem : INotifyPropertyChanged
    {
        private ImageSource? _icon;

        public DesktopItem(string name, string path, ImageSource? icon)
        {
            Name = name;
            Path = path;
            _icon = icon;
        }

        public string Name { get; }
        public string Path { get; }

        public ImageSource? Icon
        {
            get => _icon;
            set
            {
                if (ReferenceEquals(_icon, value))
                    return;

                _icon = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr IconHandle;
        public int IconIndex;
        public uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public IntPtr WindowHandle;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr IconHandle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;

        public uint State;
        public uint StateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;

        public uint TimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;

        public uint InfoFlags;
        public Guid GuidItem;
        public IntPtr BalloonIconHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private delegate IntPtr LowLevelMouseProc(int code, IntPtr wParam, IntPtr lParam);
    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr newLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(
        IntPtr parent,
        IntPtr childAfter,
        string className,
        string? windowName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr window);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string path,
        uint fileAttributes,
        out ShFileInfo fileInfo,
        uint fileInfoSize,
        uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr iconHandle);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIcon(
        IntPtr instance,
        int width,
        int height,
        byte planes,
        byte bitsPixel,
        byte[] andBits,
        byte[] xorBits);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, uint newItemId, string newItem);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenu(
        IntPtr menu,
        uint flags,
        int x,
        int y,
        int reserved,
        IntPtr window,
        IntPtr rectangle);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int hookId,
        LowLevelMouseProc callback,
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
}
