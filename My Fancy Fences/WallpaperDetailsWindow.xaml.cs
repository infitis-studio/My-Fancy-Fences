using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace My_Fancy_Fences;

public partial class WallpaperDetailsWindow : Window
{
    private const int SpiSetDeskWallpaper = 0x0014;
    private const int SpifUpdateIniFile = 0x01;
    private const int SpifSendWinIniChange = 0x02;
    private const int MonitorDefaultToNearest = 2;
    private const int AbmGetTaskbarPos = 5;
    private const int AbeBottom = 3;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    private static readonly HttpClient HttpClient = CreateHttpClient();
    private static readonly string FavoritesFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "My Fancy Fences",
        "wallpaper-favorites.json");
    private static readonly string LiveWallpaperDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "My Fancy Fences",
        "Wallpapers",
        "Live");
    private readonly WallpaperCard _wallpaper;
    private readonly ObservableCollection<PropertyRow> _properties = [];
    private string? _fullImageUrl;
    private bool _isCustomMaximized;
    private bool _isResizing;
    private bool _isTitleBarDragPending;
    private Rect _restoreBounds;
    private Point _titleBarDragStartPosition;
    private Point _resizeStartScreenPosition;
    private double _resizeStartWidth;
    private double _resizeStartHeight;

    public WallpaperDetailsWindow(WallpaperCard wallpaper)
    {
        InitializeComponent();
        Icon = AppIconProvider.Image;
        StateChanged += Window_StateChanged;
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
        var isMaximized = _isCustomMaximized || WindowState == WindowState.Maximized;
        OuterWindowBorder.CornerRadius = isMaximized ? new CornerRadius(0) : new CornerRadius(13);
        TitleBarBorder.CornerRadius = isMaximized ? new CornerRadius(0) : new CornerRadius(12, 12, 0, 0);
        FooterBorder.CornerRadius = isMaximized ? new CornerRadius(0) : new CornerRadius(0, 0, 12, 12);
        ResizeGrip.Visibility = isMaximized ? Visibility.Collapsed : Visibility.Visible;
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
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
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

    private async Task<Uri?> PrepareLiveWallpaperVideoAsync()
    {
        if (!_wallpaper.Id.StartsWith("moewalls:", StringComparison.OrdinalIgnoreCase))
            return TryCreateAbsoluteUri(_wallpaper.VideoPreviewUrl);

        StatusText.Text = LocalizationService.T("Pobieranie tapety wideo w najwyższej jakości...");

        var highQualityUri = await ResolveMoeWallsHighQualityVideoAsync();
        if (highQualityUri is not null)
        {
            var localVideoPath = await DownloadLiveWallpaperVideoAsync(highQualityUri);
            if (!string.IsNullOrWhiteSpace(localVideoPath))
                return new Uri(localVideoPath);
        }

        return TryCreateAbsoluteUri(_wallpaper.VideoPreviewUrl);
    }

    private async Task<Uri?> ResolveMoeWallsHighQualityVideoAsync()
    {
        var downloadUri = CreateMoeWallsDownloadUri(_wallpaper.PageUrl, _wallpaper.ThumbnailUrl);
        if (downloadUri is not null)
            return downloadUri;

        if (string.IsNullOrWhiteSpace(_wallpaper.PageUrl))
            return null;

        try
        {
            using var response = await HttpClient.GetAsync(_wallpaper.PageUrl);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync();

            var fourKDownloadMatch = Regex.Match(
                html,
                "href\\s*=\\s*[\"']?(?<href>/dl/4k/\\d+/?)[\"'\\s>]",
                RegexOptions.IgnoreCase);
            if (fourKDownloadMatch.Success)
                return new Uri(NormalizeMoeWallsUrl(fourKDownloadMatch.Groups["href"].Value));

            var sourceMatches = Regex.Matches(
                html,
                "(?:contentUrl|src|href|content)\\s*[=:]\\s*[\"'](?<url>[^\"']+\\.(?:mp4|webm)(?:\\?[^\"']*)?)[\"']",
                RegexOptions.IgnoreCase);
            return sourceMatches
                .Select(match => NormalizeMoeWallsUrl(match.Groups["url"].Value))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => new Uri(value))
                .OrderByDescending(uri => ScoreVideoQuality(uri.ToString()))
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static Uri? CreateMoeWallsDownloadUri(string pageUrl, string thumbnailUrl)
    {
        var mediaId = ExtractMoeWallsMediaId(thumbnailUrl);
        if (string.IsNullOrWhiteSpace(mediaId))
            mediaId = ExtractMoeWallsMediaId(pageUrl);

        return string.IsNullOrWhiteSpace(mediaId)
            ? null
            : new Uri($"https://motionbgs.com/dl/4k/{mediaId}/");
    }

    private async Task<string?> DownloadLiveWallpaperVideoAsync(Uri videoUri)
    {
        Directory.CreateDirectory(LiveWallpaperDirectory);

        using var response = await HttpClient.GetAsync(videoUri, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var fileName = GetResponseFileName(response) ??
                       CreateLiveWallpaperFileName(videoUri);
        fileName = SanitizeFileName(fileName);
        if (!fileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) &&
            !fileName.EndsWith(".webm", StringComparison.OrdinalIgnoreCase))
        {
            fileName = $"{Path.GetFileNameWithoutExtension(fileName)}.mp4";
        }

        var targetPath = Path.Combine(LiveWallpaperDirectory, fileName);
        var expectedSize = response.Content.Headers.ContentLength ?? 0;
        if (File.Exists(targetPath))
        {
            var existing = new FileInfo(targetPath);
            if (existing.Length > 1024 * 1024 &&
                (expectedSize <= 0 || existing.Length == expectedSize))
            {
                return targetPath;
            }
        }

        var temporaryPath = $"{targetPath}.{Guid.NewGuid():N}.download";
        try
        {
            await using var source = await response.Content.ReadAsStreamAsync();
            await using (var target = File.Create(temporaryPath))
                await source.CopyToAsync(target);

            var temporaryFile = new FileInfo(temporaryPath);
            if (!temporaryFile.Exists ||
                temporaryFile.Length < 1024 * 1024 ||
                (expectedSize > 0 && temporaryFile.Length != expectedSize))
            {
                throw new InvalidDataException(LocalizationService.T("Pobrany plik wideo jest niekompletny."));
            }

            File.Move(temporaryPath, targetPath, overwrite: true);
            return targetPath;
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static Uri? TryCreateAbsoluteUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? uri
            : null;

    private static string? GetResponseFileName(HttpResponseMessage response)
    {
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar ??
                       response.Content.Headers.ContentDisposition?.FileName;
        return string.IsNullOrWhiteSpace(fileName)
            ? null
            : fileName.Trim('"');
    }

    private string CreateLiveWallpaperFileName(Uri videoUri)
    {
        var fromUri = Path.GetFileName(videoUri.AbsolutePath);
        if (!string.IsNullOrWhiteSpace(fromUri) &&
            Path.HasExtension(fromUri))
        {
            return fromUri;
        }

        var slug = Uri.TryCreate(_wallpaper.PageUrl, UriKind.Absolute, out var pageUri)
            ? pageUri.Segments.LastOrDefault()?.Trim('/')
            : null;
        if (string.IsNullOrWhiteSpace(slug))
            slug = _wallpaper.Id.Replace("moewalls:", string.Empty, StringComparison.OrdinalIgnoreCase);

        return $"{slug}.mp4";
    }

    private static string SanitizeFileName(string fileName)
    {
        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(invalidCharacter, '-');

        return string.IsNullOrWhiteSpace(fileName)
            ? $"live-wallpaper-{Guid.NewGuid():N}.mp4"
            : fileName;
    }

    private static string? ExtractMoeWallsMediaId(string value)
    {
        var match = Regex.Match(value, @"/media/(?<id>\d+)/|/dl/(?:4k|hd)/(?<id>\d+)/?", RegexOptions.IgnoreCase);
        return match.Success
            ? match.Groups["id"].Value
            : null;
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

    private static int ScoreVideoQuality(string value)
    {
        var score = 0;
        var match = Regex.Match(value, @"(?<width>\d{3,5})x(?<height>\d{3,5})", RegexOptions.IgnoreCase);
        if (match.Success &&
            int.TryParse(match.Groups["width"].Value, out var width) &&
            int.TryParse(match.Groups["height"].Value, out var height))
        {
            score += width * height;
        }

        if (value.Contains("/dl/4k/", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("3840x2160", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("4k", StringComparison.OrdinalIgnoreCase))
        {
            score += 10_000_000;
        }

        return score;
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
            if (!string.IsNullOrWhiteSpace(_wallpaper.VideoPreviewUrl))
            {
                var liveWallpaperUri = await PrepareLiveWallpaperVideoAsync();
                if (liveWallpaperUri is null)
                {
                    StatusText.Text = LocalizationService.T("Nie udało się pobrać tapety.");
                    return;
                }

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
        if (_isCustomMaximized || WindowState == WindowState.Maximized)
            RestoreFromCustomMaximized();
        else
            MaximizeToCurrentMonitorWorkArea();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
            return;

        if (_isCustomMaximized || WindowState == WindowState.Maximized)
        {
            _isTitleBarDragPending = true;
            _titleBarDragStartPosition = e.GetPosition(this);
            TitleBarBorder.CaptureMouse();
            e.Handled = true;
            return;
        }

        TryDragMove();
    }

    private void TitleBar_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isTitleBarDragPending ||
            e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var currentPosition = e.GetPosition(this);
        if (Math.Abs(currentPosition.X - _titleBarDragStartPosition.X) < 4 &&
            Math.Abs(currentPosition.Y - _titleBarDragStartPosition.Y) < 4)
        {
            return;
        }

        _isTitleBarDragPending = false;
        TitleBarBorder.ReleaseMouseCapture();
        RestoreFromCustomMaximizedForDrag(currentPosition);
        DragMove();
    }

    private void TitleBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isTitleBarDragPending)
            return;

        _isTitleBarDragPending = false;
        TitleBarBorder.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void RestoreFromCustomMaximizedForDrag(Point mousePosition)
    {
        if (_restoreBounds.Width <= 0 || _restoreBounds.Height <= 0)
        {
            var workArea = GetCurrentMonitorWorkArea();
            _restoreBounds = new Rect(
                workArea.Left,
                workArea.Top,
                Math.Max(MinWidth, workArea.Width * 0.72),
                Math.Max(MinHeight, workArea.Height * 0.72));
        }

        var screenPoint = PointToScreen(mousePosition);
        var horizontalRatio = ActualWidth <= 0
            ? 0.5
            : Math.Clamp(mousePosition.X / ActualWidth, 0.08, 0.92);

        WindowState = WindowState.Normal;
        Width = _restoreBounds.Width;
        Height = _restoreBounds.Height;
        Left = screenPoint.X - Width * horizontalRatio;
        Top = screenPoint.Y - Math.Min(mousePosition.Y, 28);
        _isCustomMaximized = false;
        MaximizeIcon.Kind = MahApps.Metro.IconPacks.PackIconLucideKind.Square;
        UpdateWindowChrome();
    }

    private void MaximizeToCurrentMonitorWorkArea()
    {
        if (WindowState != WindowState.Normal)
            WindowState = WindowState.Normal;

        _restoreBounds = new Rect(
            Left,
            Top,
            Math.Max(MinWidth, ActualWidth > 0 ? ActualWidth : Width),
            Math.Max(MinHeight, ActualHeight > 0 ? ActualHeight : Height));

        SetWindowToCurrentMonitorSafeArea();
        _isCustomMaximized = true;
        MaximizeIcon.Kind = MahApps.Metro.IconPacks.PackIconLucideKind.Copy;
        UpdateWindowChrome();
    }

    private void RestoreFromCustomMaximized()
    {
        if (WindowState != WindowState.Normal)
            WindowState = WindowState.Normal;

        if (_restoreBounds.Width <= 0 || _restoreBounds.Height <= 0)
        {
            var workArea = GetCurrentMonitorWorkArea();
            _restoreBounds = new Rect(
                workArea.Left + workArea.Width * 0.075,
                workArea.Top + workArea.Height * 0.075,
                Math.Max(MinWidth, workArea.Width * 0.85),
                Math.Max(MinHeight, workArea.Height * 0.85));
        }

        Left = _restoreBounds.Left;
        Top = _restoreBounds.Top;
        Width = _restoreBounds.Width;
        Height = _restoreBounds.Height;
        _isCustomMaximized = false;
        MaximizeIcon.Kind = MahApps.Metro.IconPacks.PackIconLucideKind.Square;
        UpdateWindowChrome();
    }

    private Rect GetCurrentMonitorWorkArea()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
            if (monitor != IntPtr.Zero)
            {
                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                if (GetMonitorInfo(monitor, ref info))
                    return KeepAboveTaskbar(
                        DeviceRectToDipRect(info.Work),
                        DeviceRectToDipRect(info.Monitor));
            }
        }

        return KeepAboveTaskbar(
            SystemParameters.WorkArea,
            new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight));
    }

    private void SetWindowToCurrentMonitorSafeArea()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
            return;

        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
            return;

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
            return;

        var safe = GetSafeAreaDeviceRect(info);
        SetWindowPos(
            handle,
            IntPtr.Zero,
            safe.Left,
            safe.Top,
            Math.Max(1, safe.Width),
            Math.Max(1, safe.Height),
            SwpNoZOrder | SwpNoActivate);
    }

    private NativeRect GetSafeAreaDeviceRect(MonitorInfo info)
    {
        var safe = info.Monitor;
        var workBottomGap = Math.Max(0, info.Monitor.Bottom - info.Work.Bottom);
        var taskbarHeight = workBottomGap;

        var data = new AppBarData { Size = Marshal.SizeOf<AppBarData>() };
        if (SHAppBarMessage(AbmGetTaskbarPos, ref data) != IntPtr.Zero && data.Edge == AbeBottom)
        {
            var overlapsHorizontally = info.Monitor.Left < data.Rect.Right && info.Monitor.Right > data.Rect.Left;
            if (overlapsHorizontally)
                taskbarHeight = Math.Max(taskbarHeight, data.Rect.Height);
        }

        if (taskbarHeight < 24 || taskbarHeight > 240)
            taskbarHeight = GetFallbackTaskbarHeightDevice();

        safe.Bottom = Math.Max(safe.Top + 1, safe.Bottom - taskbarHeight);
        return safe;
    }

    private int GetFallbackTaskbarHeightDevice()
    {
        var source = PresentationSource.FromVisual(this);
        var scaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1;
        return Math.Max(48, (int)Math.Round(56 * scaleY));
    }

    private Rect DeviceRectToDipRect(NativeRect rect)
    {
        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = transform.Transform(new Point(rect.Left, rect.Top));
        var bottomRight = transform.Transform(new Point(rect.Right, rect.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    private Rect KeepAboveTaskbar(Rect workArea, Rect monitorArea)
    {
        var bottomTaskbarHeight = Math.Max(0, monitorArea.Bottom - workArea.Bottom);
        var data = new AppBarData { Size = Marshal.SizeOf<AppBarData>() };
        if (SHAppBarMessage(AbmGetTaskbarPos, ref data) != IntPtr.Zero && data.Edge == AbeBottom)
        {
            var taskbar = DeviceRectToDipRect(data.Rect);
            var overlapsHorizontally = monitorArea.Left < taskbar.Right && monitorArea.Right > taskbar.Left;
            if (overlapsHorizontally)
                bottomTaskbarHeight = Math.Max(bottomTaskbarHeight, taskbar.Height);
        }

        if (bottomTaskbarHeight < 24)
            bottomTaskbarHeight = 56;
        else if (bottomTaskbarHeight > 160)
            bottomTaskbarHeight = 56;

        var safeHeight = Math.Max(MinHeight, monitorArea.Height - bottomTaskbarHeight);

        return new Rect(
            monitorArea.Left,
            monitorArea.Top,
            monitorArea.Width,
            safeHeight);
    }

    private void TryDragMove()
    {
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Maximized)
            return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (WindowState != WindowState.Maximized)
                return;

            WindowState = WindowState.Normal;
            MaximizeToCurrentMonitorWorkArea();
        }));
    }

    private void ResizeGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isCustomMaximized ||
            WindowState == WindowState.Maximized ||
            e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

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

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    [DllImport("shell32.dll")]
    private static extern IntPtr SHAppBarMessage(int dwMessage, ref AppBarData pData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SystemParametersInfo(
        int action,
        int param,
        string value,
        int update);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public int Size;
        public IntPtr Window;
        public uint CallbackMessage;
        public uint Edge;
        public NativeRect Rect;
        public int Param;
    }

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
