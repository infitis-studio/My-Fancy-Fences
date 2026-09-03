using System.Diagnostics;
using System.Net.Http;
using System.Net;
using System.Reflection;

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
            using var response = await Client.GetAsync(
                "https://github.com/infitis-studio/My-Fancy-Fences/releases/latest",
                HttpCompletionOption.ResponseHeadersRead);
            if (response.StatusCode is not (HttpStatusCode.Found or
                HttpStatusCode.MovedPermanently or
                HttpStatusCode.TemporaryRedirect or
                HttpStatusCode.PermanentRedirect))
            {
                response.EnsureSuccessStatusCode();
            }

            var releaseUri = response.Headers.Location;
            if (releaseUri is null)
                return new UpdateCheckResult(false, string.Empty, null, null, false, []);
            if (!releaseUri.IsAbsoluteUri)
                releaseUri = new Uri(new Uri("https://github.com"), releaseUri);

            var tag = releaseUri.Segments.LastOrDefault()?.Trim('/') ?? string.Empty;
            var releaseUrl = releaseUri.AbsoluteUri;
            var downloadBase =
                $"https://github.com/infitis-studio/My-Fancy-Fences/releases/download/{tag}";
            var assets = new[]
            {
                CreateAsset(tag, "REQUIRES-NET10", downloadBase),
                CreateAsset(tag, "WITH-NET10", downloadBase)
            };

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

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false
        })
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
