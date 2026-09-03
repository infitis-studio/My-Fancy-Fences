using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MahApps.Metro.IconPacks;
using Microsoft.Win32;

namespace My_Fancy_Fences;

public partial class PanelsWindow : Window
{
    private const double InitialWidthRatio = 0.683;
    private const double InitialHeightRatio = 0.795;
    private bool _hasCheckedForUpdates;
    private string? _latestReleaseUrl;
    private UpdateCheckResult? _latestUpdate;
    private WallpaperWindow? _embeddedWallpaperWindow;
    private bool _appearanceHideHeader;
    private Color _appearanceBackgroundColor;
    private double _appearanceBackgroundOpacity;
    private double _appearanceBorderRadius;
    private double _appearanceBorderThickness;
    private Color _appearanceBorderColor;
    private double _appearanceBorderOpacity;
    private string _appearanceFontFamilyName = "Segoe UI Variable Text";
    private Color _appearanceFontColor;
    private double _appearanceFontOpacity;
    private bool _appearanceFontBold;
    private double _appearanceLetterSpacing;
    private string _appearanceIconFontFamilyName = "Segoe UI Variable Text";
    private Color _appearanceIconFontColor;
    private double _appearanceIconFontOpacity;
    private bool _appearanceIconFontBold;
    private double _appearanceIconLetterSpacing;
    private double _appearanceIconSize;
    private AppearanceState _committedAppearance = null!;
    private bool _suppressAppearanceEvents;
    private bool _isResizing;
    private Point _resizeStartScreenPosition;
    private double _resizeStartWidth;
    private double _resizeStartHeight;
    private bool _suppressLayoutSelection;
    private IReadOnlyList<LayoutOverviewItem> _layouts = [];
    private string? _activeLayoutId;
    private LayoutManageWindow? _layoutManageWindow;
    private IReadOnlyDictionary<string, KeyboardShortcut> _layoutShortcutAssignments =
        new Dictionary<string, KeyboardShortcut>();
    private readonly DispatcherTimer _panelsSmoothScrollTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(16)
    };
    private double _panelsSmoothScrollTarget;
    private readonly Func<string, bool, Task<ConfigurationArchiveResult>> _exportConfiguration;
    private readonly Func<string, bool, string?, Task<ConfigurationArchiveResult>> _importConfiguration;

    public event EventHandler<PanelVisibilityChangedEventArgs>? PanelVisibilityChanged;
    public event EventHandler<PanelEditRequestedEventArgs>? EditPanelRequested;
    public event EventHandler? NewPanelRequested;
    public event EventHandler<LayoutSelectedEventArgs>? ApplyLayoutRequested;
    public event EventHandler<LayoutRenameRequestedEventArgs>? RenameLayoutRequested;
    public event EventHandler<LayoutDeleteRequestedEventArgs>? DeleteLayoutRequested;
    public event EventHandler<LayoutDuplicateRequestedEventArgs>? DuplicateLayoutRequested;
    public event EventHandler? AddLayoutRequested;
    public event EventHandler<LayoutShortcutEditRequestedEventArgs>? LayoutShortcutEditRequested;
    public event EventHandler<LayoutShortcutDeleteRequestedEventArgs>? LayoutShortcutDeleteRequested;
    public event EventHandler<GlobalAppearanceEventArgs>? GlobalAppearanceChanged;
    public event EventHandler? RefreshIconsRequested;
    public event EventHandler<ActivationModeChangedEventArgs>? ActivationModeChanged;

    public PanelsWindow(
        IReadOnlyList<PanelOverviewItem> panels,
        IReadOnlyList<LayoutOverviewItem> layouts,
        string? activeLayoutId,
        IReadOnlyDictionary<string, KeyboardShortcut> layoutShortcuts,
        bool useDoubleClickToOpen,
        bool hideHeader,
        Color backgroundColor,
        double backgroundOpacity,
        double borderRadius,
        double borderThickness,
        Color borderColor,
        double borderOpacity,
        string fontFamilyName,
        Color fontColor,
        double fontOpacity,
        bool fontBold,
        double letterSpacing,
        string iconFontFamilyName,
        Color iconFontColor,
        double iconFontOpacity,
        bool iconFontBold,
        double iconLetterSpacing,
        double iconSize,
        Func<string, bool, Task<ConfigurationArchiveResult>> exportConfiguration,
        Func<string, bool, string?, Task<ConfigurationArchiveResult>> importConfiguration)
    {
        _exportConfiguration = exportConfiguration;
        _importConfiguration = importConfiguration;
        _appearanceHideHeader = hideHeader;
        _appearanceBackgroundColor = backgroundColor;
        _appearanceBackgroundOpacity = backgroundOpacity;
        _appearanceBorderRadius = borderRadius;
        _appearanceBorderThickness = borderThickness;
        _appearanceBorderColor = borderColor;
        _appearanceBorderOpacity = borderOpacity;
        _appearanceFontFamilyName = fontFamilyName;
        _appearanceFontColor = fontColor;
        _appearanceFontOpacity = fontOpacity;
        _appearanceFontBold = fontBold;
        _appearanceLetterSpacing = letterSpacing;
        _appearanceIconFontFamilyName = iconFontFamilyName;
        _appearanceIconFontColor = iconFontColor;
        _appearanceIconFontOpacity = iconFontOpacity;
        _appearanceIconFontBold = iconFontBold;
        _appearanceIconLetterSpacing = iconLetterSpacing;
        _appearanceIconSize = iconSize;
        _layoutShortcutAssignments = layoutShortcuts;
        _committedAppearance = CreateAppearanceStateFromFields();
        InitializeComponent();
        Icon = AppIconProvider.Image;
        ApplyInitialWindowBounds();
        _panelsSmoothScrollTimer.Tick += PanelsSmoothScrollTimer_Tick;
        DoubleClickActivationCheckBox.IsChecked = useDoubleClickToOpen;
        AppearanceHideHeaderCheckBox.IsChecked = hideHeader;
        InitializeAppearanceControls();
        LanguageComboBox.ItemsSource = LocalizationService.Languages;
        LanguageComboBox.SelectedValue = LocalizationService.CurrentLanguage;
        CurrentVersionText.Text = UpdateService.FormatVersion(UpdateService.CurrentVersion);
        UpdatePanels(panels);
        UpdateLayouts(layouts, activeLayoutId);
        RefreshLocalizedText();
        Closed += (_, _) =>
        {
            _panelsSmoothScrollTimer.Stop();
            _embeddedWallpaperWindow?.DisposeEmbedded();
            _embeddedWallpaperWindow = null;
            _layoutManageWindow?.Close();
            _layoutManageWindow = null;
        };

        _ = ApplyStartupUpdateStatusAsync();
    }

    private void ApplyInitialWindowBounds()
    {
        var workArea = SystemParameters.WorkArea;
        Width = Math.Clamp(workArea.Width * InitialWidthRatio, MinWidth, workArea.Width - 32);
        Height = Math.Clamp(workArea.Height * InitialHeightRatio, MinHeight, workArea.Height - 32);
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + (workArea.Height - Height) / 2;
    }

    private void RefreshLocalizedText()
    {
        Title = LocalizationService.T("Zarządzanie");

        GeneralTabButton.Content = LocalizationService.T("Ustawienia ogólne");
        PanelsTabButton.Content = LocalizationService.T("Panele");
        WallpaperTabButton.Content = LocalizationService.T("Tapeta");
        AppearanceTabButton.Content = LocalizationService.T("Wygląd");
        ImportExportTabButton.Content = LocalizationService.T("Import / eksport");
        ShortcutsTabButton.Content = LocalizationService.T("Skróty");
        UpdatesTabButton.Content = LocalizationService.T("Aktualizacja");

        GeneralHeaderText.Text = LocalizationService.T("Ustawienia ogólne");
        GeneralDescriptionText.Text = LocalizationService.T("Podstawowe narzędzia aplikacji");
        RefreshIconsTitleText.Text = LocalizationService.T("Odśwież ikony");
        RefreshIconsDescriptionText.Text = LocalizationService.T("Ponownie pobiera ikony skrótów we wszystkich panelach.");
        RefreshIconsButton.Content = LocalizationService.T("Odśwież ikony");
        OpeningItemsTitleText.Text = LocalizationService.T("Uruchamianie elementów");
        OpeningItemsDescriptionText.Text = LocalizationService.T("Domyślnie ikony uruchamiają się pojedynczym kliknięciem.");
        DoubleClickActivationCheckBox.Content = LocalizationService.T("Uruchamiaj dwuklikiem");
        LanguageTitleText.Text = LocalizationService.T("Język");
        LanguageDescriptionText.Text = LocalizationService.T("Język interfejsu");

        RefreshShortcutItems();
        _embeddedWallpaperWindow?.RefreshLocalizedText();
    }

    private async Task ApplyStartupUpdateStatusAsync()
    {
        var result = await UpdateService.CheckAsync();
        _latestUpdate = result;
        _latestReleaseUrl = result.ReleaseUrl;
        if (result.IsUpdateAvailable)
            ShowUpdateAvailableUi();
    }

    private void ShowUpdateAvailableUi()
    {
        FooterUpdateButton.Visibility = Visibility.Visible;
        UpdateStatusCard.Background = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x1B, 0x2C, 0x24));
        UpdateStatusCard.BorderBrush = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x35, 0x64, 0x4B));
        UpdateStatusIcon.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x83, 0xD6, 0xA5));
        UpdateStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0xD4, 0xF5, 0xE0));
        FooterUpdateButton.Background = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x24, 0x6B, 0x45));
        FooterUpdateButton.Tag = "UpdateAvailable";
        FooterUpdateText.Text = "NEW UPDATE";
        FooterUpdateText.FontWeight = FontWeights.SemiBold;
        FooterUpdateText.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0xE8, 0xF7, 0xEE));
        FooterUpdateText.Opacity = 0.86;
        FooterUpdateBellIcon.Visibility = Visibility.Visible;
    }

    private void OpenUpdatesTab() => UpdatesTabButton.IsChecked = true;

    private void FooterKoFiButton_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("https://ko-fi.com/infitisstudio#linkModal")
        {
            UseShellExecute = true
        });

    private void FooterUpdateButton_Click(object sender, RoutedEventArgs e) =>
        OpenUpdatesTab();

    public void UpdatePanels(IReadOnlyList<PanelOverviewItem> panels)
    {
        PanelsItemsControl.ItemsSource = panels;
        PanelCountText.Text = panels.Count switch
        {
            1 => "1 panel",
            2 or 3 or 4 => $"{panels.Count} panele",
            _ => $"{panels.Count} paneli"
        };
    }

    public void UpdateLayouts(IReadOnlyList<LayoutOverviewItem> layouts, string? activeLayoutId)
    {
        _layouts = layouts;
        _activeLayoutId = activeLayoutId;
        _suppressLayoutSelection = true;
        LayoutComboBox.ItemsSource = layouts;
        LayoutComboBox.SelectedValue = activeLayoutId;
        if (LayoutComboBox.SelectedItem is null && layouts.Count > 0)
        LayoutComboBox.SelectedIndex = 0;
        _suppressLayoutSelection = false;
        _layoutManageWindow?.UpdateLayouts(layouts);
        RefreshShortcutItems();
    }

    public void UpdateLayoutShortcuts(IReadOnlyDictionary<string, KeyboardShortcut> layoutShortcuts)
    {
        _layoutShortcutAssignments = layoutShortcuts;
        RefreshShortcutItems();
    }

    private async void SettingsTab_Checked(object sender, RoutedEventArgs e)
    {
        if (GeneralTabContent is null || PanelsTabContent is null ||
            WallpaperTabContent is null || AppearanceTabContent is null ||
            ImportExportTabContent is null || ShortcutsTabContent is null ||
            UpdatesTabContent is null)
            return;

        var selectedTab = (sender as FrameworkElement)?.Tag as string ?? "General";
        GeneralTabContent.Visibility = selectedTab == "General"
            ? Visibility.Visible
            : Visibility.Collapsed;
        PanelsTabContent.Visibility = selectedTab == "Panels"
            ? Visibility.Visible
            : Visibility.Collapsed;
        WallpaperTabContent.Visibility = selectedTab == "Wallpaper"
            ? Visibility.Visible
            : Visibility.Collapsed;
        AppearanceTabContent.Visibility = selectedTab == "Appearance"
            ? Visibility.Visible
            : Visibility.Collapsed;
        ImportExportTabContent.Visibility = selectedTab == "ImportExport"
            ? Visibility.Visible
            : Visibility.Collapsed;
        ShortcutsTabContent.Visibility = selectedTab == "Shortcuts"
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdatesTabContent.Visibility = selectedTab == "Updates"
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (selectedTab == "Wallpaper")
            await EnsureWallpaperTabLoadedAsync();

        if (selectedTab == "Updates" && !_hasCheckedForUpdates)
            await CheckForUpdatesAsync();
    }

    private void RefreshShortcutItems()
    {
        if (ShortcutsItemsControl is null)
            return;

        ShortcutsItemsControl.ItemsSource = _layouts
            .Select(layout =>
            {
                var hasShortcut = _layoutShortcutAssignments.TryGetValue(layout.Id, out var shortcut);
                var shortcutText = hasShortcut
                    ? $"{LocalizationService.T("Aktywny skrót klawiszowy")}: {shortcut!.DisplayText}"
                    : LocalizationService.T("Brak przypisanego skrótu");
                return new LayoutShortcutItem(
                    layout.Id,
                    $"{LocalizationService.T("Przełącz na układ")}: {LocalizationService.T(layout.Name)}",
                    shortcutText,
                    hasShortcut);
            })
            .ToList();
    }
    private void AppearanceOption_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _suppressAppearanceEvents)
            return;

        ReadAppearanceControls();
        UpdateAppearancePreviews();
        UpdateAppearanceHeaderControlsState();
        SetAppearanceActionButtonsVisible(true);
        RaiseGlobalAppearance(GlobalAppearancePhase.Preview);
    }

    private void AppearanceSectionTab_Checked(object sender, RoutedEventArgs e)
    {
        if (AppearanceBackgroundSection is null || AppearanceHeaderSection is null || AppearanceIconsSection is null)
            return;

        AppearanceBackgroundSection.Visibility = Visibility.Visible;
        AppearanceHeaderSection.Visibility = Visibility.Visible;
        AppearanceIconsSection.Visibility = Visibility.Visible;
    }

    private void InitializeAppearanceControls()
    {
        _suppressAppearanceEvents = true;

        var fonts = Fonts.SystemFontFamilies
            .Select(font => font.Source)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        AppearanceHeaderFontComboBox.ItemsSource = fonts;
        AppearanceIconFontComboBox.ItemsSource = fonts;
        AppearanceHeaderFontComboBox.SelectedItem = fonts.FirstOrDefault(name =>
            string.Equals(name, _appearanceFontFamilyName, StringComparison.CurrentCultureIgnoreCase))
            ?? fonts.FirstOrDefault(name => name.StartsWith("Segoe UI", StringComparison.CurrentCultureIgnoreCase))
            ?? fonts.FirstOrDefault();
        AppearanceIconFontComboBox.SelectedItem = fonts.FirstOrDefault(name =>
            string.Equals(name, _appearanceIconFontFamilyName, StringComparison.CurrentCultureIgnoreCase))
            ?? fonts.FirstOrDefault(name => name.StartsWith("Segoe UI", StringComparison.CurrentCultureIgnoreCase))
            ?? fonts.FirstOrDefault();

        AppearanceHideHeaderCheckBox.IsChecked = _appearanceHideHeader;
        AppearanceBoldFontCheckBox.IsChecked = _appearanceFontBold;
        AppearanceIconBoldFontCheckBox.IsChecked = _appearanceIconFontBold;
        AppearanceBorderRadiusSlider.Value = Math.Clamp(_appearanceBorderRadius, AppearanceBorderRadiusSlider.Minimum, AppearanceBorderRadiusSlider.Maximum);
        AppearanceBorderThicknessSlider.Value = Math.Clamp(_appearanceBorderThickness, AppearanceBorderThicknessSlider.Minimum, AppearanceBorderThicknessSlider.Maximum);
        AppearanceLetterSpacingSlider.Value = Math.Clamp(_appearanceLetterSpacing, AppearanceLetterSpacingSlider.Minimum, AppearanceLetterSpacingSlider.Maximum);
        AppearanceIconLetterSpacingSlider.Value = Math.Clamp(_appearanceIconLetterSpacing, AppearanceIconLetterSpacingSlider.Minimum, AppearanceIconLetterSpacingSlider.Maximum);
        AppearanceIconSizeSlider.Value = Math.Clamp(_appearanceIconSize, AppearanceIconSizeSlider.Minimum, AppearanceIconSizeSlider.Maximum);

        UpdateAppearanceValueTexts();
        UpdateAppearancePreviews();
        UpdateAppearanceHeaderControlsState();
        AppearanceBackgroundSection.Visibility = Visibility.Visible;
        AppearanceHeaderSection.Visibility = Visibility.Visible;
        AppearanceIconsSection.Visibility = Visibility.Visible;
        _suppressAppearanceEvents = false;
    }

    private void UpdateAppearanceValueTexts()
    {
        AppearanceBorderRadiusValueText.Text = Math.Round(AppearanceBorderRadiusSlider.Value).ToString(CultureInfo.CurrentCulture);
        AppearanceBorderThicknessValueText.Text = AppearanceBorderThicknessSlider.Value.ToString("0.#", CultureInfo.CurrentCulture);
        AppearanceLetterSpacingValueText.Text = AppearanceLetterSpacingSlider.Value.ToString("0.##", CultureInfo.CurrentCulture);
        AppearanceIconLetterSpacingValueText.Text = AppearanceIconLetterSpacingSlider.Value.ToString("0.##", CultureInfo.CurrentCulture);
        AppearanceIconSizeValueText.Text = Math.Round(AppearanceIconSizeSlider.Value).ToString(CultureInfo.CurrentCulture);
    }

    private void AppearanceSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (AppearanceBorderRadiusValueText is null)
            return;

        UpdateAppearanceValueTexts();
        AppearanceOption_Changed(sender, e);
    }

    private void AppearancePixelValueTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        ApplyAppearanceTextBoxValue(sender as TextBox);
        e.Handled = true;
    }

    private void AppearancePixelValueTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        ApplyAppearanceTextBoxValue(sender as TextBox);

    private void ApplyAppearanceTextBoxValue(TextBox? textBox)
    {
        if (textBox is null)
            return;

        var slider = textBox.Name switch
        {
            nameof(AppearanceBorderRadiusValueText) => AppearanceBorderRadiusSlider,
            nameof(AppearanceBorderThicknessValueText) => AppearanceBorderThicknessSlider,
            nameof(AppearanceLetterSpacingValueText) => AppearanceLetterSpacingSlider,
            nameof(AppearanceIconLetterSpacingValueText) => AppearanceIconLetterSpacingSlider,
            nameof(AppearanceIconSizeValueText) => AppearanceIconSizeSlider,
            _ => null
        };

        if (slider is null)
            return;

        if (!double.TryParse(textBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            UpdateAppearanceValueTexts();
            return;
        }

        slider.Value = Math.Clamp(value, slider.Minimum, slider.Maximum);
        UpdateAppearanceValueTexts();
        AppearanceOption_Changed(slider, new RoutedEventArgs());
    }

    private void ReadAppearanceControls()
    {
        _appearanceHideHeader = AppearanceHideHeaderCheckBox.IsChecked == true;
        _appearanceBorderRadius = AppearanceBorderRadiusSlider.Value;
        _appearanceBorderThickness = AppearanceBorderThicknessSlider.Value;
        _appearanceFontFamilyName = AppearanceHeaderFontComboBox.SelectedItem as string ?? "Segoe UI Variable Text";
        _appearanceFontBold = AppearanceBoldFontCheckBox.IsChecked == true;
        _appearanceLetterSpacing = AppearanceLetterSpacingSlider.Value;
        _appearanceIconFontFamilyName = AppearanceIconFontComboBox.SelectedItem as string ?? "Segoe UI Variable Text";
        _appearanceIconFontBold = AppearanceIconBoldFontCheckBox.IsChecked == true;
        _appearanceIconLetterSpacing = AppearanceIconLetterSpacingSlider.Value;
        _appearanceIconSize = AppearanceIconSizeSlider.Value;
    }

    private void UpdateAppearanceHeaderControlsState()
    {
        if (AppearanceHeaderControls is null)
            return;

        var enabled = AppearanceHideHeaderCheckBox.IsChecked != true;
        AppearanceHeaderControls.IsEnabled = enabled;
        AppearanceHeaderControls.Opacity = enabled ? 1 : 0.42;
    }

    private void AppearanceBackgroundColorPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        PickAppearanceColor(
            _appearanceBackgroundColor,
            _appearanceBackgroundOpacity,
            (color, opacity) =>
            {
                _appearanceBackgroundColor = color;
                _appearanceBackgroundOpacity = opacity;
            });
    }

    private void AppearanceBorderColorPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        PickAppearanceColor(
            _appearanceBorderColor,
            _appearanceBorderOpacity,
            (color, opacity) =>
            {
                _appearanceBorderColor = color;
                _appearanceBorderOpacity = opacity;
            });
    }

    private void AppearanceFontColorPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        PickAppearanceColor(
            _appearanceFontColor,
            _appearanceFontOpacity,
            (color, opacity) =>
            {
                _appearanceFontColor = color;
                _appearanceFontOpacity = opacity;
            });
    }

    private void AppearanceIconFontColorPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        PickAppearanceColor(
            _appearanceIconFontColor,
            _appearanceIconFontOpacity,
            (color, opacity) =>
            {
                _appearanceIconFontColor = color;
                _appearanceIconFontOpacity = opacity;
            });
    }

    private void PickAppearanceColor(Color initialColor, double initialOpacity, Action<Color, double> apply)
    {
        var before = CreateAppearanceStateFromFields();
        var picker = new ColorPickerWindow(initialColor, initialOpacity)
        {
            Owner = this
        };

        picker.PreviewChanged += (_, preview) =>
        {
            apply(preview.Color, preview.Opacity);
            UpdateAppearancePreviews();
            SetAppearanceActionButtonsVisible(true);
            RaiseGlobalAppearance(GlobalAppearancePhase.Preview);
        };

        if (picker.ShowDialog() != true)
        {
            ApplyAppearanceStateToFields(before);
            UpdateAppearancePreviews();
            RaiseGlobalAppearance(GlobalAppearancePhase.Preview);
            return;
        }

        apply(picker.SelectedColor, picker.SelectedOpacity);
        UpdateAppearancePreviews();
        SetAppearanceActionButtonsVisible(true);
        RaiseGlobalAppearance(GlobalAppearancePhase.Preview);
    }

    private void SaveAppearanceButton_Click(object sender, RoutedEventArgs e)
    {
        ReadAppearanceControls();
        _committedAppearance = CreateAppearanceStateFromFields();
        SetAppearanceActionButtonsVisible(false);
        RaiseGlobalAppearance(GlobalAppearancePhase.Commit);
    }

    private void CancelAppearanceButton_Click(object sender, RoutedEventArgs e)
    {
        RaiseGlobalAppearance(GlobalAppearancePhase.Cancel);
        ApplyAppearanceStateToFields(_committedAppearance);
        _suppressAppearanceEvents = true;
        InitializeAppearanceControls();
        _suppressAppearanceEvents = false;
        SetAppearanceActionButtonsVisible(false);
    }

    private void SetAppearanceActionButtonsVisible(bool visible)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        CancelAppearanceButton.Visibility = visibility;
        SaveAppearanceButton.Visibility = visibility;
    }

    private void UpdateAppearancePreviews()
    {
        AppearanceBackgroundColorText.Text =
            $"#{_appearanceBackgroundColor.R:X2}{_appearanceBackgroundColor.G:X2}{_appearanceBackgroundColor.B:X2}";
        AppearanceBackgroundColorPreview.Background = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Round(_appearanceBackgroundOpacity * 255),
            _appearanceBackgroundColor.R,
            _appearanceBackgroundColor.G,
            _appearanceBackgroundColor.B));

        AppearanceBorderColorText.Text =
            $"#{_appearanceBorderColor.R:X2}{_appearanceBorderColor.G:X2}{_appearanceBorderColor.B:X2}";
        AppearanceBorderColorPreview.Background = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Round(_appearanceBorderOpacity * 255),
            _appearanceBorderColor.R,
            _appearanceBorderColor.G,
            _appearanceBorderColor.B));

        AppearanceFontColorText.Text =
            $"#{_appearanceFontColor.R:X2}{_appearanceFontColor.G:X2}{_appearanceFontColor.B:X2}";
        AppearanceFontColorPreview.Background = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Round(_appearanceFontOpacity * 255),
            _appearanceFontColor.R,
            _appearanceFontColor.G,
            _appearanceFontColor.B));

        AppearanceIconFontColorText.Text =
            $"#{_appearanceIconFontColor.R:X2}{_appearanceIconFontColor.G:X2}{_appearanceIconFontColor.B:X2}";
        AppearanceIconFontColorPreview.Background = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Round(_appearanceIconFontOpacity * 255),
            _appearanceIconFontColor.R,
            _appearanceIconFontColor.G,
            _appearanceIconFontColor.B));
    }

    private void RaiseGlobalAppearance(GlobalAppearancePhase phase) =>
        GlobalAppearanceChanged?.Invoke(this, new GlobalAppearanceEventArgs(
            phase,
            AppearanceHideHeaderCheckBox.IsChecked == true,
            _appearanceBackgroundColor,
            _appearanceBackgroundOpacity,
            _appearanceBorderRadius,
            _appearanceBorderThickness,
            _appearanceBorderColor,
            _appearanceBorderOpacity,
            _appearanceFontFamilyName,
            _appearanceFontColor,
            _appearanceFontOpacity,
            _appearanceFontBold,
            _appearanceLetterSpacing,
            _appearanceIconFontFamilyName,
            _appearanceIconFontColor,
            _appearanceIconFontOpacity,
            _appearanceIconFontBold,
            _appearanceIconLetterSpacing,
            _appearanceIconSize));

    private void ResetAppearanceBackgroundButton_Click(object sender, RoutedEventArgs e)
    {
        _appearanceBackgroundColor = Color.FromRgb(0x0B, 0x0E, 0x12);
        _appearanceBackgroundOpacity = 0.58;
        _appearanceBorderRadius = 11;
        _appearanceBorderThickness = 0;
        _appearanceBorderColor = Colors.White;
        _appearanceBorderOpacity = 0;
        InitializeAppearanceControls();
        SetAppearanceActionButtonsVisible(true);
        RaiseGlobalAppearance(GlobalAppearancePhase.Preview);
    }

    private void ResetAppearanceHeaderButton_Click(object sender, RoutedEventArgs e)
    {
        _appearanceHideHeader = false;
        _appearanceFontFamilyName = "Segoe UI Variable Text";
        _appearanceFontColor = Color.FromRgb(0xF7, 0xF9, 0xFC);
        _appearanceFontOpacity = 1;
        _appearanceFontBold = false;
        _appearanceLetterSpacing = 0;
        InitializeAppearanceControls();
        SetAppearanceActionButtonsVisible(true);
        RaiseGlobalAppearance(GlobalAppearancePhase.Preview);
    }

    private void ResetAppearanceIconsButton_Click(object sender, RoutedEventArgs e)
    {
        _appearanceIconFontFamilyName = "Segoe UI Variable Text";
        _appearanceIconFontColor = Color.FromRgb(0xF7, 0xF9, 0xFC);
        _appearanceIconFontOpacity = 1;
        _appearanceIconFontBold = false;
        _appearanceIconLetterSpacing = 0;
        _appearanceIconSize = 42;
        InitializeAppearanceControls();
        SetAppearanceActionButtonsVisible(true);
        RaiseGlobalAppearance(GlobalAppearancePhase.Preview);
    }

    private AppearanceState CreateAppearanceStateFromFields() =>
        new(
            _appearanceHideHeader,
            _appearanceBackgroundColor,
            _appearanceBackgroundOpacity,
            _appearanceBorderRadius,
            _appearanceBorderThickness,
            _appearanceBorderColor,
            _appearanceBorderOpacity,
            _appearanceFontFamilyName,
            _appearanceFontColor,
            _appearanceFontOpacity,
            _appearanceFontBold,
            _appearanceLetterSpacing,
            _appearanceIconFontFamilyName,
            _appearanceIconFontColor,
            _appearanceIconFontOpacity,
            _appearanceIconFontBold,
            _appearanceIconLetterSpacing,
            _appearanceIconSize);

    private void ApplyAppearanceStateToFields(AppearanceState state)
    {
        _appearanceHideHeader = state.HideHeader;
        _appearanceBackgroundColor = state.BackgroundColor;
        _appearanceBackgroundOpacity = state.BackgroundOpacity;
        _appearanceBorderRadius = state.BorderRadius;
        _appearanceBorderThickness = state.BorderThickness;
        _appearanceBorderColor = state.BorderColor;
        _appearanceBorderOpacity = state.BorderOpacity;
        _appearanceFontFamilyName = state.FontFamilyName;
        _appearanceFontColor = state.FontColor;
        _appearanceFontOpacity = state.FontOpacity;
        _appearanceFontBold = state.FontBold;
        _appearanceLetterSpacing = state.LetterSpacing;
        _appearanceIconFontFamilyName = state.IconFontFamilyName;
        _appearanceIconFontColor = state.IconFontColor;
        _appearanceIconFontOpacity = state.IconFontOpacity;
        _appearanceIconFontBold = state.IconFontBold;
        _appearanceIconLetterSpacing = state.IconLetterSpacing;
        _appearanceIconSize = state.IconSize;
    }

    private async Task EnsureWallpaperTabLoadedAsync()
    {
        if (_embeddedWallpaperWindow is null)
        {
            _embeddedWallpaperWindow = new WallpaperWindow(embedded: true);
            WallpaperContentHost.Content = _embeddedWallpaperWindow.DetachForEmbedding();
        }

        _embeddedWallpaperWindow.RefreshLocalizedText();
        await _embeddedWallpaperWindow.InitializeEmbeddedAsync();
    }

    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e) =>
        await CheckForUpdatesAsync(force: true);

    public void SelectTab(string tab)
    {
        switch (tab)
        {
            case "Wallpaper":
                WallpaperTabButton.IsChecked = true;
                break;
            case "Panels":
                break;
            case "Updates":
                UpdatesTabButton.IsChecked = true;
                break;
        }
    }

    private async void OpenReleaseButton_Click(object sender, RoutedEventArgs e)
    {
        var update = _latestUpdate;
        if (update is null || !update.IsUpdateAvailable)
        {
            await CheckForUpdatesAsync(force: true);
            update = _latestUpdate;
        }

        if (update is null || !update.IsUpdateAvailable)
            return;

        var confirmation = new ConfirmationWindow(
            LocalizationService.T("Nowa wersja jest gotowa"),
            LocalizationService.T("Program pobierze aktualny wariant aplikacji, zamknie się, zainstaluje nową wersję i uruchomi ponownie.\n\nCzy rozpocząć automatyczną aktualizację?"),
            LocalizationService.T("Aktualizuj"),
            LocalizationService.T("Nie teraz"),
            positiveConfirm: true)
        {
            Owner = this
        };

        if (confirmation.ShowDialog() != true)
            return;

        OpenReleaseButton.IsEnabled = false;
        CheckUpdatesButton.IsEnabled = false;
        LatestReleaseLinkButton.IsEnabled = false;
        try
        {
            var progress = new Progress<double>(value =>
                UpdateStatusText.Text = $"{LocalizationService.T("Pobieranie aktualizacji…")} {value:P0}");
            await ApplicationUpdater.PrepareUpdateAsync(update, progress);
            UpdateStatusText.Text = LocalizationService.T("Instalowanie aktualizacji…");
            Application.Current.Shutdown();
        }
        catch (Exception exception)
        {
            UpdateStatusText.Text = LocalizationService.T("Nie udało się zainstalować aktualizacji.");
            var error = new ConfirmationWindow(
                LocalizationService.T("Aktualizacja nie powiodła się"),
                exception.Message,
                LocalizationService.T("OK"))
            {
                Owner = this
            };
            error.ShowDialog();
            OpenReleaseButton.IsEnabled = true;
            CheckUpdatesButton.IsEnabled = true;
            LatestReleaseLinkButton.IsEnabled = true;
        }
    }

    private void LatestReleaseLinkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_latestReleaseUrl))
            Process.Start(new ProcessStartInfo(_latestReleaseUrl) { UseShellExecute = true });
    }

    private async Task CheckForUpdatesAsync(bool force = false)
    {
        CheckUpdatesButton.IsEnabled = false;
        OpenReleaseButton.Visibility = Visibility.Collapsed;
        UpdateStatusText.Text = LocalizationService.T("Sprawdzanie aktualizacji…");
        LatestVersionText.Text = LocalizationService.T("sprawdzanie…");

        try
        {
            var result = await UpdateService.CheckAsync(force);
            _latestUpdate = result;
            _latestReleaseUrl = result.ReleaseUrl;
            LatestVersionText.Text = result.LatestVersion is not null
                ? UpdateService.FormatVersion(result.LatestVersion)
                : result.LatestTag;
            LatestReleaseLinkButton.Visibility = string.IsNullOrWhiteSpace(_latestReleaseUrl)
                ? Visibility.Collapsed
                : Visibility.Visible;

            if (!result.Success || result.LatestVersion is null)
            {
                FooterUpdateButton.Visibility = Visibility.Collapsed;
                UpdateStatusText.Text = LocalizationService.T("Nie udało się połączyć z GitHubem");
                LatestVersionText.Text = "—";
                LatestReleaseLinkButton.Visibility = Visibility.Collapsed;
            }
            else if (result.IsUpdateAvailable)
            {
                UpdateStatusText.Text = LocalizationService.T("Dostępna jest nowa wersja");
                OpenReleaseButton.Visibility = Visibility.Visible;
                ShowUpdateAvailableUi();
            }
            else
            {
                FooterUpdateButton.Visibility = Visibility.Collapsed;
                UpdateStatusText.Text = LocalizationService.T("Masz najnowszą wersję");
            }

            _hasCheckedForUpdates = true;
        }
        catch (Exception)
        {
            _latestUpdate = null;
            FooterUpdateButton.Visibility = Visibility.Collapsed;
            UpdateStatusText.Text = LocalizationService.T("Nie udało się połączyć z GitHubem");
            LatestVersionText.Text = "—";
            LatestReleaseLinkButton.Visibility = Visibility.Collapsed;
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void PanelsScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer || scrollViewer.ScrollableHeight <= 0)
            return;

        var direction = e.Delta > 0 ? -1 : 1;
        var distance = Math.Max(80, Math.Abs(e.Delta) * 0.72);
        _panelsSmoothScrollTarget = Math.Clamp(
            _panelsSmoothScrollTarget <= 0 ? scrollViewer.VerticalOffset + direction * distance : _panelsSmoothScrollTarget + direction * distance,
            0,
            scrollViewer.ScrollableHeight);

        _panelsSmoothScrollTimer.Start();
        e.Handled = true;
    }

    private void PanelsSmoothScrollTimer_Tick(object? sender, EventArgs e)
    {
        var current = PanelsScrollViewer.VerticalOffset;
        var delta = _panelsSmoothScrollTarget - current;

        if (Math.Abs(delta) < 0.7)
        {
            PanelsScrollViewer.ScrollToVerticalOffset(_panelsSmoothScrollTarget);
            _panelsSmoothScrollTimer.Stop();
            return;
        }

        PanelsScrollViewer.ScrollToVerticalOffset(current + delta * 0.22);
    }

    private void ResizeGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
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

    private void ResizeGrip_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isResizing || e.LeftButton != MouseButtonState.Pressed)
            return;

        var currentScreenPosition = PointToScreen(e.GetPosition(this));
        Width = Math.Max(MinWidth, _resizeStartWidth + currentScreenPosition.X - _resizeStartScreenPosition.X);
        Height = Math.Max(MinHeight, _resizeStartHeight + currentScreenPosition.Y - _resizeStartScreenPosition.Y);
    }

    private void ResizeGrip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isResizing)
            return;

        _isResizing = false;
        ((UIElement)sender).ReleaseMouseCapture();
        e.Handled = true;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void PanelVisibilityButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button ||
            button.Tag is not string panelKey ||
            button.DataContext is not PanelOverviewItem panel)
        {
            return;
        }

        PanelVisibilityChanged?.Invoke(
            this,
            new PanelVisibilityChangedEventArgs(panelKey, !panel.IsHidden));
    }

    private void EditPanelButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string panelKey })
            EditPanelRequested?.Invoke(this, new PanelEditRequestedEventArgs(panelKey));
    }

    private void AddPanelButton_Click(object sender, RoutedEventArgs e) =>
        NewPanelRequested?.Invoke(this, EventArgs.Empty);

    private void SaveLayoutButton_Click(object sender, RoutedEventArgs e)
    {
    }

    private void AssignShortcutButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string layoutId })
            return;

        LayoutShortcutEditRequested?.Invoke(this, new LayoutShortcutEditRequestedEventArgs(layoutId));
    }

    private void DeleteShortcutButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string layoutId })
            return;

        LayoutShortcutDeleteRequested?.Invoke(this, new LayoutShortcutDeleteRequestedEventArgs(layoutId));
    }

    private void ApplyLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        if (_layoutManageWindow is null)
        {
            _layoutManageWindow = new LayoutManageWindow(_layouts)
            {
                Owner = this
            };
            _layoutManageWindow.RenameRequested += (_, args) =>
                RenameLayoutRequested?.Invoke(this, args);
            _layoutManageWindow.DeleteRequested += (_, args) =>
                DeleteLayoutRequested?.Invoke(this, args);
            _layoutManageWindow.DuplicateRequested += (_, args) =>
                DuplicateLayoutRequested?.Invoke(this, args);
            _layoutManageWindow.AddRequested += (_, _) =>
                AddLayoutRequested?.Invoke(this, EventArgs.Empty);
            _layoutManageWindow.Closed += (_, _) => _layoutManageWindow = null;
        }
        else
        {
            _layoutManageWindow.UpdateLayouts(_layouts);
        }

        if (!_layoutManageWindow.IsVisible)
            _layoutManageWindow.Show();

        _layoutManageWindow.Topmost = true;
        _layoutManageWindow.Topmost = false;
        _layoutManageWindow.Activate();
        _layoutManageWindow.Focus();
    }

    private void LayoutComboBox_SelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressLayoutSelection || LayoutComboBox.SelectedValue is not string layoutId)
            return;

        if (string.Equals(layoutId, _activeLayoutId, StringComparison.Ordinal))
            return;

        ApplyLayoutRequested?.Invoke(this, new LayoutSelectedEventArgs(layoutId));
    }

    private void RefreshIconsButton_Click(object sender, RoutedEventArgs e) =>
        RefreshIconsRequested?.Invoke(this, EventArgs.Empty);

    private async void ExportConfigurationButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = LocalizationService.T("Eksport konfiguracji My Fancy Fences"),
            Filter = "Archiwum ZIP (*.zip)|*.zip",
            DefaultExt = ".zip",
            AddExtension = true,
            FileName = $"My-Fancy-Fences-config-{DateTime.Now:yyyy-MM-dd}.zip",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var button = (System.Windows.Controls.Button)sender;
        button.IsEnabled = false;
        ArchiveStatusText.Text = LocalizationService.T("Tworzenie archiwum…");
        try
        {
            var result = await _exportConfiguration(
                dialog.FileName,
                ExportShortcutsCheckBox.IsChecked == true);
            ArchiveStatusText.Text =
                $"{LocalizationService.T("Zapisano ZIP")}: {result.ArchivePath}\n{LocalizationService.T("Panele")}: {result.PanelCount}, {LocalizationService.T("skróty")}: {result.ShortcutCount}.";
        }
        catch (Exception exception)
        {
            ArchiveStatusText.Text = $"{LocalizationService.T("Eksport nie powiódł się")}: {exception.Message}";
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private async void ImportConfigurationButton_Click(object sender, RoutedEventArgs e)
    {
        var archiveDialog = new OpenFileDialog
        {
            Title = LocalizationService.T("Import konfiguracji My Fancy Fences"),
            Filter = "Archiwum ZIP (*.zip)|*.zip",
            CheckFileExists = true,
            Multiselect = false
        };
        if (archiveDialog.ShowDialog(this) != true)
            return;

        var importShortcuts = ImportShortcutsCheckBox.IsChecked == true;

        var confirmation = new ConfirmationWindow(
            LocalizationService.T("Zaimportować konfigurację?"),
            LocalizationService.T("Obecne ustawienia zostaną zastąpione zawartością archiwum. Przed zmianą powstanie kopia zapasowa, a aplikacja uruchomi się ponownie."),
            LocalizationService.T("Importuj"),
            LocalizationService.T("Anuluj"))
        {
            Owner = this
        };
        if (confirmation.ShowDialog() != true)
            return;

        var button = (System.Windows.Controls.Button)sender;
        button.IsEnabled = false;
        ArchiveStatusText.Text = LocalizationService.T("Importowanie konfiguracji…");
        try
        {
            await _importConfiguration(
                archiveDialog.FileName,
                importShortcuts,
                null);
            ArchiveStatusText.Text = LocalizationService.T("Import zakończony. Ponowne uruchamianie…");
        }
        catch (Exception exception)
        {
            ArchiveStatusText.Text = $"{LocalizationService.T("Import nie powiódł się")}: {exception.Message}";
            button.IsEnabled = true;
        }
    }

    private void DoubleClickActivationCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;

        ActivationModeChanged?.Invoke(
            this,
            new ActivationModeChangedEventArgs(DoubleClickActivationCheckBox.IsChecked == true));
    }

    private void LanguageComboBox_SelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded || LanguageComboBox.SelectedValue is not string languageCode)
            return;

        LocalizationService.SetLanguage(languageCode);
        RefreshLocalizedText();
        UpdateLayouts(_layouts, _activeLayoutId);
        _layoutManageWindow?.UpdateLayouts(_layouts);
    }
}

public sealed record PanelOverviewItem(
    string PanelKey,
    string Title,
    PackIconLucideKind Icon,
    string FolderPath,
    string Details,
    string Status,
    bool IsHidden)
{
    public Visibility EditVisibility => IsHidden ? Visibility.Collapsed : Visibility.Visible;
}

public sealed record PanelVisibilityChangedEventArgs(string PanelKey, bool IsHidden);

public sealed record PanelEditRequestedEventArgs(string PanelKey);

public sealed record ActivationModeChangedEventArgs(bool UseDoubleClickToOpen);

public sealed record LayoutOverviewItem(string Id, string Name, bool IsActive)
{
    public string DisplayName
    {
        get
        {
            var name = LocalizationService.T(Name);
            return IsActive ? $"{name} — {LocalizationService.T("aktywny")}" : name;
        }
    }
}

public sealed record LayoutShortcutItem(
    string LayoutId,
    string Title,
    string ShortcutText,
    bool HasShortcut)
{
    public Visibility AssignVisibility => HasShortcut ? Visibility.Collapsed : Visibility.Visible;

    public Visibility EditDeleteVisibility => HasShortcut ? Visibility.Visible : Visibility.Collapsed;

    public Brush ShortcutBrush => HasShortcut
        ? new SolidColorBrush(Color.FromRgb(0x8E, 0xD6, 0xAD))
        : new SolidColorBrush(Color.FromRgb(0xA9, 0xA2, 0xA5));
}

public sealed record LayoutSelectedEventArgs(string LayoutId);

public sealed record LayoutDuplicateRequestedEventArgs(string LayoutId);

public sealed record LayoutShortcutEditRequestedEventArgs(string LayoutId);

public sealed record LayoutShortcutDeleteRequestedEventArgs(string LayoutId);

internal sealed record AppearanceState(
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

