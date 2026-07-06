using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace My_Fancy_Fences;

public static class UpdateService
{
    private static readonly HttpClient Client = CreateClient();
    private static readonly object Sync = new();
    private static Task<UpdateCheckResult>? _cachedCheck;

    public static Version CurrentVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 1, 0);

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
            using var response = await Client.GetAsync(
                "repos/infitis-studio/my-fancy-fences/releases/latest");
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);
            var root = document.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
            var releaseUrl = root.GetProperty("html_url").GetString();
            var assets = root.TryGetProperty("assets", out var assetsElement)
                ? assetsElement.EnumerateArray()
                    .Select(asset => new UpdateAsset(
                        asset.GetProperty("name").GetString() ?? string.Empty,
                        asset.GetProperty("browser_download_url").GetString() ?? string.Empty,
                        asset.TryGetProperty("size", out var size) ? size.GetInt64() : 0))
                    .Where(asset => !string.IsNullOrWhiteSpace(asset.Name) &&
                                    !string.IsNullOrWhiteSpace(asset.DownloadUrl))
                    .ToArray()
                : [];

            if (!Version.TryParse(tag.Trim().TrimStart('v', 'V'), out var latestVersion))
                return new UpdateCheckResult(false, tag, null, releaseUrl, false, assets);

            return new UpdateCheckResult(
                true,
                tag,
                latestVersion,
                releaseUrl,
                latestVersion > CurrentVersion,
                assets);
        }
        catch
        {
            return new UpdateCheckResult(false, string.Empty, null, null, false, []);
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri("https://api.github.com/"),
            Timeout = TimeSpan.FromSeconds(10)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("My-Fancy-Fences-Update-Checker");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
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

public sealed record UpdateAsset(string Name, string DownloadUrl, long Size);
