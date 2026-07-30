using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace My_Fancy_Fences;

public static class ConfigurationArchiveService
{
    private const int ArchiveFormatVersion = 1;
    private static readonly HashSet<string> ShortcutExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".lnk", ".url", ".website" };
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static Task<ConfigurationArchiveResult> ExportAsync(
        string settingsPath,
        string archivePath,
        bool includeShortcuts)
    {
        return Task.Run(() => Export(settingsPath, archivePath, includeShortcuts));
    }

    public static Task<ConfigurationArchiveResult> ImportAsync(
        string settingsPath,
        string archivePath,
        bool importShortcuts,
        string? shortcutsDestination)
    {
        return Task.Run(() => Import(
            settingsPath,
            archivePath,
            importShortcuts,
            shortcutsDestination));
    }

    private static ConfigurationArchiveResult Export(
        string settingsPath,
        string archivePath,
        bool includeShortcuts)
    {
        if (!File.Exists(settingsPath))
            throw new FileNotFoundException("Nie znaleziono zapisanej konfiguracji.", settingsPath);

        var settingsJson = File.ReadAllText(settingsPath);
        var settings = JsonNode.Parse(settingsJson)?.AsObject()
            ?? throw new InvalidDataException("Plik konfiguracji jest nieprawidłowy.");
        var panels = ReadPanels(settings);

        var destinationDirectory = Path.GetDirectoryName(archivePath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
            Directory.CreateDirectory(destinationDirectory);
        if (File.Exists(archivePath))
            File.Delete(archivePath);

        var manifestPanels = new List<ArchivePanel>();
        var shortcutCount = 0;
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        WriteTextEntry(archive, "config/settings.json", settingsJson);

        var languagePath = Path.Combine(Path.GetDirectoryName(settingsPath)!, "language.txt");
        if (File.Exists(languagePath))
            archive.CreateEntryFromFile(languagePath, "config/language.txt", CompressionLevel.Optimal);

        foreach (var panel in panels)
        {
            var archiveFolder = $"shortcuts/panel-{panel.Index:D3}/";
            manifestPanels.Add(new ArchivePanel(panel.Index, panel.Title, archiveFolder));
            if (!includeShortcuts || !Directory.Exists(panel.SourceFolder))
                continue;

            foreach (var filePath in Directory.EnumerateFiles(panel.SourceFolder, "*", SearchOption.TopDirectoryOnly)
                         .Where(path => ShortcutExtensions.Contains(Path.GetExtension(path))))
            {
                archive.CreateEntryFromFile(
                    filePath,
                    archiveFolder + Path.GetFileName(filePath),
                    CompressionLevel.Optimal);
                shortcutCount++;
            }
        }

        var manifest = new ArchiveManifest(
            ArchiveFormatVersion,
            DateTimeOffset.UtcNow,
            includeShortcuts,
            manifestPanels);
        WriteTextEntry(
            archive,
            "manifest.json",
            JsonSerializer.Serialize(manifest, JsonOptions));

        return new ConfigurationArchiveResult(archivePath, panels.Count, shortcutCount);
    }

    private static ConfigurationArchiveResult Import(
        string settingsPath,
        string archivePath,
        bool importShortcuts,
        string? shortcutsDestination)
    {
        if (!File.Exists(archivePath))
            throw new FileNotFoundException("Nie znaleziono wybranego archiwum.", archivePath);
        using var archive = ZipFile.OpenRead(archivePath);
        var manifest = ReadJsonEntry<ArchiveManifest>(archive, "manifest.json")
            ?? throw new InvalidDataException("Archiwum nie zawiera prawidłowego manifestu.");
        if (manifest.FormatVersion > ArchiveFormatVersion)
            throw new InvalidDataException("Archiwum pochodzi z nowszej wersji aplikacji.");

        var settingsEntry = archive.GetEntry("config/settings.json")
            ?? throw new InvalidDataException("Archiwum nie zawiera konfiguracji.");
        string settingsJson;
        using (var reader = new StreamReader(settingsEntry.Open()))
            settingsJson = reader.ReadToEnd();
        var settings = JsonNode.Parse(settingsJson)?.AsObject()
            ?? throw new InvalidDataException("Konfiguracja w archiwum jest nieprawidłowa.");

        var importedShortcutCount = 0;
        if (importShortcuts && manifest.IncludesShortcuts)
        {
            foreach (var panel in manifest.Panels)
            {
                var panelFolder = PanelStorageService.GetPanelFolder(panel.Index, panel.Title);
                Directory.CreateDirectory(panelFolder);
                foreach (var entry in archive.Entries.Where(entry =>
                             entry.FullName.StartsWith(panel.ArchiveFolder, StringComparison.OrdinalIgnoreCase) &&
                             !string.IsNullOrWhiteSpace(entry.Name)))
                {
                    if (!ShortcutExtensions.Contains(Path.GetExtension(entry.Name)))
                        continue;

                    var targetPath = Path.Combine(panelFolder, Path.GetFileName(entry.Name));
                    entry.ExtractToFile(targetPath, overwrite: true);
                    importedShortcutCount++;
                }

                SetPanelSourceFolder(settings, panel.Index, panelFolder);
            }
        }

        var settingsDirectory = Path.GetDirectoryName(settingsPath)!;
        Directory.CreateDirectory(settingsDirectory);
        if (File.Exists(settingsPath))
        {
            var desktopDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            Directory.CreateDirectory(desktopDirectory);
            var backupPath = Path.Combine(
                desktopDirectory,
                $"My-Fancy-Fences-backup-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.json");
            File.Copy(settingsPath, backupPath, overwrite: false);
        }
        var temporarySettingsPath = $"{settingsPath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporarySettingsPath, settings.ToJsonString(JsonOptions));
        File.Move(temporarySettingsPath, settingsPath, overwrite: true);

        var languageEntry = archive.GetEntry("config/language.txt");
        if (languageEntry is not null)
            languageEntry.ExtractToFile(Path.Combine(settingsDirectory, "language.txt"), overwrite: true);

        return new ConfigurationArchiveResult(
            archivePath,
            manifest.Panels.Count,
            importedShortcutCount);
    }

    private static List<PanelConfiguration> ReadPanels(JsonObject settings)
    {
        var panels = new List<PanelConfiguration>
        {
            new(
                0,
                settings["Title"]?.GetValue<string>() ?? "Panel główny",
                settings["SourceFolder"]?.GetValue<string>() ?? string.Empty)
        };

        if (settings["AdditionalPanels"] is JsonArray additionalPanels)
        {
            for (var index = 0; index < additionalPanels.Count; index++)
            {
                if (additionalPanels[index] is not JsonObject panel)
                    continue;
                panels.Add(new PanelConfiguration(
                    index + 1,
                    panel["Title"]?.GetValue<string>() ?? $"Panel {index + 2}",
                    panel["SourceFolder"]?.GetValue<string>() ?? string.Empty));
            }
        }

        return panels;
    }

    private static void SetPanelSourceFolder(JsonObject settings, int panelIndex, string folder)
    {
        if (panelIndex == 0)
        {
            settings["SourceFolder"] = folder;
            return;
        }

        if (settings["AdditionalPanels"] is JsonArray panels &&
            panelIndex - 1 < panels.Count &&
            panels[panelIndex - 1] is JsonObject panel)
        {
            panel["SourceFolder"] = folder;
        }
    }

    private static string CreateUniqueDirectory(string parent, string requestedName)
    {
        var baseName = string.IsNullOrWhiteSpace(requestedName) ? "Panel" : requestedName;
        var path = Path.Combine(parent, baseName);
        for (var suffix = 2; Directory.Exists(path); suffix++)
            path = Path.Combine(parent, $"{baseName} ({suffix})");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character =>
            invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "Panel" : sanitized;
    }

    private static void WriteTextEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static T? ReadJsonEntry<T>(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path);
        if (entry is null)
            return default;
        using var reader = new StreamReader(entry.Open());
        return JsonSerializer.Deserialize<T>(reader.ReadToEnd());
    }

    private sealed record PanelConfiguration(int Index, string Title, string SourceFolder);
    private sealed record ArchivePanel(int Index, string Title, string ArchiveFolder);
    private sealed record ArchiveManifest(
        int FormatVersion,
        DateTimeOffset CreatedAt,
        bool IncludesShortcuts,
        List<ArchivePanel> Panels);
}

public sealed record ConfigurationArchiveResult(
    string ArchivePath,
    int PanelCount,
    int ShortcutCount);
