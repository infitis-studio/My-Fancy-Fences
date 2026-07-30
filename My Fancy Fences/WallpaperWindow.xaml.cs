using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace My_Fancy_Fences;

public partial class WallpaperWindow : Window
{
    private const int MaxCachedWallpapers = 144;
    private const double WallpaperCardPitch = 170;
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private readonly List<string> _tags = [];
    private readonly ObservableCollection<WallpaperCard> _wallpapers = [];
    private string _sorting = "date_added";
    private bool _isLoaded;
    private bool _isLoading;
    private bool _hasMorePages = true;
    private bool _isCustomMaximized;
    private readonly bool _isEmbedded;
    private bool _isResizing;
    private int _currentPage;
    private Rect _restoreBounds;
    private Point _resizeStartScreenPosition;
    private double _resizeStartWidth;
    private double _resizeStartHeight;
    private CancellationTokenSource? _loadCancellation;

    private static readonly Brush[] TagColors =
    [
        new SolidColorBrush(Color.FromRgb(0x3E, 0x8E, 0x82)),
        new SolidColorBrush(Color.FromRgb(0x5A, 0x70, 0xB8)),
        new SolidColorBrush(Color.FromRgb(0xA1, 0x5E, 0x7A)),
        new SolidColorBrush(Color.FromRgb(0xA0, 0x72, 0x42)),
        new SolidColorBrush(Color.FromRgb(0x68, 0x72, 0x82))
    ];

    public WallpaperWindow() : this(false)
    {
    }

    public WallpaperWindow(bool embedded)
    {
        _isEmbedded = embedded;
        InitializeComponent();
        Icon = AppIconProvider.Image;

        var workArea = SystemParameters.WorkArea;
        Width = Math.Clamp(workArea.Width * 0.62, MinWidth, 1280);
        Height = Math.Clamp(workArea.Height * 0.62, MinHeight, 820);

        ResolutionComboBox.SelectedIndex = 0;
        RatioComboBox.SelectedIndex = 0;
        ColorComboBox.SelectedIndex = 0;
        WallpapersItemsControl.ItemsSource = _wallpapers;
        SizeChanged += (_, _) => ApplyRoundedWindowClip();

        if (_isEmbedded)
        {
            ConfigureEmbeddedMode();
        }

        Loaded += async (_, _) =>
        {
            if (_isEmbedded)
                return;

            _isLoaded = true;
            ApplyRoundedWindowClip();
            await ReloadWallpapersAsync();
        };
        Closed += (_, _) => ReleaseWallpaperResources();
    }

    public UIElement DetachForEmbedding()
    {
        ConfigureEmbeddedMode();
        var content = (UIElement)Content;
        Content = null;
        return content;
    }

    public async Task InitializeEmbeddedAsync()
    {
        if (_isLoaded)
            return;

        _isLoaded = true;
        await ReloadWallpapersAsync();
    }

    public void DisposeEmbedded() => ReleaseWallpaperResources();

    private void ConfigureEmbeddedMode()
    {
        TitleBarRow.Height = new GridLength(0);
        TitleBarBorder.Visibility = Visibility.Collapsed;
        ResizeGrip.Visibility = Visibility.Collapsed;
        OuterWindowBorder.CornerRadius = new CornerRadius(8);
        SidebarBorder.CornerRadius = new CornerRadius(0, 0, 0, 8);
        OuterWindowBorder.BorderThickness = new Thickness(0);
        OuterWindowBorder.ClipToBounds = true;
        WallpapersScrollViewer.Margin = new Thickness(0);
        ApplyWallpaperComboBoxStyles();
    }

    private void ApplyWallpaperComboBoxStyles()
    {
        if (Resources[typeof(ComboBox)] is not Style comboBoxStyle)
            return;

        ResolutionComboBox.Style = comboBoxStyle;
        RatioComboBox.Style = comboBoxStyle;
        ColorComboBox.Style = comboBoxStyle;
    }

    private void ApplyRoundedWindowClip()
    {
        if (_isEmbedded)
            return;

        if (Content is not FrameworkElement root || ActualWidth <= 0 || ActualHeight <= 0)
            return;

        var radius = _isCustomMaximized ? 0 : 13;
        root.Clip = new RectangleGeometry(
            new Rect(0, 0, ActualWidth, ActualHeight),
            radius,
            radius);
    }

    private void UpdateWindowChrome()
    {
        if (_isEmbedded)
            return;

        OuterWindowBorder.CornerRadius = _isCustomMaximized ? new CornerRadius(0) : new CornerRadius(13);
        TitleBarBorder.CornerRadius = _isCustomMaximized ? new CornerRadius(0) : new CornerRadius(12, 12, 0, 0);
        SidebarBorder.CornerRadius = _isCustomMaximized ? new CornerRadius(0) : new CornerRadius(0, 0, 0, 12);
        ResizeGrip.Visibility = _isCustomMaximized ? Visibility.Collapsed : Visibility.Visible;
        ApplyRoundedWindowClip();
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("MyFancyFences", "1.0"));
        return client;
    }

    private async Task ReloadWallpapersAsync()
    {
        _loadCancellation?.Cancel();
        _loadCancellation = new CancellationTokenSource();
        _currentPage = 0;
        _hasMorePages = true;
        _isLoading = false;
        _wallpapers.Clear();
        WallpapersScrollViewer.ScrollToTop();
        await LoadNextPageAsync(showOverlay: true);
    }

    private async Task LoadNextPageAsync(bool showOverlay = false)
    {
        if (_isLoading || !_hasMorePages)
            return;

        _isLoading = true;
        _loadCancellation ??= new CancellationTokenSource();
        var cancellationToken = _loadCancellation.Token;

        if (showOverlay || _wallpapers.Count == 0)
        {
            StartLoadingAnimation();
            StatusPanel.Visibility = Visibility.Collapsed;
            RetryButton.Visibility = Visibility.Collapsed;
        }

        try
        {
            var requestedPage = _currentPage + 1;
            using var response = await HttpClient.GetAsync(
                BuildApiUrl(requestedPage),
                cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var result = await JsonSerializer.DeserializeAsync<WallhavenResponse>(
                stream,
                cancellationToken: cancellationToken);

            var wallpapers = result?.Data?
                .Where(item =>
                    !string.IsNullOrWhiteSpace(item.Url) &&
                    !string.IsNullOrWhiteSpace(item.Thumbs?.Large))
                .Select(item => new WallpaperCard(
                    item.Id ?? string.Empty,
                    item.Thumbs!.Large!,
                    item.Url!,
                    item.Path,
                    item.Resolution ?? string.Empty,
                    item.Category,
                    item.Purity,
                    item.FileType,
                    item.FileSize))
                .ToList() ?? [];

            foreach (var wallpaper in wallpapers)
                _wallpapers.Add(wallpaper);

            TrimWallpaperCache();

            _currentPage = requestedPage;
            _hasMorePages =
                wallpapers.Count > 0 &&
                (result?.Meta is null || _currentPage < result.Meta.LastPage);

            StatusPanel.Visibility = _wallpapers.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            StatusText.Text = _wallpapers.Count == 0
                ? "Nie znaleziono tapet dla wybranych filtrów."
                : string.Empty;
            RetryButton.Visibility = _wallpapers.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        catch (OperationCanceledException)
        {
            // A newer filter request replaced this one.
        }
        catch (Exception)
        {
            StatusPanel.Visibility = Visibility.Visible;
            StatusText.Text = "Nie udało się pobrać tapet z Wallhaven.";
            RetryButton.Visibility = Visibility.Visible;
        }
        finally
        {
            _isLoading = false;
            StopLoadingAnimation();
        }

        await PrefetchIfViewportIsNotFilledAsync();
    }

    private void TrimWallpaperCache()
    {
        var removeCount = _wallpapers.Count - MaxCachedWallpapers;
        if (removeCount <= 0)
            return;

        var columns = Math.Max(
            1,
            (int)(Math.Max(1, WallpapersScrollViewer.ViewportWidth) / 250));
        var removedRows = (int)Math.Ceiling(removeCount / (double)columns);
        var previousOffset = WallpapersScrollViewer.VerticalOffset;

        for (var index = 0; index < removeCount; index++)
        {
            _wallpapers[0].ReleaseThumbnail();
            _wallpapers.RemoveAt(0);
        }

        WallpapersScrollViewer.ScrollToVerticalOffset(
            Math.Max(0, previousOffset - removedRows * WallpaperCardPitch));
    }

    private void ReleaseWallpaperResources()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
        StopLoadingAnimation();
        WallpapersItemsControl.ItemsSource = null;
        TagsItemsControl.ItemsSource = null;
        foreach (var wallpaper in _wallpapers)
            wallpaper.ReleaseThumbnail();
        _wallpapers.Clear();
        _tags.Clear();
    }

    private async Task PrefetchIfViewportIsNotFilledAsync()
    {
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);

        if (_hasMorePages &&
            !_isLoading &&
            WallpapersScrollViewer.ScrollableHeight < WallpapersScrollViewer.ViewportHeight * 0.35)
        {
            await LoadNextPageAsync();
        }
    }

    private void StartLoadingAnimation()
    {
        LoadingOverlay.Visibility = Visibility.Visible;
        var animation = new DoubleAnimation
        {
            From = 0,
            To = 360,
            Duration = TimeSpan.FromSeconds(.85),
            RepeatBehavior = RepeatBehavior.Forever
        };
        LoadingRotateTransform.BeginAnimation(RotateTransform.AngleProperty, animation);
    }

    private void StopLoadingAnimation()
    {
        LoadingRotateTransform.BeginAnimation(RotateTransform.AngleProperty, null);
        LoadingOverlay.Visibility = Visibility.Collapsed;
    }

    private string BuildApiUrl(int page)
    {
        var categories =
            $"{(GeneralCheckBox.IsChecked == true ? '1' : '0')}" +
            $"{(AnimeCheckBox.IsChecked == true ? '1' : '0')}" +
            $"{(PeopleCheckBox.IsChecked == true ? '1' : '0')}";
        if (categories == "000")
            categories = "111";

        var purity =
            $"{(SfwCheckBox.IsChecked == true ? '1' : '0')}" +
            $"{(SketchyCheckBox.IsChecked == true ? '1' : '0')}0";
        if (purity == "000")
            purity = "100";

        var parameters = new List<string>
        {
            $"categories={categories}",
            $"purity={purity}",
            $"sorting={Uri.EscapeDataString(_sorting)}",
            "order=desc",
            $"page={page}"
        };

        if (_tags.Count > 0)
            parameters.Add($"q={Uri.EscapeDataString(string.Join(' ', _tags))}");

        AddComboParameter(parameters, "resolutions", ResolutionComboBox);
        AddComboParameter(parameters, "ratios", RatioComboBox);
        AddComboParameter(parameters, "colors", ColorComboBox);

        if (_sorting == "toplist")
            parameters.Add("topRange=1M");

        return $"https://wallhaven.cc/api/v1/search?{string.Join('&', parameters)}";
    }

    private static void AddComboParameter(
        ICollection<string> parameters,
        string name,
        ComboBox comboBox)
    {
        if (comboBox.SelectedItem is ComboBoxItem { Tag: string value } &&
            !string.IsNullOrWhiteSpace(value))
        {
            parameters.Add($"{name}={Uri.EscapeDataString(value)}");
        }
    }

    private async void WallpapersScrollViewer_ScrollChanged(
        object sender,
        ScrollChangedEventArgs e)
    {
        if (!_isLoaded || _isLoading || !_hasMorePages)
            return;

        var remainingDistance =
            e.ExtentHeight - e.VerticalOffset - e.ViewportHeight;
        var prefetchDistance = Math.Max(420, e.ViewportHeight * 0.9);

        if (remainingDistance <= prefetchDistance)
            await LoadNextPageAsync();
    }

    private async void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoaded)
            await ReloadWallpapersAsync();
    }

    private async void FilterComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_isLoaded)
            await ReloadWallpapersAsync();
    }

    private async void SortButton_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string sorting })
            _sorting = sorting;

        if (_isLoaded)
            await ReloadWallpapersAsync();
    }

    private void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddTag();
            e.Handled = true;
        }
    }

    private void AddTagButton_Click(object sender, RoutedEventArgs e) => AddTag();

    private async void AddTag()
    {
        var candidates = SearchTextBox.Text
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var candidate in candidates)
        {
            if (!_tags.Contains(candidate, StringComparer.CurrentCultureIgnoreCase))
                _tags.Add(candidate);
        }

        SearchTextBox.Clear();
        UpdateSearchPlaceholder();
        RefreshTagChips();
        await ReloadWallpapersAsync();
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e) =>
        UpdateSearchPlaceholder();

    private void UpdateSearchPlaceholder()
    {
        if (SearchPlaceholderText is null || SearchTextBox is null)
            return;

        SearchPlaceholderText.Visibility = string.IsNullOrWhiteSpace(SearchTextBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void RemoveTagButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag })
            _tags.RemoveAll(item => string.Equals(item, tag, StringComparison.CurrentCultureIgnoreCase));

        RefreshTagChips();
        await ReloadWallpapersAsync();
    }

    private void RefreshTagChips()
    {
        TagsItemsControl.ItemsSource = _tags
            .Select((tag, index) => new TagChip(tag, TagColors[index % TagColors.Length]))
            .ToList();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) =>
        await ReloadWallpapersAsync();

    private void WallpaperButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: WallpaperCard wallpaper })
            return;

        var owner = Window.GetWindow(sender as DependencyObject);
        var detailsWindow = new WallpaperDetailsWindow(wallpaper)
        {
            Owner = owner
        };
        detailsWindow.Show();
        BringWindowToFront(detailsWindow);
    }

    private static void BringWindowToFront(Window window)
    {
        window.Topmost = true;
        window.Topmost = false;
        window.Activate();
        window.Focus();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isEmbedded)
            Close();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isEmbedded)
            WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isEmbedded)
            return;

        if (_isCustomMaximized)
        {
            Left = _restoreBounds.Left;
            Top = _restoreBounds.Top;
            Width = _restoreBounds.Width;
            Height = _restoreBounds.Height;
            _isCustomMaximized = false;
        }
        else
        {
            _restoreBounds = new Rect(Left, Top, Width, Height);
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Left;
            Top = workArea.Top;
            Width = workArea.Width;
            Height = workArea.Height;
            _isCustomMaximized = true;
        }

        MaximizeIcon.Kind = _isCustomMaximized
            ? MahApps.Metro.IconPacks.PackIconLucideKind.Copy
            : MahApps.Metro.IconPacks.PackIconLucideKind.Square;
        UpdateWindowChrome();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isEmbedded)
            return;

        if (!_isCustomMaximized && e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void ResizeGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isEmbedded)
            return;

        if (_isCustomMaximized || e.ButtonState != MouseButtonState.Pressed)
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
        MaximizeIcon.Kind = MahApps.Metro.IconPacks.PackIconLucideKind.Square;
    }

    private void ResizeGrip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isResizing)
            return;

        _isResizing = false;
        ((UIElement)sender).ReleaseMouseCapture();
        e.Handled = true;
    }

    private sealed record TagChip(string Text, Brush Background);

    private sealed class WallhavenResponse
    {
        [JsonPropertyName("data")]
        public List<WallhavenItem>? Data { get; init; }

        [JsonPropertyName("meta")]
        public WallhavenMeta? Meta { get; init; }
    }

    private sealed class WallhavenMeta
    {
        [JsonPropertyName("last_page")]
        public int LastPage { get; init; }
    }

    private sealed class WallhavenItem
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("url")]
        public string? Url { get; init; }

        [JsonPropertyName("path")]
        public string? Path { get; init; }

        [JsonPropertyName("resolution")]
        public string? Resolution { get; init; }

        [JsonPropertyName("category")]
        public string? Category { get; init; }

        [JsonPropertyName("purity")]
        public string? Purity { get; init; }

        [JsonPropertyName("file_type")]
        public string? FileType { get; init; }

        [JsonPropertyName("file_size")]
        public long? FileSize { get; init; }

        [JsonPropertyName("thumbs")]
        public WallhavenThumbs? Thumbs { get; init; }
    }

    private sealed class WallhavenThumbs
    {
        [JsonPropertyName("large")]
        public string? Large { get; init; }
    }
}

public sealed record WallpaperCard(
    string Id,
    string ThumbnailUrl,
    string PageUrl,
    string? FullImageUrl,
    string Resolution,
    string? Category,
    string? Purity,
    string? FileType,
    long? FileSize)
{
    public ImageSource? ThumbnailImage { get; private set; } =
        CreateImage(ThumbnailUrl, 320);

    public void ReleaseThumbnail() => ThumbnailImage = null;

    public static BitmapImage CreateImage(string url, int decodePixelWidth)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(url, UriKind.Absolute);
        image.DecodePixelWidth = decodePixelWidth;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        image.EndInit();
        if (image.CanFreeze && !image.IsDownloading)
            image.Freeze();
        return image;
    }
}
