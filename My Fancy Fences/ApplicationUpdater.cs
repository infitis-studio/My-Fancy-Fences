using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace My_Fancy_Fences;

public static class ApplicationUpdater
{
    private const long BundledRuntimeSizeThreshold = 30L * 1024 * 1024;
    private static readonly HttpClient Client = CreateClient();

    public static UpdatePackageKind DetectCurrentPackageKind()
    {
        var executablePath = GetCurrentExecutablePath();
        var directory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory;
        var executableSize = File.Exists(executablePath)
            ? new FileInfo(executablePath).Length
            : 0;

        return executableSize >= BundledRuntimeSizeThreshold ||
               File.Exists(Path.Combine(directory, "coreclr.dll"))
            ? UpdatePackageKind.WithNet10
            : UpdatePackageKind.RequiresNet10;
    }

    public static async Task<UpdatePackageKind> PrepareUpdateAsync(
        UpdateCheckResult update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var packageKind = DetectCurrentPackageKind();
        var marker = packageKind == UpdatePackageKind.WithNet10
            ? "WITH-NET10"
            : "REQUIRES-NET10";
        var asset = update.Assets.FirstOrDefault(candidate =>
            candidate.Name.Contains(marker, StringComparison.OrdinalIgnoreCase) &&
            candidate.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        if (asset is null)
            throw new InvalidOperationException($"{LocalizationService.T("Wydanie nie zawiera pliku")} {marker}.");

        var downloadUri = new Uri(asset.DownloadUrl);
        if (!downloadUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !downloadUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(LocalizationService.T("Nieprawidłowy adres pliku aktualizacji."));
        }

        var currentExecutable = GetCurrentExecutablePath();
        EnsureTargetDirectoryIsWritable(currentExecutable);

        var updateDirectory = Path.Combine(
            Path.GetTempPath(),
            "MyFancyFencesUpdate",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(updateDirectory);
        var downloadedExecutable = Path.Combine(updateDirectory, asset.Name);

        using var response = await Client.GetAsync(
            downloadUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? asset.Size;
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var target = File.Create(downloadedExecutable))
        {
            var buffer = new byte[128 * 1024];
            long downloadedBytes = 0;
            int bytesRead;
            while ((bytesRead = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                downloadedBytes += bytesRead;
                if (totalBytes > 0)
                    progress?.Report(Math.Clamp((double)downloadedBytes / totalBytes, 0, 1));
            }
        }

        ValidateDownloadedExecutable(downloadedExecutable, asset.Size);
        StartReplacementHelper(currentExecutable, downloadedExecutable);
        return packageKind;
    }

    public static void RestartAfterCurrentProcessExits()
    {
        var executablePath = GetCurrentExecutablePath();
        var scriptDirectory = Path.Combine(
            Path.GetTempPath(),
            "MyFancyFencesRestart",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scriptDirectory);
        var scriptPath = Path.Combine(scriptDirectory, "restart.ps1");
        var script = $$"""
            Wait-Process -Id {{Environment.ProcessId}} -ErrorAction SilentlyContinue
            Start-Sleep -Milliseconds 350
            Start-Process -FilePath '{{EscapePowerShell(executablePath)}}'
            Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
            """;
        File.WriteAllText(scriptPath, script);
        StartPowerShellScript(scriptPath);
    }

    private static void EnsureTargetDirectoryIsWritable(string executablePath)
    {
        var directory = Path.GetDirectoryName(executablePath)
            ?? throw new InvalidOperationException(LocalizationService.T("Nie można ustalić folderu aplikacji."));
        var probePath = Path.Combine(directory, $".mff-update-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(probePath, []);
        }
        catch (Exception exception)
        {
            throw new UnauthorizedAccessException(
                LocalizationService.T("Brak uprawnień do podmiany pliku aplikacji w obecnym folderze."),
                exception);
        }
        finally
        {
            if (File.Exists(probePath))
                File.Delete(probePath);
        }
    }

    private static void ValidateDownloadedExecutable(string path, long expectedSize)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length < 1024 * 1024 ||
            (expectedSize > 0 && file.Length != expectedSize))
        {
            throw new InvalidDataException(LocalizationService.T("Pobrany plik aktualizacji jest niekompletny."));
        }

        using var stream = file.OpenRead();
        if (stream.ReadByte() != 'M' || stream.ReadByte() != 'Z')
            throw new InvalidDataException(LocalizationService.T("Pobrany plik nie jest prawidłową aplikacją Windows."));
    }

    private static void StartReplacementHelper(string targetPath, string downloadedPath)
    {
        var scriptPath = Path.Combine(
            Path.GetDirectoryName(downloadedPath)!,
            "install-update.ps1");
        var backupPath = $"{targetPath}.previous";
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $target = '{{EscapePowerShell(targetPath)}}'
            $download = '{{EscapePowerShell(downloadedPath)}}'
            $backup = '{{EscapePowerShell(backupPath)}}'
            try {
                Wait-Process -Id {{Environment.ProcessId}} -ErrorAction SilentlyContinue
                Start-Sleep -Milliseconds 350
                if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Force }
                if (Test-Path -LiteralPath $target) { Move-Item -LiteralPath $target -Destination $backup -Force }
                Move-Item -LiteralPath $download -Destination $target -Force
                Start-Process -FilePath $target
                Start-Sleep -Seconds 1
                if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Force }
            }
            catch {
                if (-not (Test-Path -LiteralPath $target) -and (Test-Path -LiteralPath $backup)) {
                    Move-Item -LiteralPath $backup -Destination $target -Force
                }
                if (Test-Path -LiteralPath $target) { Start-Process -FilePath $target }
            }
            Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
            """;
        File.WriteAllText(scriptPath, script);

        StartPowerShellScript(scriptPath);
    }

    private static void StartPowerShellScript(string scriptPath)
    {
        var powershellPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        Process.Start(new ProcessStartInfo
        {
            FileName = powershellPath,
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    private static string GetCurrentExecutablePath() =>
        Environment.ProcessPath
        ?? Process.GetCurrentProcess().MainModule?.FileName
        ?? Path.Combine(AppContext.BaseDirectory, "My Fancy Fences.exe");

    private static string EscapePowerShell(string value) => value.Replace("'", "''");

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("My-Fancy-Fences-Updater");
        return client;
    }
}

public enum UpdatePackageKind
{
    WithNet10,
    RequiresNet10
}
