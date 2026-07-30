using System.Diagnostics;
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
    private bool _hasCheckedForUpdates;
    private string? _latestReleaseUrl;
    private UpdateCheckResult? _latestUpdate;
    private WallpaperWindow? _embeddedWallpaperWindow;
    private bool _appearanceHideHeader;
    private Color _appearanceBackgroundColor;
    private double _appearanceBackgroundOpacity;
    private bool _suppressAppearanceEvents;
    private bool _isResizing;
    private Point _resizeStartScreenPosition;
    private double _resizeStartWidth;
    private double _resizeStartHeight;
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
    public event EventHandler<GlobalAppearanceEventArgs>? GlobalAppearanceChanged;
    public event EventHandler? RefreshIconsRequested;
    public event EventHandler<ActivationModeChangedEventArgs>? ActivationModeChanged;

    public PanelsWindow(
        IReadOnlyList<PanelOverviewItem> panels,
        bool useDoubleClickToOpen,
        bool hideHeader,
        Color backgroundColor,
        double backgroundOpacity,
        Func<string, bool, Task<ConfigurationArchiveResult>> exportConfiguration,
        Func<string, bool, string?, Task<ConfigurationArchiveResult>> importConfiguration)
    {
        _exportConfiguration = exportConfiguration;
        _importConfiguration = importConfiguration;
        _appearanceHideHeader = hideHeader;
        _appearanceBackgroundColor = backgroundColor;
        _appearanceBackgroundOpacity = backgroundOpacity;
        InitializeComponent();
        Icon = AppIconProvider.Image;
        _panelsSmoothScrollTimer.Tick += PanelsSmoothScrollTimer_Tick;
        DoubleClickActivationCheckBox.IsChecked = useDoubleClickToOpen;
        AppearanceHideHeaderCheckBox.IsChecked = hideHeader;
        UpdateAppearanceColorPreview();
        LanguageComboBox.ItemsSource = LocalizationService.Languages;
        LanguageComboBox.SelectedValue = LocalizationService.CurrentLanguage;
        CurrentVersionText.Text = UpdateService.FormatVersion(UpdateService.CurrentVersion);
        UpdatePanels(panels);
        Closed += (_, _) =>
        {
            _panelsSmoothScrollTimer.Stop();
            _embeddedWallpaperWindow?.DisposeEmbedded();
            _embeddedWallpaperWindow = null;
        };

        _ = ApplyStartupUpdateStatusAsync();
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

    private async void SettingsTab_Checked(object sender, RoutedEventArgs e)
    {
        if (GeneralTabContent is null || PanelsTabContent is null ||
            WallpaperTabContent is null || AppearanceTabContent is null ||
            ImportExportTabContent is null || UpdatesTabContent is null)
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
        UpdatesTabContent.Visibility = selectedTab == "Updates"
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (selectedTab == "Wallpaper")
            await EnsureWallpaperTabLoadedAsync();

        if (selectedTab == "Updates" && !_hasCheckedForUpdates)
            await CheckForUpdatesAsync();
    }

    private void AppearanceOption_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _suppressAppearanceEvents)
            return;

        SetAppearanceActionButtonsVisible(true);
        RaiseGlobalAppearance(GlobalAppearancePhase.Preview);
    }

    private void AppearanceBackgroundColorPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var originalColor = _appearanceBackgroundColor;
        var originalOpacity = _appearanceBackgroundOpacity;
        var picker = new ColorPickerWindow(_appearanceBackgroundColor, _appearanceBackgroundOpacity)
        {
            Owner = this
        };

        picker.PreviewChanged += (_, preview) =>
        {
            _appearanceBackgroundColor = preview.Color;
            _appearanceBackgroundOpacity = preview.Opacity;
            UpdateAppearanceColorPreview();
            SetAppearanceActionButtonsVisible(true);
            RaiseGlobalAppearance(GlobalAppearancePhase.Preview);
        };

        if (picker.ShowDialog() != true)
        {
            _appearanceBackgroundColor = originalColor;
            _appearanceBackgroundOpacity = originalOpacity;
            UpdateAppearanceColorPreview();
            RaiseGlobalAppearance(GlobalAppearancePhase.Preview);
            return;
        }

        _appearanceBackgroundColor = picker.SelectedColor;
        _appearanceBackgroundOpacity = picker.SelectedOpacity;
        UpdateAppearanceColorPreview();
        SetAppearanceActionButtonsVisible(true);
        RaiseGlobalAppearance(GlobalAppearancePhase.Preview);
    }

    private void SaveAppearanceButton_Click(object sender, RoutedEventArgs e)
    {
        _appearanceHideHeader = AppearanceHideHeaderCheckBox.IsChecked == true;
        SetAppearanceActionButtonsVisible(false);
        RaiseGlobalAppearance(GlobalAppearancePhase.Commit);
    }

    private void CancelAppearanceButton_Click(object sender, RoutedEventArgs e)
    {
        RaiseGlobalAppearance(GlobalAppearancePhase.Cancel);
        _suppressAppearanceEvents = true;
        AppearanceHideHeaderCheckBox.IsChecked = _appearanceHideHeader;
        _suppressAppearanceEvents = false;
        UpdateAppearanceColorPreview();
        SetAppearanceActionButtonsVisible(false);
    }

    private void SetAppearanceActionButtonsVisible(bool visible)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        CancelAppearanceButton.Visibility = visibility;
        SaveAppearanceButton.Visibility = visibility;
    }

    private void UpdateAppearanceColorPreview()
    {
        AppearanceBackgroundColorText.Text =
            $"#{_appearanceBackgroundColor.R:X2}{_appearanceBackgroundColor.G:X2}{_appearanceBackgroundColor.B:X2}";
        AppearanceBackgroundColorPreview.Background = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Round(_appearanceBackgroundOpacity * 255),
            _appearanceBackgroundColor.R,
            _appearanceBackgroundColor.G,
            _appearanceBackgroundColor.B));
    }

    private void RaiseGlobalAppearance(GlobalAppearancePhase phase) =>
        GlobalAppearanceChanged?.Invoke(this, new GlobalAppearanceEventArgs(
            phase,
            AppearanceHideHeaderCheckBox.IsChecked == true,
            _appearanceBackgroundColor,
            _appearanceBackgroundOpacity,
            true,
            true));

    private async Task EnsureWallpaperTabLoadedAsync()
    {
        if (_embeddedWallpaperWindow is null)
        {
            _embeddedWallpaperWindow = new WallpaperWindow(embedded: true);
            WallpaperContentHost.Content = _embeddedWallpaperWindow.DetachForEmbedding();
        }

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
            "Program pobierze aktualny wariant aplikacji, zamknie się, zainstaluje nową wersję i uruchomi ponownie.\n\nCzy rozpocząć automatyczną aktualizację?",
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
                UpdateStatusText.Text = $"Pobieranie aktualizacji… {value:P0}");
            await ApplicationUpdater.PrepareUpdateAsync(update, progress);
            UpdateStatusText.Text = "Instalowanie aktualizacji…";
            Application.Current.Shutdown();
        }
        catch (Exception exception)
        {
            UpdateStatusText.Text = "Nie udało się zainstalować aktualizacji.";
            var error = new ConfirmationWindow(
                "Aktualizacja nie powiodła się",
                exception.Message,
                "OK")
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

    private void RefreshIconsButton_Click(object sender, RoutedEventArgs e) =>
        RefreshIconsRequested?.Invoke(this, EventArgs.Empty);

    private async void ExportConfigurationButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Eksport konfiguracji My Fancy Fences",
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
        ArchiveStatusText.Text = "Tworzenie archiwum…";
        try
        {
            var result = await _exportConfiguration(
                dialog.FileName,
                ExportShortcutsCheckBox.IsChecked == true);
            ArchiveStatusText.Text =
                $"Zapisano ZIP: {result.ArchivePath}\nPanele: {result.PanelCount}, skróty: {result.ShortcutCount}.";
        }
        catch (Exception exception)
        {
            ArchiveStatusText.Text = $"Eksport nie powiódł się: {exception.Message}";
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
            Title = "Import konfiguracji My Fancy Fences",
            Filter = "Archiwum ZIP (*.zip)|*.zip",
            CheckFileExists = true,
            Multiselect = false
        };
        if (archiveDialog.ShowDialog(this) != true)
            return;

        var importShortcuts = ImportShortcutsCheckBox.IsChecked == true;

        var confirmation = new ConfirmationWindow(
            "Zaimportować konfigurację?",
            "Obecne ustawienia zostaną zastąpione zawartością archiwum. Przed zmianą powstanie kopia zapasowa, a aplikacja uruchomi się ponownie.",
            "Importuj",
            "Anuluj")
        {
            Owner = this
        };
        if (confirmation.ShowDialog() != true)
            return;

        var button = (System.Windows.Controls.Button)sender;
        button.IsEnabled = false;
        ArchiveStatusText.Text = "Importowanie konfiguracji…";
        try
        {
            await _importConfiguration(
                archiveDialog.FileName,
                importShortcuts,
                null);
            ArchiveStatusText.Text = "Import zakończony. Ponowne uruchamianie…";
        }
        catch (Exception exception)
        {
            ArchiveStatusText.Text = $"Import nie powiódł się: {exception.Message}";
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
