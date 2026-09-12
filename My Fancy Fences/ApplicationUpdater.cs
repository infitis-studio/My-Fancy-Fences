using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace My_Fancy_Fences;

public static class ApplicationUpdater
{
    private const long BundledRuntimeSizeThreshold = 30L * 1024 * 1024;
    private static readonly HttpClient Client = CreateClient();
    private static readonly string UpdateRootDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "My Fancy Fences",
        "Updates");
    private static readonly string PendingUpdatePath = Path.Combine(UpdateRootDirectory, "pending-update.json");
    private static readonly string UpdateLogPath = Path.Combine(UpdateRootDirectory, "update.log");

    public static bool TryResumePendingUpdateOnStartup()
    {
        try
        {
            if (!File.Exists(PendingUpdatePath))
                return false;

            var pending = JsonSerializer.Deserialize<PendingUpdate>(
                File.ReadAllText(PendingUpdatePath));
            if (pending is null ||
                string.IsNullOrWhiteSpace(pending.TargetPath) ||
                string.IsNullOrWhiteSpace(pending.DownloadedPath) ||
                !File.Exists(pending.DownloadedPath))
            {
                SafeDelete(PendingUpdatePath);
                return false;
            }

            AppendLog("Resuming pending update on startup.");
            SafeDelete(PendingUpdatePath);
            StartReplacementHelper(pending.TargetPath, pending.DownloadedPath, pending.Version);
            return true;
        }
        catch (Exception exception)
        {
            AppendLog($"Failed to resume pending update: {exception}");
            return false;
        }
    }

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
        var preferredMarker = "WITH-NET10";
        var fallbackMarker = packageKind == UpdatePackageKind.WithNet10
            ? "REQUIRES-NET10"
            : "WITH-NET10";
        var asset = FindAsset(update, preferredMarker) ?? FindAsset(update, fallbackMarker);
        if (asset is null)
            throw new InvalidOperationException($"{LocalizationService.T("Wydanie nie zawiera pliku")} {preferredMarker}.");

        var downloadUri = new Uri(asset.DownloadUrl);
        if (!downloadUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !downloadUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(LocalizationService.T("Nieprawidłowy adres pliku aktualizacji."));
        }

        var currentExecutable = GetCurrentExecutablePath();
        EnsureTargetDirectoryIsWritable(currentExecutable);

        Directory.CreateDirectory(UpdateRootDirectory);
        SafeDelete(PendingUpdatePath);
        var updateDirectory = Path.Combine(UpdateRootDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(updateDirectory);
        var downloadedExecutable = Path.Combine(updateDirectory, asset.Name);
        AppendLog($"Preparing update {update.LatestTag}. Current executable: {currentExecutable}");
        AppendLog($"Selected asset: {asset.Name} ({asset.Size} bytes)");

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
        var pendingUpdate = new PendingUpdate(
            currentExecutable,
            downloadedExecutable,
            update.LatestTag,
            DateTimeOffset.Now);
        File.WriteAllText(
            PendingUpdatePath,
            JsonSerializer.Serialize(pendingUpdate, new JsonSerializerOptions { WriteIndented = true }));
        StartReplacementHelper(currentExecutable, downloadedExecutable, update.LatestTag);
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

    private static UpdateAsset? FindAsset(UpdateCheckResult update, string marker) =>
        update.Assets.FirstOrDefault(candidate =>
            candidate.Name.Contains(marker, StringComparison.OrdinalIgnoreCase) &&
            candidate.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

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

    private static void StartReplacementHelper(string targetPath, string downloadedPath, string? version = null)
    {
        var scriptPath = Path.Combine(
            Path.GetDirectoryName(downloadedPath)!,
            "install-update.ps1");
        var backupPath = $"{targetPath}.previous";
        var logPath = UpdateLogPath;
        var pendingPath = PendingUpdatePath;
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $target = '{{EscapePowerShell(targetPath)}}'
            $download = '{{EscapePowerShell(downloadedPath)}}'
            $backup = '{{EscapePowerShell(backupPath)}}'
            $pending = '{{EscapePowerShell(pendingPath)}}'
            $log = '{{EscapePowerShell(logPath)}}'
            function Write-UpdateLog([string]$message) {
                $directory = [System.IO.Path]::GetDirectoryName($log)
                if (-not (Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
                Add-Content -LiteralPath $log -Value "[$([DateTime]::Now.ToString('yyyy-MM-dd HH:mm:ss.fff'))] $message"
            }
            function Invoke-WithRetry([scriptblock]$operation, [string]$name) {
                for ($attempt = 1; $attempt -le 30; $attempt++) {
                    try {
                        & $operation
                        Write-UpdateLog "$name succeeded on attempt $attempt"
                        return
                    }
                    catch {
                        Write-UpdateLog "$name attempt $attempt failed: $($_.Exception.Message)"
                        Start-Sleep -Milliseconds 300
                    }
                }
                throw "$name failed after retries"
            }
            $targetDirectory = [System.IO.Path]::GetDirectoryName($target)
            $applicationBaseName = [System.IO.Path]::GetFileNameWithoutExtension($target)
            $sidecarPatterns = @(
                "$applicationBaseName.dll",
                "$applicationBaseName.deps.json",
                "$applicationBaseName.runtimeconfig.json",
                "$applicationBaseName.pdb"
            )
            try {
                Write-UpdateLog "Installing update {{EscapePowerShell(version ?? string.Empty)}}"
                Write-UpdateLog "Waiting for process {{Environment.ProcessId}}"
                Wait-Process -Id {{Environment.ProcessId}} -ErrorAction SilentlyContinue
                Start-Sleep -Milliseconds 800
                if (Test-Path -LiteralPath $backup) {
                    Invoke-WithRetry { Remove-Item -LiteralPath $backup -Force } "Remove old backup"
                }
                if (Test-Path -LiteralPath $target) {
                    Invoke-WithRetry { Move-Item -LiteralPath $target -Destination $backup -Force } "Move current executable to backup"
                }
                foreach ($pattern in $sidecarPatterns) {
                    $sidecar = Join-Path -Path $targetDirectory -ChildPath $pattern
                    if (Test-Path -LiteralPath $sidecar) {
                        Remove-Item -LiteralPath $sidecar -Force -ErrorAction SilentlyContinue
                    }
                }
                Invoke-WithRetry { Move-Item -LiteralPath $download -Destination $target -Force } "Move downloaded executable into place"
                if (Test-Path -LiteralPath $pending) { Remove-Item -LiteralPath $pending -Force -ErrorAction SilentlyContinue }
                Write-UpdateLog "Starting updated application: $target"
                Start-Process -FilePath $target
                Start-Sleep -Seconds 1
                if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Force }
            }
            catch {
                Write-UpdateLog "Update failed: $($_.Exception.Message)"
                if (-not (Test-Path -LiteralPath $target) -and (Test-Path -LiteralPath $backup)) {
                    Move-Item -LiteralPath $backup -Destination $target -Force
                }
                Write-UpdateLog "Starting fallback application: $target"
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
        AppendLog($"Starting update helper: {scriptPath}");
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = powershellPath,
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch (Exception exception)
        {
            AppendLog($"Failed to start update helper: {exception}");
            throw;
        }
    }

    private static string GetCurrentExecutablePath() =>
        Environment.ProcessPath
        ?? Process.GetCurrentProcess().MainModule?.FileName
        ?? Path.Combine(AppContext.BaseDirectory, "My Fancy Fences.exe");

    private static string EscapePowerShell(string value) => value.Replace("'", "''");

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static void AppendLog(string message)
    {
        try
        {
            Directory.CreateDirectory(UpdateRootDirectory);
            File.AppendAllText(
                UpdateLogPath,
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("My-Fancy-Fences-Updater");
        return client;
    }
}

public sealed record PendingUpdate(
    [property: JsonPropertyName("targetPath")] string TargetPath,
    [property: JsonPropertyName("downloadedPath")] string DownloadedPath,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt);

public enum UpdatePackageKind
{
    WithNet10,
    RequiresNet10
}
