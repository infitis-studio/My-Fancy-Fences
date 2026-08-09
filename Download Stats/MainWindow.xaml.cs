using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace MyFancyFencesStats;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private static readonly HttpClient Client = CreateClient();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string HistoryDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "My Fancy Fences Stats");
    private static readonly string HistoryFilePath = Path.Combine(HistoryDirectory, "download-history.json");

    private int _totalDownloads;
    private int _todayDownloads;
    private int _releaseCount;
    private string _status = "Łączenie z GitHubem…";

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += async (_, _) => await RefreshAsync();
    }

    public ObservableCollection<ReleaseStats> Releases { get; } = [];
    public ObservableCollection<DailyDownloadRow> DailyRows { get; } = [];

    public int TotalDownloads
    {
        get => _totalDownloads;
        private set => SetField(ref _totalDownloads, value);
    }

    public int TodayDownloads
    {
        get => _todayDownloads;
        private set => SetField(ref _todayDownloads, value);
    }

    public int ReleaseCount
    {
        get => _releaseCount;
        private set => SetField(ref _releaseCount, value);
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        RefreshButton.IsEnabled = false;
        Status = "Pobieranie aktualnych statystyk…";
        try
        {
            var releases = await LoadReleasesAsync();
            var snapshot = BuildSnapshot(releases);
            var history = await LoadHistoryAsync();
            UpsertToday(history, snapshot);
            await SaveHistoryAsync(history);

            Releases.Clear();
            foreach (var release in snapshot.Releases)
                Releases.Add(new ReleaseStats(release.Tag, release.Total));

            DailyRows.Clear();
            foreach (var row in BuildDailyRows(history))
                DailyRows.Add(row);

            TotalDownloads = snapshot.Total;
            TodayDownloads = DailyRows.FirstOrDefault()?.Downloads ?? 0;
            ReleaseCount = snapshot.Releases.Count;
            Status = $"Ostatnie odświeżenie: {DateTime.Now:dd.MM.yyyy HH:mm:ss} · historia: {HistoryFilePath}";
        }
        catch (Exception exception)
        {
            Status = $"Nie udało się pobrać statystyk: {exception.Message}";
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private static DownloadSnapshot BuildSnapshot(IReadOnlyList<GitHubRelease> releases)
    {
        var releaseSnapshots = releases
            .Select(release =>
            {
                var assets = release.Assets
                    .Select(asset => new AssetSnapshot(asset.Name, asset.DownloadCount, DetectVariant(asset.Name)))
                    .ToList();
                return new ReleaseSnapshot(
                    release.TagName,
                    assets.Sum(asset => asset.Downloads),
                    assets);
            })
            .ToList();

        return new DownloadSnapshot(
            DateOnly.FromDateTime(DateTime.Now),
            DateTime.Now,
            releaseSnapshots.Sum(release => release.Total),
            releaseSnapshots);
    }

    private static IReadOnlyList<DailyDownloadRow> BuildDailyRows(List<DownloadSnapshot> history)
    {
        var ordered = history
            .OrderByDescending(snapshot => snapshot.Date)
            .ToList();
        var rows = new List<DailyDownloadRow>();

        for (var index = 0; index < ordered.Count; index++)
        {
            var current = ordered[index];
            var previous = ordered
                .Skip(index + 1)
                .FirstOrDefault();
            var downloads = previous is null
                ? 0
                : Math.Max(0, current.Total - previous.Total);
            rows.Add(new DailyDownloadRow(
                current.Date.ToString("dd.MM.yyyy"),
                downloads,
                downloads == 0 ? "0" : $"+{downloads}"));
        }

        return rows;
    }

    private static void UpsertToday(List<DownloadSnapshot> history, DownloadSnapshot snapshot)
    {
        var existingIndex = history.FindIndex(item => item.Date == snapshot.Date);
        if (existingIndex >= 0)
            history[existingIndex] = snapshot;
        else
            history.Add(snapshot);

        history.Sort((left, right) => left.Date.CompareTo(right.Date));
    }

    private static async Task<List<DownloadSnapshot>> LoadHistoryAsync()
    {
        if (!File.Exists(HistoryFilePath))
            return [];

        try
        {
            await using var stream = File.OpenRead(HistoryFilePath);
            return await JsonSerializer.DeserializeAsync<List<DownloadSnapshot>>(stream) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static async Task SaveHistoryAsync(List<DownloadSnapshot> history)
    {
        Directory.CreateDirectory(HistoryDirectory);
        await using var stream = File.Create(HistoryFilePath);
        await JsonSerializer.SerializeAsync(stream, history, JsonOptions);
    }

    private static string DetectVariant(string assetName)
    {
        if (assetName.Contains("requires", StringComparison.OrdinalIgnoreCase) ||
            assetName.Contains("net10", StringComparison.OrdinalIgnoreCase))
            return "requires-net10";
        if (assetName.Contains("with", StringComparison.OrdinalIgnoreCase) ||
            assetName.Contains("portable", StringComparison.OrdinalIgnoreCase) ||
            assetName.Contains("self-contained", StringComparison.OrdinalIgnoreCase))
            return "with-net10";
        return "unknown";
    }

    private static async Task<List<GitHubRelease>> LoadReleasesAsync()
    {
        var ghPath = FindGitHubCli();
        if (ghPath is not null)
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = ghPath,
                Arguments = "api \"repos/infitis-studio/My-Fancy-Fences/releases?per_page=100\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }) ?? throw new InvalidOperationException("Nie udało się uruchomić GitHub CLI.");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var output = await outputTask;
            var error = await errorTask;
            if (process.ExitCode == 0)
                return JsonSerializer.Deserialize<List<GitHubRelease>>(output) ?? [];
            if (!string.IsNullOrWhiteSpace(error))
                throw new InvalidOperationException(error.Trim());
        }

        using var response = await Client.GetAsync(
            "repos/infitis-studio/My-Fancy-Fences/releases?per_page=100");
        if ((int)response.StatusCode == 403)
            throw new InvalidOperationException("Limit publicznego API GitHuba został wyczerpany. Zaloguj się poleceniem gh auth login.");
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync<List<GitHubRelease>>(stream) ?? [];
    }

    private static string? FindGitHubCli()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GitHub CLI", "gh.exe"),
            "gh.exe"
        };
        return candidates.FirstOrDefault(candidate =>
            Path.IsPathRooted(candidate) ? File.Exists(candidate) : CanStart(candidate));
    }

    private static bool CanStart(string executable)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = "--version",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            });
            process?.WaitForExit(2000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri("https://api.github.com/"),
            Timeout = TimeSpan.FromSeconds(15)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("My-Fancy-Fences-Stats");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; init; } = string.Empty;

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; init; } = [];
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("download_count")]
        public int DownloadCount { get; init; }
    }
}

public sealed record ReleaseStats(string Tag, int Total);

public sealed record DailyDownloadRow(string DateText, int Downloads, string DownloadsText);

public sealed record DownloadSnapshot(
    DateOnly Date,
    DateTime CapturedAt,
    int Total,
    IReadOnlyList<ReleaseSnapshot> Releases);

public sealed record ReleaseSnapshot(
    string Tag,
    int Total,
    IReadOnlyList<AssetSnapshot> Assets);

public sealed record AssetSnapshot(
    string Name,
    int Downloads,
    string Variant);
