using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using MahApps.Metro.IconPacks;

namespace My_Fancy_Fences;

public partial class PanelsWindow : Window
{
    private bool _hasCheckedForUpdates;
    private string? _latestReleaseUrl;

    public event EventHandler<PanelVisibilityChangedEventArgs>? PanelVisibilityChanged;
    public event EventHandler<PanelEditRequestedEventArgs>? EditPanelRequested;
    public event EventHandler? RefreshIconsRequested;
    public event EventHandler<ActivationModeChangedEventArgs>? ActivationModeChanged;

    public PanelsWindow(IReadOnlyList<PanelOverviewItem> panels, bool useDoubleClickToOpen)
    {
        InitializeComponent();
        Icon = AppIconProvider.Image;
        DoubleClickActivationCheckBox.IsChecked = useDoubleClickToOpen;
        LanguageComboBox.ItemsSource = LocalizationService.Languages;
        LanguageComboBox.SelectedValue = LocalizationService.CurrentLanguage;
        CurrentVersionText.Text = $"v{UpdateService.CurrentVersion.ToString(3)}";
        UpdatePanels(panels);

        _ = ApplyStartupUpdateStatusAsync();
    }

    private async Task ApplyStartupUpdateStatusAsync()
    {
        var result = await UpdateService.CheckAsync();
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
            ImportExportTabContent is null || UpdatesTabContent is null)
            return;

        var selectedTab = (sender as FrameworkElement)?.Tag as string ?? "General";
        GeneralTabContent.Visibility = selectedTab == "General"
            ? Visibility.Visible
            : Visibility.Collapsed;
        PanelsTabContent.Visibility = selectedTab == "Panels"
            ? Visibility.Visible
            : Visibility.Collapsed;
        ImportExportTabContent.Visibility = selectedTab == "ImportExport"
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdatesTabContent.Visibility = selectedTab == "Updates"
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (selectedTab == "Updates" && !_hasCheckedForUpdates)
            await CheckForUpdatesAsync();
    }

    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e) =>
        await CheckForUpdatesAsync(force: true);

    private void OpenReleaseButton_Click(object sender, RoutedEventArgs e)
    {
        var confirmation = new ConfirmationWindow(
            LocalizationService.T("Nowa wersja jest gotowa"),
            LocalizationService.T("Program może pobrać i zainstalować najnowszą wersję automatycznie.\n\nCzy chcesz rozpocząć aktualizację?"),
            LocalizationService.T("Aktualizuj"),
            LocalizationService.T("Nie teraz"),
            positiveConfirm: true)
        {
            Owner = this
        };

        _ = confirmation.ShowDialog();
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
            _latestReleaseUrl = result.ReleaseUrl;
            LatestVersionText.Text = result.LatestTag;
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

    private void RefreshIconsButton_Click(object sender, RoutedEventArgs e) =>
        RefreshIconsRequested?.Invoke(this, EventArgs.Empty);

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
