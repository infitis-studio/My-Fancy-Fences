using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using MahApps.Metro.IconPacks;

namespace My_Fancy_Fences;

public partial class SettingsWindow : Window
{
    private static readonly PackIconLucideKind[] AvailableIcons =
    [
        PackIconLucideKind.Settings,
        PackIconLucideKind.Folder,
        PackIconLucideKind.FolderOpen,
        PackIconLucideKind.Gamepad2,
        PackIconLucideKind.Monitor,
        PackIconLucideKind.Laptop,
        PackIconLucideKind.Music,
        PackIconLucideKind.Film,
        PackIconLucideKind.Camera,
        PackIconLucideKind.Image,
        PackIconLucideKind.Globe,
        PackIconLucideKind.Cloud,
        PackIconLucideKind.Lock,
        PackIconLucideKind.Wrench,
        PackIconLucideKind.Lightbulb,
        PackIconLucideKind.Rocket,
        PackIconLucideKind.House,
        PackIconLucideKind.BriefcaseBusiness,
        PackIconLucideKind.ShoppingCart,
        PackIconLucideKind.Trash2,
        PackIconLucideKind.Star,
        PackIconLucideKind.Heart,
        PackIconLucideKind.CircleCheck,
        PackIconLucideKind.Diamond,
        PackIconLucideKind.CalendarDays,
        PackIconLucideKind.Pin,
        PackIconLucideKind.Paperclip,
        PackIconLucideKind.FileText,
        PackIconLucideKind.Terminal,
        PackIconLucideKind.Database
    ];

    public string FenceTitle { get; private set; }
    public PackIconLucideKind FenceIcon { get; private set; }
    public double FenceWidth { get; private set; }
    public double FenceHeight { get; private set; }
    public string SourceFolder { get; private set; }
    public Color BackgroundColor { get; private set; }
    public double BackgroundOpacity { get; private set; }
    public bool HideHeader { get; private set; }
    public bool HideFolders { get; private set; }
    public double IconSize { get; private set; }
    public string FenceFontFamily { get; private set; }
    public double FenceBorderRadius { get; private set; }
    public double FenceBorderThickness { get; private set; }
    public Color FenceBorderColor { get; private set; }
    public double FenceBorderOpacity { get; private set; }
    public Color FenceFontColor { get; private set; }
    public double FenceFontOpacity { get; private set; }
    public bool FenceFontBold { get; private set; }
    public double FenceLetterSpacing { get; private set; }
    public string FenceIconFontFamily { get; private set; }
    public Color FenceIconFontColor { get; private set; }
    public double FenceIconFontOpacity { get; private set; }
    public bool FenceIconFontBold { get; private set; }
    public double FenceIconLetterSpacing { get; private set; }
    public bool DeleteRequested { get; private set; }
    public bool Accepted { get; private set; }
    private double _backgroundOpacity;
    private double _borderOpacity;
    private double _fontOpacity;
    private double _iconFontOpacity;
    private readonly bool _isNewPanel;
    public event EventHandler<SettingsPreviewEventArgs>? PreviewChanged;

    public SettingsWindow(
        string title,
        PackIconLucideKind icon,
        double width,
        double height,
        string sourceFolder,
        Color backgroundColor,
        double backgroundOpacity,
        bool hideHeader,
        bool hideFolders,
        double iconSize,
        string fontFamilyName,
        double borderRadius,
        double borderThickness,
        Color borderColor,
        double borderOpacity,
        Color fontColor,
        double fontOpacity,
        bool fontBold,
        double letterSpacing,
        string iconFontFamilyName,
        Color iconFontColor,
        double iconFontOpacity,
        bool iconFontBold,
        double iconLetterSpacing,
        bool isNewPanel = false)
    {
        InitializeComponent();
        Icon = AppIconProvider.Image;
        _isNewPanel = isNewPanel;
        Title = isNewPanel ? "Dodaj nowy panel" : "Ustawienia panelu";
        SettingsWindowTitle.Text = Title;
        DeleteButton.Visibility = isNewPanel ? Visibility.Collapsed : Visibility.Visible;

        FenceTitle = title;
        FenceIcon = icon;
        FenceWidth = width;
        FenceHeight = height;
        SourceFolder = sourceFolder;
        BackgroundColor = backgroundColor;
        BackgroundOpacity = backgroundOpacity;
        _backgroundOpacity = backgroundOpacity;
        HideHeader = hideHeader;
        HideFolders = hideFolders;
        IconSize = iconSize;
        FenceFontFamily = fontFamilyName;
        FenceBorderRadius = borderRadius;
        FenceBorderThickness = borderThickness;
        FenceBorderColor = borderColor;
        FenceBorderOpacity = borderOpacity;
        _borderOpacity = borderOpacity;
        FenceFontColor = fontColor;
        FenceFontOpacity = fontOpacity;
        FenceFontBold = fontBold;
        FenceLetterSpacing = letterSpacing;
        _fontOpacity = fontOpacity;
        FenceIconFontFamily = iconFontFamilyName;
        FenceIconFontColor = iconFontColor;
        FenceIconFontOpacity = iconFontOpacity;
        FenceIconFontBold = iconFontBold;
        FenceIconLetterSpacing = iconLetterSpacing;
        _iconFontOpacity = iconFontOpacity;

        TitleTextBox.Text = title;
        IconComboBox.ItemsSource = AvailableIcons;
        IconComboBox.SelectedItem = AvailableIcons.Contains(icon) ? icon : AvailableIcons[0];
        var availableFonts = Fonts.SystemFontFamilies
            .Select(font => font.Source)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        FontComboBox.ItemsSource = availableFonts;
        FontComboBox.SelectedItem = availableFonts.FirstOrDefault(name =>
            string.Equals(name, fontFamilyName, StringComparison.CurrentCultureIgnoreCase))
            ?? availableFonts.FirstOrDefault(name => name.StartsWith("Segoe UI", StringComparison.CurrentCultureIgnoreCase))
            ?? availableFonts.FirstOrDefault();
        IconFontComboBox.ItemsSource = availableFonts;
        IconFontComboBox.SelectedItem = availableFonts.FirstOrDefault(name =>
            string.Equals(name, iconFontFamilyName, StringComparison.CurrentCultureIgnoreCase))
            ?? availableFonts.FirstOrDefault(name => name.StartsWith("Segoe UI", StringComparison.CurrentCultureIgnoreCase))
            ?? availableFonts.FirstOrDefault();
        WidthSlider.Maximum = Math.Max(310, SystemParameters.PrimaryScreenWidth);
        HeightSlider.Maximum = Math.Max(70, SystemParameters.PrimaryScreenHeight);
        WidthSlider.Value = Math.Clamp(width, WidthSlider.Minimum, WidthSlider.Maximum);
        HeightSlider.Value = Math.Clamp(height, HeightSlider.Minimum, HeightSlider.Maximum);
        FolderTextBox.Text = sourceFolder;
        FolderPanel.Visibility = Visibility.Collapsed;
        BackgroundColorTextBox.Text = $"#{backgroundColor.R:X2}{backgroundColor.G:X2}{backgroundColor.B:X2}";
        BorderColorTextBox.Text = $"#{borderColor.R:X2}{borderColor.G:X2}{borderColor.B:X2}";
        FontColorTextBox.Text = $"#{fontColor.R:X2}{fontColor.G:X2}{fontColor.B:X2}";
        IconFontColorTextBox.Text = $"#{iconFontColor.R:X2}{iconFontColor.G:X2}{iconFontColor.B:X2}";
        BorderRadiusSlider.Value = Math.Clamp(borderRadius, BorderRadiusSlider.Minimum, BorderRadiusSlider.Maximum);
        BorderThicknessSlider.Value = Math.Clamp(borderThickness, BorderThicknessSlider.Minimum, BorderThicknessSlider.Maximum);
        BorderRadiusValueText.Text = Math.Round(BorderRadiusSlider.Value).ToString(CultureInfo.CurrentCulture);
        BorderThicknessValueText.Text = BorderThicknessSlider.Value.ToString("0.#", CultureInfo.CurrentCulture);
        BoldFontCheckBox.IsChecked = fontBold;
        LetterSpacingSlider.Value = Math.Clamp(letterSpacing, LetterSpacingSlider.Minimum, LetterSpacingSlider.Maximum);
        LetterSpacingValueText.Text = LetterSpacingSlider.Value.ToString("0.##", CultureInfo.CurrentCulture);
        IconBoldFontCheckBox.IsChecked = iconFontBold;
        IconLetterSpacingSlider.Value = Math.Clamp(iconLetterSpacing, IconLetterSpacingSlider.Minimum, IconLetterSpacingSlider.Maximum);
        IconLetterSpacingValueText.Text = IconLetterSpacingSlider.Value.ToString("0.##", CultureInfo.CurrentCulture);
        HideHeaderCheckBox.IsChecked = hideHeader;
        HideFoldersCheckBox.IsChecked = hideFolders;
        IconSizeSlider.Value = Math.Clamp(iconSize, IconSizeSlider.Minimum, IconSizeSlider.Maximum);
        IconSizeValueText.Text = Math.Round(IconSizeSlider.Value).ToString(CultureInfo.CurrentCulture);
        UpdateHeaderOptionsState();
        UpdateBackgroundPreview();
        UpdateBorderPreview();
        UpdateFontColorPreview();
        UpdateIconFontColorPreview();

        TitleTextBox.TextChanged += (_, _) => RaisePreviewChanged();
        IconComboBox.SelectionChanged += (_, _) => RaisePreviewChanged();
        FontComboBox.SelectionChanged += (_, _) => RaisePreviewChanged();
        IconFontComboBox.SelectionChanged += (_, _) => RaisePreviewChanged();

        Loaded += (_, _) =>
        {
            Activate();
            Focus();
        };
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var title = TitleTextBox.Text.Trim();
        var icon = IconComboBox.SelectedItem is PackIconLucideKind selectedIcon
            ? selectedIcon
            : AvailableIcons[0];
        var sourceFolder = FolderTextBox.Text;

        if (!TryReadBackgroundColor(out var backgroundColor))
        {
            ValidationText.Text = LocalizationService.T("Podaj kolor w formacie HEX, np. #0B0E12.");
            return;
        }

        if (!TryReadBorderColor(out var borderColor))
        {
            ValidationText.Text = LocalizationService.T("Podaj kolor obramowania w formacie HEX, np. #FFFFFF.");
            return;
        }

        if (!TryReadFontColor(out var fontColor))
        {
            ValidationText.Text = LocalizationService.T("Podaj kolor tekstu w formacie HEX, np. #FFFFFF.");
            return;
        }

        if (!TryReadIconFontColor(out var iconFontColor))
        {
            ValidationText.Text = LocalizationService.T("Podaj kolor podpisów ikon w formacie HEX, np. #FFFFFF.");
            return;
        }

        FenceTitle = title;
        FenceIcon = icon;
        FenceWidth = WidthSlider.Value;
        FenceHeight = HeightSlider.Value;
        SourceFolder = sourceFolder;
        BackgroundColor = backgroundColor;
        BackgroundOpacity = _backgroundOpacity;
        HideHeader = HideHeaderCheckBox.IsChecked == true;
        HideFolders = HideFoldersCheckBox.IsChecked == true;
        IconSize = IconSizeSlider.Value;
        FenceFontFamily = FontComboBox.SelectedItem as string ?? "Segoe UI";
        FenceBorderRadius = BorderRadiusSlider.Value;
        FenceBorderThickness = BorderThicknessSlider.Value;
        FenceBorderColor = borderColor;
        FenceBorderOpacity = _borderOpacity;
        FenceFontColor = fontColor;
        FenceFontOpacity = _fontOpacity;
        FenceFontBold = BoldFontCheckBox.IsChecked == true;
        FenceLetterSpacing = LetterSpacingSlider.Value;
        FenceIconFontFamily = IconFontComboBox.SelectedItem as string ?? "Segoe UI";
        FenceIconFontColor = iconFontColor;
        FenceIconFontOpacity = _iconFontOpacity;
        FenceIconFontBold = IconBoldFontCheckBox.IsChecked == true;
        FenceIconLetterSpacing = IconLetterSpacingSlider.Value;
        Complete(true);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Complete(false);

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        var confirmation = new ConfirmationWindow(
            LocalizationService.T("Usunąć panel?"),
            LocalizationService.T("Panel zostanie trwale usunięty wraz z jego zapisanymi ustawieniami. Tej operacji nie można cofnąć."),
            LocalizationService.T("Usuń panel"))
        {
            Owner = this
        };
        if (confirmation.ShowDialog() != true)
            return;

        DeleteRequested = true;
        Complete(true);
    }

    private void ResetBackgroundDefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmSectionReset("tła i obramowania"))
            return;

        _backgroundOpacity = 0.58;
        BackgroundColorTextBox.Text = "#0B0E12";
        _borderOpacity = 0;
        BorderColorTextBox.Text = "#FFFFFF";
        BorderRadiusSlider.Value = 11;
        BorderThicknessSlider.Value = 0;
        UpdateBackgroundPreview();
        UpdateBorderPreview();
        RaisePreviewChanged();
    }

    private void ResetHeaderFontDefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmSectionReset("czcionki nagłówka"))
            return;

        FontComboBox.SelectedItem = GetDefaultFontName(FontComboBox);
        _fontOpacity = 1;
        FontColorTextBox.Text = "#F7F9FC";
        BoldFontCheckBox.IsChecked = false;
        LetterSpacingSlider.Value = 0;
        UpdateFontColorPreview();
        RaisePreviewChanged();
    }

    private void ResetIconFontDefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmSectionReset("czcionki ikon"))
            return;

        IconFontComboBox.SelectedItem = GetDefaultFontName(IconFontComboBox);
        _iconFontOpacity = 1;
        IconFontColorTextBox.Text = "#F7F9FC";
        IconBoldFontCheckBox.IsChecked = false;
        IconLetterSpacingSlider.Value = 0;
        IconSizeSlider.Value = 42;
        UpdateIconFontColorPreview();
        RaisePreviewChanged();
    }

    private bool ConfirmSectionReset(string sectionName)
    {
        var confirmation = new ConfirmationWindow(
            LocalizationService.T("Przywrócić ustawienia?"),
            $"{LocalizationService.T("Ustawienia")} {LocalizationService.T(sectionName)} {LocalizationService.T("zostaną zastąpione wartościami domyślnymi.")}",
            LocalizationService.T("Przywróć"))
        {
            Owner = this
        };
        return confirmation.ShowDialog() == true;
    }

    private static string? GetDefaultFontName(ComboBox comboBox) =>
        comboBox.Items.OfType<string>().FirstOrDefault(name => string.Equals(
            name,
            "Segoe UI Variable Text",
            StringComparison.CurrentCultureIgnoreCase))
        ?? comboBox.Items.OfType<string>()
            .FirstOrDefault(name => name.StartsWith("Segoe UI", StringComparison.CurrentCultureIgnoreCase));

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Complete(false);

    private void Complete(bool accepted)
    {
        Accepted = accepted;
        if (_isNewPanel)
            Close();
        else
            DialogResult = accepted;
    }

    private void BrowseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Wybierz folder z elementami",
            InitialDirectory = Directory.Exists(FolderTextBox.Text)
                ? FolderTextBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };

        if (dialog.ShowDialog(this) == true)
        {
            FolderTextBox.Text = dialog.FolderName;
            RaisePreviewChanged();
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void BackgroundAppearance_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized)
            return;

        UpdateBackgroundPreview();
        RaisePreviewChanged();
    }

    private void BackgroundColorPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var currentColor = TryReadBackgroundColor(out var color)
            ? color
            : Color.FromRgb(0x0B, 0x0E, 0x12);
        var originalColor = currentColor;
        var originalOpacity = _backgroundOpacity;

        var dialog = new ColorPickerWindow(currentColor, _backgroundOpacity);
        dialog.PreviewChanged += (_, preview) =>
        {
            _backgroundOpacity = preview.Opacity;
            BackgroundColorTextBox.Text =
                $"#{preview.Color.R:X2}{preview.Color.G:X2}{preview.Color.B:X2}";
            UpdateBackgroundPreview();
            RaisePreviewChanged();
        };

        if (dialog.ShowDialog() != true)
        {
            _backgroundOpacity = originalOpacity;
            BackgroundColorTextBox.Text =
                $"#{originalColor.R:X2}{originalColor.G:X2}{originalColor.B:X2}";
            UpdateBackgroundPreview();
            RaisePreviewChanged();
            return;
        }

        _backgroundOpacity = dialog.SelectedOpacity;
        BackgroundColorTextBox.Text =
            $"#{dialog.SelectedColor.R:X2}{dialog.SelectedColor.G:X2}{dialog.SelectedColor.B:X2}";
        UpdateBackgroundPreview();
        RaisePreviewChanged();
        e.Handled = true;
    }

    private void BorderAppearance_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized)
            return;

        UpdateBorderPreview();
        RaisePreviewChanged();
    }

    private void BorderSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized)
            return;

        BorderRadiusValueText.Text = Math.Round(BorderRadiusSlider.Value).ToString(CultureInfo.CurrentCulture);
        BorderThicknessValueText.Text = BorderThicknessSlider.Value.ToString("0.#", CultureInfo.CurrentCulture);
        RaisePreviewChanged();
    }

    private void BorderColorPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var currentColor = TryReadBorderColor(out var color) ? color : Colors.White;
        var originalColor = currentColor;
        var originalOpacity = _borderOpacity;
        var dialog = new ColorPickerWindow(currentColor, _borderOpacity);

        dialog.PreviewChanged += (_, preview) =>
        {
            _borderOpacity = preview.Opacity;
            BorderColorTextBox.Text = $"#{preview.Color.R:X2}{preview.Color.G:X2}{preview.Color.B:X2}";
            UpdateBorderPreview();
            RaisePreviewChanged();
        };

        if (dialog.ShowDialog() != true)
        {
            _borderOpacity = originalOpacity;
            BorderColorTextBox.Text = $"#{originalColor.R:X2}{originalColor.G:X2}{originalColor.B:X2}";
            UpdateBorderPreview();
            RaisePreviewChanged();
            return;
        }

        _borderOpacity = dialog.SelectedOpacity;
        BorderColorTextBox.Text = $"#{dialog.SelectedColor.R:X2}{dialog.SelectedColor.G:X2}{dialog.SelectedColor.B:X2}";
        UpdateBorderPreview();
        RaisePreviewChanged();
        e.Handled = true;
    }

    private void FontAppearance_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized)
            return;

        UpdateFontColorPreview();
        RaisePreviewChanged();
    }

    private void LetterSpacingSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized)
            return;

        LetterSpacingValueText.Text = LetterSpacingSlider.Value.ToString("0.##", CultureInfo.CurrentCulture);
        RaisePreviewChanged();
    }

    private void FontColorPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var currentColor = TryReadFontColor(out var color) ? color : Colors.White;
        var originalColor = currentColor;
        var originalOpacity = _fontOpacity;
        var dialog = new ColorPickerWindow(currentColor, _fontOpacity);

        dialog.PreviewChanged += (_, preview) =>
        {
            _fontOpacity = preview.Opacity;
            FontColorTextBox.Text = $"#{preview.Color.R:X2}{preview.Color.G:X2}{preview.Color.B:X2}";
            UpdateFontColorPreview();
            RaisePreviewChanged();
        };

        if (dialog.ShowDialog() != true)
        {
            _fontOpacity = originalOpacity;
            FontColorTextBox.Text = $"#{originalColor.R:X2}{originalColor.G:X2}{originalColor.B:X2}";
            UpdateFontColorPreview();
            RaisePreviewChanged();
            return;
        }

        _fontOpacity = dialog.SelectedOpacity;
        FontColorTextBox.Text = $"#{dialog.SelectedColor.R:X2}{dialog.SelectedColor.G:X2}{dialog.SelectedColor.B:X2}";
        UpdateFontColorPreview();
        RaisePreviewChanged();
        e.Handled = true;
    }

    private void IconFontAppearance_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized)
            return;

        UpdateIconFontColorPreview();
        RaisePreviewChanged();
    }

    private void IconLetterSpacingSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized)
            return;

        IconLetterSpacingValueText.Text = IconLetterSpacingSlider.Value.ToString("0.##", CultureInfo.CurrentCulture);
        RaisePreviewChanged();
    }

    private void IconFontColorPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var currentColor = TryReadIconFontColor(out var color) ? color : Colors.White;
        var originalColor = currentColor;
        var originalOpacity = _iconFontOpacity;
        var dialog = new ColorPickerWindow(currentColor, _iconFontOpacity);

        dialog.PreviewChanged += (_, preview) =>
        {
            _iconFontOpacity = preview.Opacity;
            IconFontColorTextBox.Text = $"#{preview.Color.R:X2}{preview.Color.G:X2}{preview.Color.B:X2}";
            UpdateIconFontColorPreview();
            RaisePreviewChanged();
        };

        if (dialog.ShowDialog() != true)
        {
            _iconFontOpacity = originalOpacity;
            IconFontColorTextBox.Text = $"#{originalColor.R:X2}{originalColor.G:X2}{originalColor.B:X2}";
            UpdateIconFontColorPreview();
            RaisePreviewChanged();
            return;
        }

        _iconFontOpacity = dialog.SelectedOpacity;
        IconFontColorTextBox.Text = $"#{dialog.SelectedColor.R:X2}{dialog.SelectedColor.G:X2}{dialog.SelectedColor.B:X2}";
        UpdateIconFontColorPreview();
        RaisePreviewChanged();
        e.Handled = true;
    }

    private void DimensionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized)
            return;

        WidthValueText.Text = Math.Round(WidthSlider.Value).ToString(CultureInfo.CurrentCulture);
        HeightValueText.Text = Math.Round(HeightSlider.Value).ToString(CultureInfo.CurrentCulture);
        RaisePreviewChanged();
    }

    private void PixelValueTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox textBox)
            CommitPixelValue(textBox);
    }

    private void PixelValueTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox textBox)
            return;

        CommitPixelValue(textBox);
        textBox.SelectAll();
        e.Handled = true;
    }

    private void CommitPixelValue(TextBox textBox)
    {
        if (textBox.Tag is not string sliderName || FindName(sliderName) is not Slider slider)
            return;

        var rawValue = textBox.Text.Trim();
        var parsed = double.TryParse(
            rawValue,
            NumberStyles.Float,
            CultureInfo.CurrentCulture,
            out var value);

        if (!parsed)
        {
            parsed = double.TryParse(
                rawValue.Replace(',', '.'),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
        }

        if (parsed)
            slider.Value = Math.Clamp(value, slider.Minimum, slider.Maximum);

        textBox.Text = slider.TickFrequency >= 1
            ? Math.Round(slider.Value).ToString(CultureInfo.CurrentCulture)
            : slider.Value.ToString("0.##", CultureInfo.CurrentCulture);
    }

    public void UpdatePanelDimensions(double width, double height)
    {
        var clampedWidth = Math.Clamp(width, WidthSlider.Minimum, WidthSlider.Maximum);
        var clampedHeight = Math.Clamp(height, HeightSlider.Minimum, HeightSlider.Maximum);

        if (Math.Abs(WidthSlider.Value - clampedWidth) > 0.1)
            WidthSlider.Value = clampedWidth;
        if (Math.Abs(HeightSlider.Value - clampedHeight) > 0.1)
            HeightSlider.Value = clampedHeight;
    }

    private void HideHeaderCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateHeaderOptionsState();
        RaisePreviewChanged();
    }

    private void HideFoldersCheckBox_Changed(object sender, RoutedEventArgs e) => RaisePreviewChanged();

    private void SettingsTab_Checked(object sender, RoutedEventArgs e)
    {
        if (HideHeaderCheckBox is null ||
            TitleOptionsPanel is null ||
            IconOptionsPanel is null ||
            DimensionsPanel is null ||
            FolderPanel is null ||
            BackgroundTabContent is null ||
            HeaderFontTabContent is null ||
            IconFontTabContent is null)
        {
            return;
        }

        var selectedTab = (sender as FrameworkElement)?.Tag as string ?? "General";
        var showGeneral = selectedTab == "General";
        TitleOptionsPanel.Visibility = showGeneral ? Visibility.Visible : Visibility.Collapsed;
        IconOptionsPanel.Visibility = showGeneral ? Visibility.Visible : Visibility.Collapsed;
        DimensionsPanel.Visibility = showGeneral ? Visibility.Visible : Visibility.Collapsed;
        FolderPanel.Visibility = Visibility.Collapsed;
        BackgroundTabContent.Visibility = selectedTab == "Background" ? Visibility.Visible : Visibility.Collapsed;
        HeaderFontTabContent.Visibility = selectedTab == "Header" ? Visibility.Visible : Visibility.Collapsed;
        IconFontTabContent.Visibility = selectedTab == "Icons" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void IconSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized)
            return;

        IconSizeValueText.Text = Math.Round(IconSizeSlider.Value).ToString(CultureInfo.CurrentCulture);
        RaisePreviewChanged();
    }

    private void UpdateHeaderOptionsState()
    {
        if (HeaderAppearancePanel is null)
            return;

        var enabled = HideHeaderCheckBox.IsChecked != true;
        HeaderAppearancePanel.IsEnabled = enabled;
        HeaderAppearancePanel.Opacity = enabled ? 1 : 0.4;
    }

    private void UpdateBackgroundPreview()
    {
        if (!TryReadBackgroundColor(out var color))
        {
            BackgroundColorPreview.Background = Brushes.Transparent;
            return;
        }

        BackgroundColorPreview.Background = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Round(_backgroundOpacity * 255),
            color.R,
            color.G,
            color.B));
    }

    private void UpdateBorderPreview()
    {
        if (!TryReadBorderColor(out var color))
        {
            BorderColorPreview.Background = Brushes.Transparent;
            return;
        }

        BorderColorPreview.Background = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Round(_borderOpacity * 255),
            color.R,
            color.G,
            color.B));
    }

    private void UpdateFontColorPreview()
    {
        if (!TryReadFontColor(out var color))
        {
            FontColorPreview.Background = Brushes.Transparent;
            return;
        }

        FontColorPreview.Background = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Round(_fontOpacity * 255),
            color.R,
            color.G,
            color.B));
    }

    private void UpdateIconFontColorPreview()
    {
        if (!TryReadIconFontColor(out var color))
        {
            IconFontColorPreview.Background = Brushes.Transparent;
            return;
        }

        IconFontColorPreview.Background = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Round(_iconFontOpacity * 255),
            color.R,
            color.G,
            color.B));
    }

    private bool TryReadBackgroundColor(out Color color)
    {
        try
        {
            var value = BackgroundColorTextBox.Text.Trim();
            if (!value.StartsWith('#'))
                value = $"#{value}";

            if (value.Length != 7)
            {
                color = default;
                return false;
            }

            color = (Color)ColorConverter.ConvertFromString(value);
            return true;
        }
        catch
        {
            color = default;
            return false;
        }
    }

    private bool TryReadBorderColor(out Color color)
    {
        try
        {
            var value = BorderColorTextBox.Text.Trim();
            if (!value.StartsWith('#'))
                value = $"#{value}";

            if (value.Length != 7)
            {
                color = default;
                return false;
            }

            color = (Color)ColorConverter.ConvertFromString(value);
            return true;
        }
        catch
        {
            color = default;
            return false;
        }
    }

    private bool TryReadFontColor(out Color color)
    {
        try
        {
            var value = FontColorTextBox.Text.Trim();
            if (!value.StartsWith('#'))
                value = $"#{value}";

            if (value.Length != 7)
            {
                color = default;
                return false;
            }

            color = (Color)ColorConverter.ConvertFromString(value);
            return true;
        }
        catch
        {
            color = default;
            return false;
        }
    }

    private bool TryReadIconFontColor(out Color color)
    {
        try
        {
            var value = IconFontColorTextBox.Text.Trim();
            if (!value.StartsWith('#'))
                value = $"#{value}";

            if (value.Length != 7)
            {
                color = default;
                return false;
            }

            color = (Color)ColorConverter.ConvertFromString(value);
            return true;
        }
        catch
        {
            color = default;
            return false;
        }
    }

    private void RaisePreviewChanged()
    {
        if (!IsLoaded ||
            !TryReadBackgroundColor(out var color) ||
            !TryReadBorderColor(out var borderColor) ||
            !TryReadFontColor(out var fontColor) ||
            !TryReadIconFontColor(out var iconFontColor))
            return;

        PreviewChanged?.Invoke(this, new SettingsPreviewEventArgs(
            TitleTextBox.Text.Trim(),
            IconComboBox.SelectedItem is PackIconLucideKind selectedIcon
                ? selectedIcon
                : AvailableIcons[0],
            WidthSlider.Value,
            HeightSlider.Value,
            FolderTextBox.Text,
            HideFoldersCheckBox.IsChecked == true,
            color,
            _backgroundOpacity,
            HideHeaderCheckBox.IsChecked == true,
            IconSizeSlider.Value,
            FontComboBox.SelectedItem as string ?? "Segoe UI",
            BorderRadiusSlider.Value,
            BorderThicknessSlider.Value,
            borderColor,
            _borderOpacity,
            fontColor,
            _fontOpacity,
            BoldFontCheckBox.IsChecked == true,
            LetterSpacingSlider.Value,
            IconFontComboBox.SelectedItem as string ?? "Segoe UI",
            iconFontColor,
            _iconFontOpacity,
            IconBoldFontCheckBox.IsChecked == true,
            IconLetterSpacingSlider.Value));
    }
}

public sealed record SettingsPreviewEventArgs(
    string Title,
    PackIconLucideKind Icon,
    double Width,
    double Height,
    string SourceFolder,
    bool HideFolders,
    Color BackgroundColor,
    double BackgroundOpacity,
    bool HideHeader,
    double IconSize,
    string FontFamilyName,
    double BorderRadius,
    double BorderThickness,
    Color BorderColor,
    double BorderOpacity,
    Color FontColor,
    double FontOpacity,
    bool FontBold,
    double LetterSpacing,
    string IconFontFamilyName,
    Color IconFontColor,
    double IconFontOpacity,
    bool IconFontBold,
    double IconLetterSpacing);
