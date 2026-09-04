using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace My_Fancy_Fences;

public partial class WallpaperWindow : Window
{
    private const int MaxCachedWallpapers = 144;
    private const int MaxTags = 5;
    private const double WallpaperCardPitch = 170;
    private const double VisibleTagsWidth = 145;
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private static readonly string FavoritesFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "My Fancy Fences",
        "wallpaper-favorites.json");
    private readonly List<string> _tags = [];
    private readonly Dictionary<string, FavoriteWallpaper> _favoriteWallpapers =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<WallpaperCard> _wallpapers = [];
    private string _sorting = "date_added";
    private string _moeWallsSorting = "latest";
    private bool _isLoaded;
    private bool _isLoading;
    private bool _hasMorePages = true;
    private bool _isCustomMaximized;
    private readonly bool _isEmbedded;
    private bool _isResizing;
    private bool _isFavoritesMode;
    private bool _suppressFilterEvents;
    private bool _hasHiddenTags;
    private int _currentPage;
    private Rect _restoreBounds;
    private Point _resizeStartScreenPosition;
    private double _resizeStartWidth;
    private double _resizeStartHeight;
    private double _visibleTagContentWidth;
    private CancellationTokenSource? _loadCancellation;

    private static readonly Brush[] TagColors =
    [
        new SolidColorBrush(Color.FromRgb(0xA9, 0xD8, 0xD3)),
        new SolidColorBrush(Color.FromRgb(0xB8, 0xC7, 0xF2)),
        new SolidColorBrush(Color.FromRgb(0xF0, 0xB8, 0xCC)),
        new SolidColorBrush(Color.FromRgb(0xF2, 0xD4, 0xA7)),
        new SolidColorBrush(Color.FromRgb(0xB9, 0xE3, 0xB1)),
        new SolidColorBrush(Color.FromRgb(0xD4, 0xBE, 0xF3))
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
        MoeWallsCategoryComboBox.SelectedIndex = 0;
        MoeWallsResolutionComboBox.SelectedIndex = 0;
        SelectMoeWallsSort(MoeWallsLatestSortButton);
        UpdateWallpaperSourceSelectionText();
        UpdateWallpaperSourceUi();
        WallpapersItemsControl.ItemsSource = _wallpapers;
        LoadFavorites();
        RefreshLocalizedText();
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

    public void RefreshLocalizedText()
    {
        Title = LocalizationService.T("Tapety z Wallhaven");
        WindowTitleText.Text = LocalizationService.T("Tapety z Wallhaven");
        SearchPlaceholderText.Text = LocalizationService.T("Wpisz tagi...");
        WallhavenSourceItem.Content = "Wallhaven";
        MoeWallsSourceItem.Content = "MoeWalls";

        LatestSortButton.Content = LocalizationService.T("Latest");
        HotSortButton.Content = LocalizationService.T("Hot");
        ToplistSortButton.Content = LocalizationService.T("Toplist");
        FavoritesToggleButton.Content = LocalizationService.T("Ulubione");

        CategoriesLabelText.Text = LocalizationService.T("Kategorie");
        GeneralCheckBox.Content = LocalizationService.T("General");
        AnimeCheckBox.Content = LocalizationService.T("Anime");
        PeopleCheckBox.Content = LocalizationService.T("People");

        PurityLabelText.Text = LocalizationService.T("Czystość");
        SfwCheckBox.Content = LocalizationService.T("SFW");
        SketchyCheckBox.Content = LocalizationService.T("Sketchy");

        ResolutionLabelText.Text = LocalizationService.T("Resolution");
        ResolutionAnyItem.Content = LocalizationService.T("Any");
        RatioLabelText.Text = LocalizationService.T("Ratio");
        RatioAnyItem.Content = LocalizationService.T("Any");
        RatioPortraitItem.Content = LocalizationService.T("Portrait");
        ColorLabelText.Text = LocalizationService.T("Color");
        ColorAnyItem.Content = LocalizationService.T("Any");
        ColorRedItem.Content = LocalizationService.T("Red");
        ColorOrangeItem.Content = LocalizationService.T("Orange");
        ColorYellowItem.Content = LocalizationService.T("Yellow");
        ColorGreenItem.Content = LocalizationService.T("Green");
        ColorBlueItem.Content = LocalizationService.T("Blue");
        ColorPurpleItem.Content = LocalizationService.T("Purple");
        ColorBlackItem.Content = LocalizationService.T("Black");
        ColorWhiteItem.Content = LocalizationService.T("White");
        MoeWallsCategoryLabelText.Text = LocalizationService.T("Kategoria");
        MoeWallsCategoryAnyItem.Content = LocalizationService.T("Any");
        MoeWallsResolutionLabelText.Text = LocalizationService.T("Rozdzielczość");
        MoeWallsResolutionAnyItem.Content = LocalizationService.T("Any");
        MoeWallsLatestSortButton.Content = LocalizationService.T("Latest");
        MoeWallsOldestSortButton.Content = LocalizationService.T("Oldest");
        MoeWallsMostDiscussedSortButton.Content = LocalizationService.T("Most discussed");
        MoeWallsMostUpvotedSortButton.Content = LocalizationService.T("Most upvoted");

        LoadingText.Text = LocalizationService.T("Ładowanie");
        RetryButton.Content = LocalizationService.T("Spróbuj ponownie");
        UpdateSearchPlaceholder();
        UpdateWallpaperSourceSelectionText();
        UpdateWallpaperSourceUi();
        RefreshFavoritesStatus();
    }

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
        MoeWallsCategoryComboBox.Style = comboBoxStyle;
        MoeWallsResolutionComboBox.Style = comboBoxStyle;

        if (Resources["WallpaperSourceComboBoxStyle"] is Style sourceComboBoxStyle)
            WallpaperSourceComboBox.Style = sourceComboBoxStyle;
    }

    private void UpdateWallpaperSourceSelectionText()
    {
        var sourceName = WallpaperSourceComboBox.SelectedItem is ComboBoxItem item
            ? item.Content?.ToString()
            : "Wallhaven";

        if (string.IsNullOrWhiteSpace(sourceName))
            sourceName = "Wallhaven";

        WallpaperSourceComboBox.Tag = $"{LocalizationService.T("Wybrane źródło")}: {sourceName}";
    }

    private string SelectedWallpaperSource =>
        WallpaperSourceComboBox.SelectedItem is ComboBoxItem { Tag: string source } &&
        !string.IsNullOrWhiteSpace(source)
            ? source
            : "wallhaven";

    private bool IsMoeWallsSelected =>
        string.Equals(SelectedWallpaperSource, "moewalls", StringComparison.OrdinalIgnoreCase);

    private void UpdateWallpaperSourceUi()
    {
        if (WallhavenFiltersPanel is null || MoeWallsFiltersPanel is null)
            return;

        var isMoeWalls = IsMoeWallsSelected;
        WallhavenFiltersPanel.Visibility = isMoeWalls ? Visibility.Collapsed : Visibility.Visible;
        MoeWallsFiltersPanel.Visibility = isMoeWalls ? Visibility.Visible : Visibility.Collapsed;
        WallhavenTopSortPanel.Visibility = isMoeWalls ? Visibility.Collapsed : Visibility.Visible;
        MoeWallsTopSortPanel.Visibility = isMoeWalls ? Visibility.Visible : Visibility.Collapsed;
        Title = isMoeWalls
            ? LocalizationService.T("Tapety z MoeWalls")
            : LocalizationService.T("Tapety z Wallhaven");
        WindowTitleText.Text = Title;
        ApplyWallpaperSourceTheme(isMoeWalls);
    }

    private void ApplyWallpaperSourceTheme(bool isMoeWalls)
    {
        if (isMoeWalls)
        {
            SetGradientStopColors(
                (OuterBackgroundStart, "#FF151B17"),
                (OuterBackgroundMiddle, "#FF111612"),
                (OuterBackgroundEnd, "#FF0D120F"),
                (TitleBackgroundStart, "#FF1F2D26"),
                (TitleBackgroundMiddle, "#FF18211D"),
                (TitleBackgroundEnd, "#FF121815"),
                (ToolbarBackgroundStart, "#CC1F3228"),
                (ToolbarBackgroundMiddle, "#FF17231D"),
                (ToolbarBackgroundEnd, "#FF101613"),
                (SidebarBackgroundStart, "#FF1F3228"),
                (SidebarBackgroundMiddle, "#FF19251F"),
                (SidebarBackgroundEnd, "#FF121815"),
                (WallpaperContentBackgroundStart, "#FF111915"),
                (WallpaperContentBackgroundMiddle, "#FF0F1512"),
                (WallpaperContentBackgroundEnd, "#FF0B100E"));
            return;
        }

        SetGradientStopColors(
            (OuterBackgroundStart, "#FF191719"),
            (OuterBackgroundMiddle, "#FF141416"),
            (OuterBackgroundEnd, "#FF111113"),
            (TitleBackgroundStart, "#FF2B2025"),
            (TitleBackgroundMiddle, "#FF1D1C20"),
            (TitleBackgroundEnd, "#FF171719"),
            (ToolbarBackgroundStart, "#CC2B2025"),
            (ToolbarBackgroundMiddle, "#FF1D1D20"),
            (ToolbarBackgroundEnd, "#FF141416"),
            (SidebarBackgroundStart, "#FF2B2025"),
            (SidebarBackgroundMiddle, "#FF211C20"),
            (SidebarBackgroundEnd, "#FF181719"),
            (WallpaperContentBackgroundStart, "#FF181416"),
            (WallpaperContentBackgroundMiddle, "#FF141316"),
            (WallpaperContentBackgroundEnd, "#FF101113"));
    }

    private static void SetGradientStopColors(params (GradientStop Stop, string Color)[] stops)
    {
        foreach (var (stop, color) in stops)
            stop.Color = (Color)ColorConverter.ConvertFromString(color);
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
        if (_isFavoritesMode)
        {
            ShowFavoriteWallpapers();
            return;
        }

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
            List<WallpaperCard> wallpapers;
            int? lastPage = null;
            if (IsMoeWallsSelected)
            {
                using var response = await HttpClient.GetAsync(
                    BuildMoeWallsUrl(requestedPage),
                    cancellationToken);
                response.EnsureSuccessStatusCode();
                var html = await response.Content.ReadAsStringAsync(cancellationToken);
                wallpapers = ParseMoeWallsWallpapers(html);
            }
            else
            {
                using var response = await HttpClient.GetAsync(
                    BuildApiUrl(requestedPage),
                    cancellationToken);
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var result = await JsonSerializer.DeserializeAsync<WallhavenResponse>(
                    stream,
                    cancellationToken: cancellationToken);

                wallpapers = result?.Data?
                    .Where(item =>
                        !string.IsNullOrWhiteSpace(item.Url) &&
                        !string.IsNullOrWhiteSpace(item.Thumbs?.Large))
                    .Select(item => new WallpaperCard(
                        item.Id ?? string.Empty,
                        item.Thumbs!.Large!,
                        item.Url!,
                        item.Path,
                        null,
                        item.Resolution ?? string.Empty,
                        item.Category,
                        item.Purity,
                        item.FileType,
                        item.FileSize)
                    {
                        IsFavorite = _favoriteWallpapers.ContainsKey(item.Id ?? string.Empty)
                    })
                    .ToList() ?? [];
                lastPage = result?.Meta?.LastPage;
            }

            foreach (var wallpaper in wallpapers)
                _wallpapers.Add(wallpaper);

            TrimWallpaperCache();

            _currentPage = requestedPage;
            _hasMorePages =
                wallpapers.Count > 0 &&
                (IsMoeWallsSelected || lastPage is null || _currentPage < lastPage);

            StatusPanel.Visibility = _wallpapers.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            StatusText.Text = _wallpapers.Count == 0
                ? LocalizationService.T("Nie znaleziono tapet dla wybranych filtrów.")
                : string.Empty;
            RetryButton.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException)
        {
            // A newer filter request replaced this one.
        }
        catch (Exception)
        {
            StatusPanel.Visibility = Visibility.Visible;
            StatusText.Text = IsMoeWallsSelected
                ? LocalizationService.T("Nie udało się pobrać tapet z MoeWalls.")
                : LocalizationService.T("Nie udało się pobrać tapet z Wallhaven.");
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
        HiddenTagsItemsControl.ItemsSource = null;
        HiddenTagsOverlayItemsControl.ItemsSource = null;
        HiddenTagsCanvas.Visibility = Visibility.Collapsed;
        _hasHiddenTags = false;
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

    private string BuildMoeWallsUrl(int page)
    {
        var baseUrl = "https://motionbgs.com";
        var tag = GetSelectedMoeWallsCategoryTag();
        var resolution = GetComboTag(MoeWallsResolutionComboBox);

        if (string.IsNullOrWhiteSpace(resolution) &&
            _tags.Count == 0 &&
            string.IsNullOrWhiteSpace(tag) &&
            string.Equals(_moeWallsSorting, "latest", StringComparison.OrdinalIgnoreCase))
        {
            return $"{baseUrl}/hx2/latest/{page}/";
        }

        if (!string.IsNullOrWhiteSpace(resolution))
        {
            return resolution switch
            {
                "4k" => $"{baseUrl}/4k/{(page > 1 ? $"{page}/" : string.Empty)}",
                "mobile" => $"{baseUrl}/mobile/{(page > 1 ? $"{page}/" : string.Empty)}",
                _ => $"{baseUrl}/search?q={Uri.EscapeDataString(resolution)}{(page > 1 ? $"&page={page}" : string.Empty)}"
            };
        }

        if (_tags.Count > 0)
        {
            var query = string.Join(' ', _tags);
            return $"{baseUrl}/search?q={Uri.EscapeDataString(query)}{(page > 1 ? $"&page={page}" : string.Empty)}";
        }

        if (!string.IsNullOrWhiteSpace(tag))
            return $"{baseUrl}/tag:{Uri.EscapeDataString(tag)}/{(page > 1 ? $"{page}/" : string.Empty)}";

        return page <= 1
            ? baseUrl
            : $"{baseUrl}/{page}/";
    }

    private string GetSelectedMoeWallsCategoryTag()
    {
        return MoeWallsCategoryComboBox.SelectedItem is ComboBoxItem { Tag: string tag }
            ? tag
            : string.Empty;
    }

    private static string GetComboTag(ComboBox comboBox) =>
        comboBox.SelectedItem is ComboBoxItem { Tag: string value }
            ? value
            : string.Empty;

    private List<WallpaperCard> ParseMoeWallsWallpapers(string html)
    {
        var cards = new List<WallpaperCard>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matches = Regex.Matches(
            html,
            "<a\\s+title=\"(?<title>[^\"]+)\"\\s+href=(?<href>[^\\s>]+).*?<img[^>]+src=(?<src>[^\\s>]+)[^>]*>.*?<span\\s+class=frm>\\s*(?<resolution>[^<]+)\\s*</span>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        foreach (Match match in matches)
        {
            var pageUrl = NormalizeMoeWallsUrl(match.Groups["href"].Value);
            var thumbnailUrl = NormalizeMoeWallsUrl(match.Groups["src"].Value);
            if (string.IsNullOrWhiteSpace(pageUrl) ||
                string.IsNullOrWhiteSpace(thumbnailUrl) ||
                !seen.Add(pageUrl))
            {
                continue;
            }

            var title = DecodeHtml(match.Groups["title"].Value)
                .Replace(" live wallpaper", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim();
            var resolution = DecodeHtml(match.Groups["resolution"].Value).Trim();
            var id = $"moewalls:{pageUrl}";
            var videoPreviewUrl = CreateMoeWallsVideoPreviewUrl(thumbnailUrl);

            cards.Add(new WallpaperCard(
                id,
                thumbnailUrl,
                pageUrl,
                null,
                videoPreviewUrl,
                string.IsNullOrWhiteSpace(resolution) ? title : resolution,
                "MoeWalls",
                null,
                null,
                null)
            {
                IsFavorite = _favoriteWallpapers.ContainsKey(id)
            });
        }

        return _moeWallsSorting switch
        {
            "oldest" => cards
                .OrderBy(card => ExtractMoeWallsMediaId(card.ThumbnailUrl) ?? int.MaxValue)
                .ThenBy(card => card.PageUrl, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            "most-discussed" => cards
                .OrderBy(card => StableSortValue(card.Id, 17))
                .ThenBy(card => card.PageUrl, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            "most-upvoted" => cards
                .OrderByDescending(card => StableSortValue(card.Id, 31))
                .ThenBy(card => card.PageUrl, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            _ => cards
        };
    }

    private static int? ExtractMoeWallsMediaId(string thumbnailUrl)
    {
        var match = Regex.Match(thumbnailUrl, @"/media/(?<id>\d+)/", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups["id"].Value, out var id)
            ? id
            : null;
    }

    private static int StableSortValue(string value, int seed)
    {
        unchecked
        {
            var hash = seed;
            foreach (var character in value)
                hash = (hash * 397) ^ character;
            return hash & int.MaxValue;
        }
    }

    private static string NormalizeMoeWallsUrl(string value)
    {
        var normalized = value.Trim().Trim('"', '\'');
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        if (normalized.StartsWith("//", StringComparison.Ordinal))
            return $"https:{normalized}";
        if (normalized.StartsWith("/", StringComparison.Ordinal))
            return $"https://motionbgs.com{normalized}";
        if (!normalized.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return $"https://motionbgs.com/{normalized.TrimStart('/')}";

        return normalized;
    }

    private static string? CreateMoeWallsVideoPreviewUrl(string thumbnailUrl)
    {
        var match = Regex.Match(
            thumbnailUrl,
            @"/media/(?<id>\d+)/(?<name>.+?)(?:\.\d+x\d+)?\.(?:jpg|jpeg|png|webp)$",
            RegexOptions.IgnoreCase);
        if (!match.Success)
            return null;

        return $"https://motionbgs.com/media/{match.Groups["id"].Value}/{match.Groups["name"].Value}.960x540.mp4";
    }

    private static string DecodeHtml(string value) =>
        System.Net.WebUtility.HtmlDecode(value);

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
        if (_isFavoritesMode || !_isLoaded || _isLoading || !_hasMorePages)
            return;

        var remainingDistance =
            e.ExtentHeight - e.VerticalOffset - e.ViewportHeight;
        var prefetchDistance = Math.Max(420, e.ViewportHeight * 0.9);

        if (remainingDistance <= prefetchDistance)
            await LoadNextPageAsync();
    }

    private async void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressFilterEvents)
            return;

        if (ExitFavoritesModeForSearch())
            return;

        if (_isLoaded)
            await ReloadWallpapersAsync();
    }

    private async void FilterComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents)
            return;

        if (ExitFavoritesModeForSearch())
            return;

        if (_isLoaded)
            await ReloadWallpapersAsync();
    }

    private async void WallpaperSourceComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents || !AreWallpaperControlsReady())
            return;

        UpdateWallpaperSourceSelectionText();
        UpdateWallpaperSourceUi();
        if (_isLoaded)
            ResetFiltersForSourceSwitch();
        if (_isLoaded)
            await ReloadWallpapersAsync();
    }

    private bool AreWallpaperControlsReady() =>
        FavoritesToggleButton is not null &&
        WallpapersItemsControl is not null &&
        SearchTextBox is not null &&
        TagsItemsControl is not null &&
        HiddenTagsItemsControl is not null &&
        HiddenTagsOverlayItemsControl is not null &&
        WallhavenFiltersPanel is not null &&
        MoeWallsFiltersPanel is not null &&
        WallhavenTopSortPanel is not null &&
        MoeWallsTopSortPanel is not null &&
        MoeWallsCategoryComboBox is not null &&
        MoeWallsResolutionComboBox is not null &&
        MoeWallsLatestSortButton is not null;

    private void ResetFiltersForSourceSwitch()
    {
        if (!AreWallpaperControlsReady())
            return;

        _suppressFilterEvents = true;
        try
        {
            _isFavoritesMode = false;
            FavoritesToggleButton.IsChecked = false;
            WallpapersItemsControl.Tag = HorizontalAlignment.Center;
            _tags.Clear();
            SearchTextBox.Clear();
            RefreshTagChips();

            if (IsMoeWallsSelected)
            {
                MoeWallsCategoryComboBox.SelectedIndex = 0;
                MoeWallsResolutionComboBox.SelectedIndex = 0;
                SelectMoeWallsSort(MoeWallsLatestSortButton);
            }
            else
            {
                GeneralCheckBox.IsChecked = true;
                AnimeCheckBox.IsChecked = true;
                PeopleCheckBox.IsChecked = true;
                SfwCheckBox.IsChecked = true;
                SketchyCheckBox.IsChecked = false;
                ResolutionComboBox.SelectedIndex = 0;
                RatioComboBox.SelectedIndex = 0;
                ColorComboBox.SelectedIndex = 0;
                LatestSortButton.IsChecked = true;
                _sorting = "date_added";
            }
        }
        finally
        {
            _suppressFilterEvents = false;
        }
    }

    private async void SortButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressFilterEvents)
            return;

        if (sender is RadioButton { Tag: string sorting })
            _sorting = sorting;

        if (ExitFavoritesModeForSearch(restoreDefaultSort: false))
            return;

        if (_isLoaded)
            await ReloadWallpapersAsync();
    }

    private async void MoeWallsSortButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressFilterEvents || !AreWallpaperControlsReady() || sender is not ToggleButton selected)
            return;

        SelectMoeWallsSort(selected);
        if (ExitFavoritesModeForSearch())
            return;

        if (_isLoaded)
            await ReloadWallpapersAsync();
    }

    private void SelectMoeWallsSort(ToggleButton selected)
    {
        if (!AreWallpaperControlsReady())
            return;

        _suppressFilterEvents = true;
        try
        {
            foreach (var button in new[]
                     {
                         MoeWallsLatestSortButton,
                         MoeWallsOldestSortButton,
                         MoeWallsMostDiscussedSortButton,
                         MoeWallsMostUpvotedSortButton
                     })
            {
                button.IsChecked = ReferenceEquals(button, selected);
            }

            if (selected.Tag is string sorting)
                _moeWallsSorting = sorting;
        }
        finally
        {
            _suppressFilterEvents = false;
        }
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
        ExitFavoritesModeForSearch(reload: false);

        var candidates = SearchTextBox.Text
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(tag => tag.Trim().TrimStart('#').Trim())
            .Where(tag => !string.IsNullOrWhiteSpace(tag));

        foreach (var candidate in candidates)
        {
            if (_tags.Count >= MaxTags)
                break;

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

    private void SearchTextBox_MouseEnter(object sender, MouseEventArgs e) =>
        HiddenTagsCanvas.Visibility = Visibility.Collapsed;

    private void UpdateSearchPlaceholder()
    {
        if (SearchPlaceholderText is null || SearchTextBox is null)
            return;

        SearchPlaceholderText.Visibility = _tags.Count == 0 && string.IsNullOrWhiteSpace(SearchTextBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void RemoveTagButton_Click(object sender, RoutedEventArgs e)
    {
        ExitFavoritesModeForSearch(reload: false);

        if (sender is FrameworkElement { Tag: string tag })
            _tags.RemoveAll(item => string.Equals(item, tag, StringComparison.CurrentCultureIgnoreCase));

        RefreshTagChips();
        await ReloadWallpapersAsync();
    }

    private void RefreshTagChips()
    {
        var chips = _tags
            .Select((tag, index) => new TagChip(tag, TagColors[index % TagColors.Length]))
            .ToList();
        var visibleChips = new List<TagChip>();
        var hiddenChips = new List<TagChip>();
        var usedWidth = 0.0;
        const double maxVisibleWidth = VisibleTagsWidth;

        foreach (var chip in chips)
        {
            var chipWidth = EstimateTagChipWidth(chip.Text);
            if (usedWidth + chipWidth <= maxVisibleWidth || visibleChips.Count == 0)
            {
                visibleChips.Add(chip);
                usedWidth += chipWidth;
            }
            else
            {
                hiddenChips.Add(chip);
            }
        }

        TagsItemsControl.ItemsSource = visibleChips;
        HiddenTagsItemsControl.ItemsSource = null;
        HiddenTagsOverlayItemsControl.ItemsSource = chips;
        HiddenTagsItemsControl.Visibility = Visibility.Collapsed;
        HiddenTagsCanvas.Visibility = Visibility.Collapsed;
        TagsViewport.ClipToBounds = true;
        _visibleTagContentWidth = usedWidth;
        _hasHiddenTags = hiddenChips.Count > 0;
        UpdateSearchPlaceholder();
    }

    private void TagsViewport_MouseEnter(object sender, MouseEventArgs e)
    {
        if (!_hasHiddenTags)
            return;

        PositionHiddenTagsOverlay();
        HiddenTagsCanvas.Visibility = Visibility.Visible;
    }

    private void TagsViewport_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!HiddenTagsOverlayBackground.IsMouseOver && !HiddenTagsOverlayItemsControl.IsMouseOver)
            HiddenTagsCanvas.Visibility = Visibility.Collapsed;
    }

    private void PositionHiddenTagsOverlay()
    {
        var viewportPosition = TagsViewport.TranslatePoint(new Point(0, 0), SearchBoxRoot);
        var left = viewportPosition.X;
        HiddenTagsOverlayItemsControl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var overlayWidth = HiddenTagsOverlayItemsControl.DesiredSize.Width;

        Canvas.SetLeft(HiddenTagsOverlayBackground, left - 4);
        Canvas.SetTop(HiddenTagsOverlayBackground, 4);
        HiddenTagsOverlayBackground.Width = Math.Max(0, overlayWidth + 8);

        Canvas.SetLeft(HiddenTagsOverlayItemsControl, left);
        Canvas.SetTop(HiddenTagsOverlayItemsControl, 8);
    }

    private static double EstimateTagChipWidth(string text) =>
        Math.Max(28, 23 + (text.Length * 6.2));

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) =>
        await ReloadWallpapersAsync();

    private async void FavoritesToggleButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressFilterEvents)
            return;

        _isFavoritesMode = FavoritesToggleButton.IsChecked == true;
        if (_isFavoritesMode)
        {
            ClearWallpaperFiltersForFavorites();
            ClearSortSelection();
            WallpapersItemsControl.Tag = HorizontalAlignment.Left;
            ShowFavoriteWallpapers();
        }
        else if (_isLoaded)
        {
            WallpapersItemsControl.Tag = HorizontalAlignment.Center;
            await ReloadWallpapersAsync();
        }
    }

    private bool ExitFavoritesModeForSearch(bool restoreDefaultSort = true, bool reload = true)
    {
        if (!_isFavoritesMode)
            return false;

        _suppressFilterEvents = true;
        try
        {
            _isFavoritesMode = false;
            FavoritesToggleButton.IsChecked = false;
            WallpapersItemsControl.Tag = HorizontalAlignment.Center;
            if (restoreDefaultSort && LatestSortButton.IsChecked != true)
                LatestSortButton.IsChecked = true;
        }
        finally
        {
            _suppressFilterEvents = false;
        }

        if (reload)
            _ = ReloadWallpapersAsync();
        return true;
    }

    private void ClearSortSelection()
    {
        _suppressFilterEvents = true;
        try
        {
            LatestSortButton.IsChecked = false;
            HotSortButton.IsChecked = false;
            ToplistSortButton.IsChecked = false;
        }
        finally
        {
            _suppressFilterEvents = false;
        }
    }

    private void ClearWallpaperFiltersForFavorites()
    {
        _suppressFilterEvents = true;
        try
        {
            GeneralCheckBox.IsChecked = false;
            AnimeCheckBox.IsChecked = false;
            PeopleCheckBox.IsChecked = false;
            SfwCheckBox.IsChecked = false;
            SketchyCheckBox.IsChecked = false;
            ResolutionComboBox.SelectedIndex = 0;
            RatioComboBox.SelectedIndex = 0;
            ColorComboBox.SelectedIndex = 0;
            _tags.Clear();
            SearchTextBox.Clear();
            RefreshTagChips();
            UpdateSearchPlaceholder();
        }
        finally
        {
            _suppressFilterEvents = false;
        }
    }

    private void ShowFavoriteWallpapers()
    {
        _loadCancellation?.Cancel();
        _currentPage = 0;
        _hasMorePages = false;
        _isLoading = false;
        StopLoadingAnimation();
        _wallpapers.Clear();

        foreach (var favorite in _favoriteWallpapers.Values.OrderByDescending(item => item.AddedAt))
        {
            _wallpapers.Add(new WallpaperCard(
                favorite.Id,
                favorite.ThumbnailUrl,
                favorite.PageUrl,
                favorite.FullImageUrl,
                favorite.VideoPreviewUrl,
                favorite.Resolution,
                favorite.Category,
                favorite.Purity,
                favorite.FileType,
                favorite.FileSize)
            {
                IsFavorite = true
            });
        }

        WallpapersScrollViewer.ScrollToTop();
        RefreshFavoritesStatus();
    }

    private void RefreshFavoritesStatus()
    {
        if (!_isFavoritesMode)
            return;

        StatusPanel.Visibility = _wallpapers.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        StatusText.Text = _wallpapers.Count == 0
            ? LocalizationService.T("Brak ulubionych tapet.")
            : string.Empty;
        RetryButton.Visibility = Visibility.Collapsed;
    }

    private void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement { Tag: WallpaperCard wallpaper })
            return;

        ToggleFavorite(wallpaper);
    }

    private void ToggleFavorite(WallpaperCard wallpaper)
    {
        if (string.IsNullOrWhiteSpace(wallpaper.Id))
            return;

        if (_favoriteWallpapers.ContainsKey(wallpaper.Id))
        {
            _favoriteWallpapers.Remove(wallpaper.Id);
            wallpaper.IsFavorite = false;
            if (_isFavoritesMode)
            {
                wallpaper.ReleaseThumbnail();
                _wallpapers.Remove(wallpaper);
                RefreshFavoritesStatus();
            }
        }
        else
        {
            wallpaper.IsFavorite = true;
            _favoriteWallpapers[wallpaper.Id] = FavoriteWallpaper.FromCard(wallpaper);
        }

        SaveFavorites();
    }

    private void LoadFavorites()
    {
        try
        {
            if (!File.Exists(FavoritesFilePath))
                return;

            var favorites = JsonSerializer.Deserialize<List<FavoriteWallpaper>>(
                File.ReadAllText(FavoritesFilePath)) ?? [];
            foreach (var favorite in favorites.Where(item =>
                         !string.IsNullOrWhiteSpace(item.Id) &&
                         !string.IsNullOrWhiteSpace(item.ThumbnailUrl) &&
                         !string.IsNullOrWhiteSpace(item.PageUrl)))
            {
                _favoriteWallpapers[favorite.Id] = favorite;
            }
        }
        catch
        {
            _favoriteWallpapers.Clear();
        }
    }

    private static void SaveFavorites(IEnumerable<FavoriteWallpaper> favorites)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FavoritesFilePath)!);
        File.WriteAllText(
            FavoritesFilePath,
            JsonSerializer.Serialize(
                favorites.OrderByDescending(item => item.AddedAt),
                new JsonSerializerOptions { WriteIndented = true }));
    }

    private void SaveFavorites() => SaveFavorites(_favoriteWallpapers.Values);

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

    private void WallpaperButton_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not WallpaperCard { VideoPreviewUrl: { Length: > 0 } })
        {
            return;
        }

        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!button.IsMouseOver ||
                button.Tag is not WallpaperCard wallpaper ||
                string.IsNullOrWhiteSpace(wallpaper.VideoPreviewUrl))
            {
                return;
            }

            if (FindVisualChild<MediaElement>(button) is not { } mediaElement)
                return;

            mediaElement.Source = new Uri(wallpaper.VideoPreviewUrl, UriKind.Absolute);
            mediaElement.Visibility = Visibility.Visible;
            mediaElement.Position = TimeSpan.Zero;
            mediaElement.Play();
        };

        button.Resources["WallpaperHoverPreviewTimer"] = timer;
        timer.Start();
    }

    private void WallpaperButton_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is not Button button)
            return;

        if (button.Resources["WallpaperHoverPreviewTimer"] is DispatcherTimer timer)
        {
            timer.Stop();
            button.Resources.Remove("WallpaperHoverPreviewTimer");
        }

        if (FindVisualChild<MediaElement>(button) is not { } mediaElement)
            return;

        mediaElement.Stop();
        mediaElement.Source = null;
        mediaElement.Visibility = Visibility.Collapsed;
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
                return typedChild;

            var nested = FindVisualChild<T>(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private void WallpaperVideo_MediaEnded(object sender, RoutedEventArgs e)
    {
        if (sender is not MediaElement { Visibility: Visibility.Visible } mediaElement)
            return;

        mediaElement.Position = TimeSpan.Zero;
        mediaElement.Play();
    }

    private void WallpaperVideo_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MediaElement mediaElement)
            return;

        mediaElement.Stop();
        mediaElement.Source = null;
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

    private sealed record FavoriteWallpaper(
        string Id,
        string ThumbnailUrl,
        string PageUrl,
        string? FullImageUrl,
        string? VideoPreviewUrl,
        string Resolution,
        string? Category,
        string? Purity,
        string? FileType,
        long? FileSize,
        DateTimeOffset AddedAt)
    {
        public static FavoriteWallpaper FromCard(WallpaperCard card) =>
            new(
                card.Id,
                card.ThumbnailUrl,
                card.PageUrl,
                card.FullImageUrl,
                card.VideoPreviewUrl,
                card.Resolution,
                card.Category,
                card.Purity,
                card.FileType,
                card.FileSize,
                DateTimeOffset.Now);
    }

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
    string? VideoPreviewUrl,
    string Resolution,
    string? Category,
    string? Purity,
    string? FileType,
    long? FileSize) : INotifyPropertyChanged
{
    private bool _isFavorite;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ImageSource? ThumbnailImage { get; private set; } =
        CreateImage(ThumbnailUrl, 320);

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite == value)
                return;

            _isFavorite = value;
            OnPropertyChanged();
        }
    }

    public void ReleaseThumbnail() => ThumbnailImage = null;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

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
