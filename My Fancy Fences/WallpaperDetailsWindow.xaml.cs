using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace My_Fancy_Fences;

public partial class WallpaperDetailsWindow : Window
{
    private const int SpiSetDeskWallpaper = 0x0014;
    private const int SpifUpdateIniFile = 0x01;
    private const int SpifSendWinIniChange = 0x02;

    private static readonly HttpClient HttpClient = CreateHttpClient();
    private static readonly string FavoritesFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "My Fancy Fences",
        "wallpaper-favorites.json");
    private readonly WallpaperCard _wallpaper;
    private readonly ObservableCollection<PropertyRow> _properties = [];
    private string? _fullImageUrl;
    private bool _isCustomMaximized;
    private bool _isResizing;
    private Rect _restoreBounds;
    private Point _resizeStartScreenPosition;
    private double _resizeStartWidth;
    private double _resizeStartHeight;

    public WallpaperDetailsWindow(WallpaperCard wallpaper)
    {
        InitializeComponent();
        Icon = AppIconProvider.Image;
        ApplyInitialWindowSize();

        _wallpaper = wallpaper;
        _fullImageUrl = string.IsNullOrWhiteSpace(wallpaper.FullImageUrl)
            ? wallpaper.PageUrl
            : wallpaper.FullImageUrl;

        _wallpaper.IsFavorite = IsFavoriteStored(_wallpaper.Id);
        FavoriteDetailsButton.DataContext = _wallpaper;
        PropertiesItemsControl.ItemsSource = _properties;
        WallpaperLinkText.Text = _wallpaper.PageUrl;
        WallpaperPreviewImage.Source = WallpaperCard.CreateImage(wallpaper.ThumbnailUrl, 720);
        ApplyFallbackProperties();
        SizeChanged += (_, _) => ApplyRoundedWindowClip();

        Loaded += async (_, _) =>
        {
            ApplyRoundedWindowClip();
            StartVideoPreviewIfAvailable();
            await LoadDetailsAsync();
        };
        Closed += (_, _) => ReleaseImageResources();
    }

    private void ApplyRoundedWindowClip()
    {
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
        OuterWindowBorder.CornerRadius = _isCustomMaximized ? new CornerRadius(0) : new CornerRadius(13);
        TitleBarBorder.CornerRadius = _isCustomMaximized ? new CornerRadius(0) : new CornerRadius(12, 12, 0, 0);
        FooterBorder.CornerRadius = _isCustomMaximized ? new CornerRadius(0) : new CornerRadius(0, 0, 12, 12);
        ResizeGrip.Visibility = _isCustomMaximized ? Visibility.Collapsed : Visibility.Visible;
        ApplyRoundedWindowClip();
    }

    private void ApplyInitialWindowSize()
    {
        var workArea = SystemParameters.WorkArea;
        Width = Math.Max(MinWidth, workArea.Width * 0.85);
        Height = Math.Max(MinHeight, workArea.Height * 0.85);
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + (workArea.Height - Height) / 2;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("MyFancyFences", "1.0"));
        return client;
    }

    private void ApplyFallbackProperties()
    {
        _properties.Clear();
        AddProperty("ID", _wallpaper.Id);
        AddProperty(LocalizationService.T("Rozdzielczość"), _wallpaper.Resolution);
        AddProperty(LocalizationService.T("Kategoria"), _wallpaper.Category);
        AddProperty(LocalizationService.T("Czystość"), _wallpaper.Purity);
        AddProperty(LocalizationService.T("Typ"), _wallpaper.FileType);
        AddProperty(LocalizationService.T("Rozmiar"), FormatFileSize(_wallpaper.FileSize));
    }

    private async Task LoadDetailsAsync()
    {
        if (_wallpaper.Id.StartsWith("moewalls:", StringComparison.OrdinalIgnoreCase))
        {
            TagsItemsControl.ItemsSource = Array.Empty<string>();
            TagsLoadingText.Text = LocalizationService.T("Szczegóły dla MoeWalls są w trakcie tworzenia.");
            StopLoadingAnimation();
            return;
        }

        StartLoadingAnimation();
        TagsLoadingText.Visibility = Visibility.Visible;

        try
        {
            using var response = await HttpClient.GetAsync($"https://wallhaven.cc/api/v1/w/{Uri.EscapeDataString(_wallpaper.Id)}");
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();
            var result = await JsonSerializer.DeserializeAsync<WallhavenDetailsResponse>(stream);
            var item = result?.Data;

            if (item is not null)
            {
                _fullImageUrl = item.Path ?? _fullImageUrl;
                WallpaperPreviewImage.Source = WallpaperCard.CreateImage(
                    item.Path ?? _wallpaper.ThumbnailUrl,
                    1400);

                _properties.Clear();
                AddProperty("ID", item.Id);
                AddProperty(LocalizationService.T("Rozdzielczość"), item.Resolution);
                AddProperty(LocalizationService.T("Wymiary"), FormatDimensions(item.DimensionX, item.DimensionY));
                AddProperty("Ratio", item.Ratio);
                AddProperty(LocalizationService.T("Kategoria"), item.Category);
                AddProperty(LocalizationService.T("Czystość"), item.Purity);
                AddProperty(LocalizationService.T("Typ"), item.FileType);
                AddProperty(LocalizationService.T("Rozmiar"), FormatFileSize(item.FileSize));
                AddProperty(LocalizationService.T("Wyświetlenia"), item.Views?.ToString());
                AddProperty(LocalizationService.T("Ulubione"), item.Favorites?.ToString());
                AddProperty(LocalizationService.T("Dodano"), item.CreatedAt);

                var tags = item.Tags?
                    .Select(tag => tag.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Cast<string>()
                    .ToList() ?? [];

                TagsItemsControl.ItemsSource = tags;
                TagsLoadingText.Visibility = tags.Count == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                TagsLoadingText.Text = tags.Count == 0
                    ? LocalizationService.T("Brak tagów dla tej tapety.")
                    : string.Empty;
            }
        }
        catch
        {
            TagsLoadingText.Text = LocalizationService.T("Nie udało się pobrać tagów.");
        }
        finally
        {
            StopLoadingAnimation();
        }
    }

    private void AddProperty(string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            _properties.Add(new PropertyRow(name, value));
    }

    private static string? FormatDimensions(int? width, int? height)
    {
        return width > 0 && height > 0
            ? $"{width}×{height}"
            : null;
    }

    private static string? FormatFileSize(long? bytes)
    {
        if (bytes is null or <= 0)
            return null;

        var value = bytes.Value;
        string[] units = ["B", "KB", "MB", "GB"];
        var unit = 0;
        var size = (double)value;

        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.#} {units[unit]}";
    }

    private async Task<string?> DownloadWallpaperAsync()
    {
        if (string.IsNullOrWhiteSpace(_fullImageUrl))
            return null;

        var downloadsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");
        var targetDirectory = Path.Combine(downloadsDirectory, "My Fancy Fences");
        Directory.CreateDirectory(targetDirectory);

        var extension = Path.GetExtension(new Uri(_fullImageUrl).AbsolutePath);
        if (string.IsNullOrWhiteSpace(extension))
            extension = ".jpg";

        var targetPath = Path.Combine(targetDirectory, $"{_wallpaper.Id}{extension}");
        if (File.Exists(targetPath) && IsValidImageFile(targetPath))
            return targetPath;

        if (File.Exists(targetPath))
            File.Delete(targetPath);

        StatusText.Text = LocalizationService.T("Pobieranie tapety...");
        using var response = await HttpClient.GetAsync(_fullImageUrl);
        response.EnsureSuccessStatusCode();

        var temporaryPath = $"{targetPath}.{Guid.NewGuid():N}.download";
        try
        {
            await using var source = await response.Content.ReadAsStreamAsync();
            await using (var target = File.Create(temporaryPath))
                await source.CopyToAsync(target);

            if (!IsValidImageFile(temporaryPath))
                throw new InvalidDataException(LocalizationService.T("Pobrany plik nie jest prawidłowym obrazem."));

            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }

        return targetPath;
    }

    private static bool IsValidImageFile(string path)
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            return decoder.Frames.Count > 0 &&
                   decoder.Frames[0].PixelWidth > 0 &&
                   decoder.Frames[0].PixelHeight > 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> PrepareWallpaperForWindowsAsync(string sourcePath)
    {
        var wallpaperDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "My Fancy Fences",
            "Wallpapers");
        Directory.CreateDirectory(wallpaperDirectory);

        var targetPath = Path.Combine(wallpaperDirectory, $"{_wallpaper.Id}.jpg");
        if (File.Exists(targetPath) && IsValidImageFile(targetPath))
            return targetPath;

        return await Task.Run(() =>
        {
            using var source = File.Open(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var decoder = BitmapDecoder.Create(
                source,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            var converted = new FormatConvertedBitmap(
                decoder.Frames[0],
                PixelFormats.Bgr24,
                null,
                0);
            converted.Freeze();

            var encoder = new JpegBitmapEncoder { QualityLevel = 96 };
            encoder.Frames.Add(BitmapFrame.Create(converted));

            var temporaryPath = $"{targetPath}.{Guid.NewGuid():N}.tmp";
            try
            {
                using (var target = File.Create(temporaryPath))
                    encoder.Save(target);
                File.Move(temporaryPath, targetPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }

            return targetPath;
        });
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = await DownloadWallpaperAsync();
            StatusText.Text = path is null
                ? LocalizationService.T("Nie udało się pobrać tapety.")
                : $"{LocalizationService.T("Pobrano")}: {path}";
        }
        catch
        {
            StatusText.Text = LocalizationService.T("Nie udało się pobrać tapety.");
        }
    }

    private void FavoriteDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_wallpaper.Id))
            return;

        var favorites = LoadFavorites();
        var existing = favorites.FindIndex(item =>
            string.Equals(item.Id, _wallpaper.Id, StringComparison.OrdinalIgnoreCase));

        if (existing >= 0)
        {
            favorites.RemoveAt(existing);
            _wallpaper.IsFavorite = false;
        }
        else
        {
            favorites.Add(FavoriteWallpaper.FromCard(_wallpaper));
            _wallpaper.IsFavorite = true;
        }

        SaveFavorites(favorites);
    }

    private void OpenLinkButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_wallpaper.PageUrl))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(_wallpaper.PageUrl)
            {
                UseShellExecute = true
            });
        }
        catch
        {
            StatusText.Text = LocalizationService.T("Nie udało się otworzyć linku.");
        }
    }

    private async void SetWallpaperButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_wallpaper.VideoPreviewUrl) &&
                Uri.TryCreate(_wallpaper.VideoPreviewUrl, UriKind.Absolute, out var liveWallpaperUri))
            {
                StatusText.Text = await LiveWallpaperService.TrySetAsync(liveWallpaperUri)
                    ? LocalizationService.T("Ustawiono ruchomą tapetę.")
                    : LocalizationService.T("Nie udało się ustawić ruchomej tapety.");
                return;
            }

            var path = await DownloadWallpaperAsync();
            if (path is null)
            {
                StatusText.Text = LocalizationService.T("Nie udało się pobrać tapety.");
                return;
            }

            var windowsWallpaperPath = await PrepareWallpaperForWindowsAsync(path);

            var success = SystemParametersInfo(
                SpiSetDeskWallpaper,
                0,
                windowsWallpaperPath,
                SpifUpdateIniFile | SpifSendWinIniChange);

            StatusText.Text = success
                ? LocalizationService.T("Ustawiono jako tapetę.")
                : LocalizationService.T("Windows nie pozwolił ustawić tapety.");
        }
        catch
        {
            StatusText.Text = LocalizationService.T("Nie udało się ustawić tapety.");
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
        LoadingRotateTransform.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, animation);
    }

    private void StopLoadingAnimation()
    {
        LoadingRotateTransform.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
        LoadingOverlay.Visibility = Visibility.Collapsed;
    }

    private void StartVideoPreviewIfAvailable()
    {
        if (string.IsNullOrWhiteSpace(_wallpaper.VideoPreviewUrl))
            return;

        WallpaperPreviewVideo.Source = new Uri(_wallpaper.VideoPreviewUrl, UriKind.Absolute);
        WallpaperPreviewVideo.Visibility = Visibility.Visible;
        WallpaperPreviewVideo.Position = TimeSpan.Zero;
        WallpaperPreviewVideo.Play();
    }

    private void WallpaperPreviewVideo_MediaEnded(object sender, RoutedEventArgs e)
    {
        WallpaperPreviewVideo.Position = TimeSpan.Zero;
        WallpaperPreviewVideo.Play();
    }

    private void ReleaseImageResources()
    {
        StopLoadingAnimation();
        WallpaperPreviewVideo.Stop();
        WallpaperPreviewVideo.Source = null;
        WallpaperPreviewImage.Source = null;
        TagsItemsControl.ItemsSource = null;
        PropertiesItemsControl.ItemsSource = null;
        _properties.Clear();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
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
        if (!_isCustomMaximized && e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void ResizeGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SystemParametersInfo(
        int action,
        int param,
        string value,
        int update);

    private sealed record PropertyRow(string Name, string Value);

    private static bool IsFavoriteStored(string wallpaperId) =>
        LoadFavorites().Any(item =>
            string.Equals(item.Id, wallpaperId, StringComparison.OrdinalIgnoreCase));

    private static List<FavoriteWallpaper> LoadFavorites()
    {
        try
        {
            if (!File.Exists(FavoritesFilePath))
                return [];

            return JsonSerializer.Deserialize<List<FavoriteWallpaper>>(
                File.ReadAllText(FavoritesFilePath)) ?? [];
        }
        catch
        {
            return [];
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

    private sealed record FavoriteWallpaper(
        string Id,
        string ThumbnailUrl,
        string PageUrl,
        string? FullImageUrl,
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
                card.Resolution,
                card.Category,
                card.Purity,
                card.FileType,
                card.FileSize,
                DateTimeOffset.Now);
    }

    private sealed class WallhavenDetailsResponse
    {
        [JsonPropertyName("data")]
        public WallhavenDetailsItem? Data { get; init; }
    }

    private sealed class WallhavenDetailsItem
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("path")]
        public string? Path { get; init; }

        [JsonPropertyName("resolution")]
        public string? Resolution { get; init; }

        [JsonPropertyName("dimension_x")]
        public int? DimensionX { get; init; }

        [JsonPropertyName("dimension_y")]
        public int? DimensionY { get; init; }

        [JsonPropertyName("ratio")]
        public string? Ratio { get; init; }

        [JsonPropertyName("category")]
        public string? Category { get; init; }

        [JsonPropertyName("purity")]
        public string? Purity { get; init; }

        [JsonPropertyName("file_type")]
        public string? FileType { get; init; }

        [JsonPropertyName("file_size")]
        public long? FileSize { get; init; }

        [JsonPropertyName("views")]
        public int? Views { get; init; }

        [JsonPropertyName("favorites")]
        public int? Favorites { get; init; }

        [JsonPropertyName("created_at")]
        public string? CreatedAt { get; init; }

        [JsonPropertyName("tags")]
        public List<WallhavenTag>? Tags { get; init; }
    }

    private sealed class WallhavenTag
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }
    }
}
