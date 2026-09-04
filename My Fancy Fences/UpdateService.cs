using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace My_Fancy_Fences;

public static class UpdateService
{
    private static readonly HttpClient Client = CreateClient();
    private static readonly object Sync = new();
    private static Task<UpdateCheckResult>? _cachedCheck;

    public static Version CurrentVersion { get; } = ResolveCurrentVersion();

    public static string FormatVersion(Version version) =>
        $"v{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";

    public static Task<UpdateCheckResult> CheckAsync(bool force = false)
    {
        lock (Sync)
        {
            if (force || _cachedCheck is null)
                _cachedCheck = CheckCoreAsync();

            return _cachedCheck;
        }
    }

    private static async Task<UpdateCheckResult> CheckCoreAsync()
    {
        try
        {
            await using var stream = await Client.GetStreamAsync(
                "https://api.github.com/repos/infitis-studio/My-Fancy-Fences/releases?per_page=30");
            var releases = await JsonSerializer.DeserializeAsync<List<GitHubRelease>>(
                stream,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            var latestRelease = releases?
                .Where(release => !release.Draft &&
                                  TryParseVersion(release.TagName, out _))
                .Select(release => new
                {
                    Release = release,
                    Version = ParseVersionOrDefault(release.TagName)
                })
                .OrderByDescending(item => item.Version)
                .FirstOrDefault();

            if (latestRelease is null)
                return new UpdateCheckResult(false, string.Empty, null, null, false, []);

            var tag = latestRelease.Release.TagName;
            var releaseUrl = latestRelease.Release.HtmlUrl;
            var downloadBase =
                $"https://github.com/infitis-studio/My-Fancy-Fences/releases/download/{tag}";
            var releaseAssets = latestRelease.Release.Assets
                .Where(asset => !string.IsNullOrWhiteSpace(asset.Name) &&
                                !string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
                .Select(asset => new UpdateAsset(
                    asset.Id,
                    asset.Name,
                    asset.BrowserDownloadUrl,
                    asset.Size))
                .ToList();
            var assets = releaseAssets.Count > 0
                ? releaseAssets
                : new List<UpdateAsset>
            {
                CreateAsset(tag, "REQUIRES-NET10", downloadBase),
                CreateAsset(tag, "WITH-NET10", downloadBase)
            };

            return new UpdateCheckResult(
                true,
                tag,
                latestRelease.Version,
                releaseUrl,
                latestRelease.Version > CurrentVersion,
                assets);
        }
        catch
        {
            return new UpdateCheckResult(false, string.Empty, null, null, false, []);
        }
    }

    private static UpdateAsset CreateAsset(string tag, string variant, string downloadBase)
    {
        var name = $"My-Fancy-Fences-{tag}-{variant}-win-x64.exe";
        return new UpdateAsset(0, name, $"{downloadBase}/{name}", 0);
    }

    private static Version ResolveCurrentVersion()
    {
        var executablePath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(executablePath);
            if (TryParseVersion(versionInfo.ProductVersion, out var productVersion))
                return productVersion;
            if (TryParseVersion(versionInfo.FileVersion, out var fileVersion))
                return fileVersion;
        }

        var informationalVersion = Assembly
            .GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (TryParseVersion(informationalVersion, out var assemblyInformationalVersion))
            return assemblyInformationalVersion;

        return Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 1, 0);
    }

    private static bool TryParseVersion(string? value, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim().TrimStart('v', 'V');
        var metadataIndex = normalized.IndexOfAny(['+', '-', ' ']);
        if (metadataIndex >= 0)
            normalized = normalized[..metadataIndex];

        return Version.TryParse(normalized, out version!);
    }

    private static Version ParseVersionOrDefault(string? value) =>
        TryParseVersion(value, out var version)
            ? version
            : new Version(0, 0, 0);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("My-Fancy-Fences-Update-Checker");
        return client;
    }
}

public sealed record UpdateCheckResult(
    bool Success,
    string LatestTag,
    Version? LatestVersion,
    string? ReleaseUrl,
    bool IsUpdateAvailable,
    IReadOnlyList<UpdateAsset> Assets);

public sealed record UpdateAsset(long Id, string Name, string DownloadUrl, long Size);

internal sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("draft")]
    public bool Draft { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubReleaseAsset> Assets { get; set; } = [];
}

internal sealed class GitHubReleaseAsset
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }
}
