using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace My_Fancy_Fences;

public static class ShortcutLibraryService
{
    private static readonly HashSet<string> ShortcutExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".lnk", ".url", ".website" };

    public static string AppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "My Fancy Fences");

    public static string ShortcutsDirectory { get; } = Path.Combine(AppDataDirectory, "Shortcuts");

    public static string IconsDirectory { get; } = Path.Combine(AppDataDirectory, "Icons");

    public static string LayoutsFilePath { get; } = Path.Combine(AppDataDirectory, "layouts.json");

    public static string ShortcutMetadataFilePath { get; } = Path.Combine(AppDataDirectory, "shortcuts.json");

    public static string GetIconPath(string shortcutId)
    {
        Directory.CreateDirectory(IconsDirectory);
        return Path.Combine(IconsDirectory, $"{shortcutId}.png");
    }

    public static string GetShortcutPath(string shortcutId)
    {
        Directory.CreateDirectory(ShortcutsDirectory);
        return Directory
            .EnumerateFiles(ShortcutsDirectory, $"{shortcutId}.*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault() ?? Path.Combine(ShortcutsDirectory, $"{shortcutId}.lnk");
    }

    public static bool ShortcutExists(string shortcutId)
    {
        try
        {
            return Directory.Exists(ShortcutsDirectory) &&
                   Directory.EnumerateFiles(ShortcutsDirectory, $"{shortcutId}.*", SearchOption.TopDirectoryOnly).Any();
        }
        catch
        {
            return false;
        }
    }

    public static List<string> AddShortcuts(IEnumerable<string> sourcePaths, out List<string> failures) =>
        AddShortcuts(sourcePaths, out failures, out _);

    /// <summary>Same as above; <paramref name="addedSources"/> lists the source paths that were added successfully.</summary>
    public static List<string> AddShortcuts(
        IEnumerable<string> sourcePaths,
        out List<string> failures,
        out List<string> addedSources)
    {
        Directory.CreateDirectory(ShortcutsDirectory);
        var shortcutIds = new List<string>();
        failures = [];
        addedSources = [];

        foreach (var sourcePath in sourcePaths)
        {
            try
            {
                var shortcutId = GetStableShortcutId(sourcePath);
                if (!ShortcutExists(shortcutId))
                {
                    if (ShortcutExtensions.Contains(Path.GetExtension(sourcePath)) && File.Exists(sourcePath))
                    {
                        var extension = Path.GetExtension(sourcePath);
                        File.Copy(sourcePath, Path.Combine(ShortcutsDirectory, $"{shortcutId}{extension}"), overwrite: false);
                    }
                    else
                    {
                        CreateWindowsShortcut(sourcePath, Path.Combine(ShortcutsDirectory, $"{shortcutId}.lnk"));
                    }
                }

                SaveShortcutDisplayName(shortcutId, GetDisplayName(sourcePath));
                shortcutIds.Add(shortcutId);
                addedSources.Add(sourcePath);
            }
            catch
            {
                failures.Add(Path.GetFileName(
                    sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
            }
        }

        return shortcutIds;
    }

    /// <summary>
    /// Puts a copy of the library item on the current user's desktop, named after its visible name.
    /// Existing desktop items are never overwritten: "Name.lnk" becomes "Name (1).lnk", "Name (2).lnk", ...
    /// </summary>
    public static string CopyToDesktop(string sourcePath, string displayName)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktop) || !Directory.Exists(desktop))
            throw new DirectoryNotFoundException("The desktop folder could not be found.");

        var isFolder = Directory.Exists(sourcePath);
        if (!isFolder && !File.Exists(sourcePath))
            throw new FileNotFoundException("The source item no longer exists.", sourcePath);

        var invalid = Path.GetInvalidFileNameChars();
        var baseName = new string(displayName.Select(character => invalid.Contains(character) ? '_' : character).ToArray())
            .Trim()
            .TrimEnd('.');
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "Shortcut";

        var extension = isFolder ? ".lnk" : Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(extension))
            extension = ".lnk";

        var destination = Path.Combine(desktop, baseName + extension);
        for (var suffix = 1; File.Exists(destination) || Directory.Exists(destination); suffix++)
            destination = Path.Combine(desktop, $"{baseName} ({suffix}){extension}");

        if (isFolder)
            CreateWindowsShortcut(sourcePath, destination);
        else
            File.Copy(sourcePath, destination, overwrite: false);

        return destination;
    }

    public static List<string> ImportFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return [];

        try
        {
            var entries = Directory
                .EnumerateFileSystemEntries(folder, "*", SearchOption.TopDirectoryOnly)
                .Where(path => File.Exists(path) || Directory.Exists(path));

            return AddShortcuts(entries, out _)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public static void DeleteUnusedShortcuts(IEnumerable<string> usedShortcutIds)
    {
        var used = new HashSet<string>(usedShortcutIds, StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(ShortcutsDirectory))
            return;

        foreach (var filePath in Directory.EnumerateFiles(ShortcutsDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            var shortcutId = Path.GetFileNameWithoutExtension(filePath);
            if (!used.Contains(shortcutId))
                File.Delete(filePath);
        }
    }

    public static string GetShortcutDisplayName(string shortcutId, string shortcutPath)
    {
        var metadata = LoadShortcutMetadata();
        if (metadata.TryGetValue(shortcutId, out var savedName) &&
            !string.IsNullOrWhiteSpace(savedName))
        {
            return savedName;
        }

        var inferredName = InferDisplayName(shortcutId, shortcutPath);
        if (!string.IsNullOrWhiteSpace(inferredName))
            SaveShortcutDisplayName(shortcutId, inferredName);

        return inferredName;
    }

    private static string GetStableShortcutId(string sourcePath)
    {
        var identity = ResolveShortcutIdentity(sourcePath);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(identity.ToLowerInvariant()));
        return Convert.ToHexString(bytes)[..20].ToLowerInvariant();
    }

    private static string ResolveShortcutIdentity(string sourcePath)
    {
        try
        {
            if (ShortcutExtensions.Contains(Path.GetExtension(sourcePath)) && File.Exists(sourcePath))
            {
                var fileInfo = new FileInfo(sourcePath);
                return $"{Path.GetFileNameWithoutExtension(sourcePath)}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}";
            }

            return Path.GetFullPath(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        catch
        {
            return sourcePath;
        }
    }

    private static string GetDisplayName(string sourcePath)
    {
        var trimmed = sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Directory.Exists(trimmed)
            ? new DirectoryInfo(trimmed).Name
            : Path.GetFileNameWithoutExtension(trimmed);
        return string.IsNullOrWhiteSpace(name) ? "Shortcut" : name;
    }

    private static string InferDisplayName(string shortcutId, string shortcutPath)
    {
        var fileName = Path.GetFileNameWithoutExtension(shortcutPath);
        if (!LooksLikeShortcutId(fileName))
            return string.IsNullOrWhiteSpace(fileName) ? "Shortcut" : fileName;

        if (string.Equals(Path.GetExtension(shortcutPath), ".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var targetPath = TryReadShortcutTarget(shortcutPath);
            if (!string.IsNullOrWhiteSpace(targetPath))
            {
                var versionName = TryReadVersionName(targetPath);
                if (!string.IsNullOrWhiteSpace(versionName))
                    return versionName;

                return GetDisplayName(targetPath);
            }
        }

        if (string.Equals(Path.GetExtension(shortcutPath), ".url", StringComparison.OrdinalIgnoreCase))
        {
            var urlName = TryReadUrlHost(shortcutPath);
            if (!string.IsNullOrWhiteSpace(urlName))
                return urlName;
        }

        return string.IsNullOrWhiteSpace(shortcutId) ? "Shortcut" : "Shortcut";
    }

    private static bool LooksLikeShortcutId(string value) =>
        value.Length == 20 && value.All(Uri.IsHexDigit);

    private static string? TryReadShortcutTarget(string shortcutPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
                return null;

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            return shortcut.TargetPath as string;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadVersionName(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            var version = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
            return FirstUsefulName(version.FileDescription, version.ProductName);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadUrlHost(string shortcutPath)
    {
        try
        {
            var urlLine = File.ReadLines(shortcutPath)
                .FirstOrDefault(line => line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase));
            if (urlLine is null || !Uri.TryCreate(urlLine[4..], UriKind.Absolute, out var uri))
                return null;

            return string.IsNullOrWhiteSpace(uri.Host) ? null : uri.Host;
        }
        catch
        {
            return null;
        }
    }

    private static string? FirstUsefulName(params string?[] names) =>
        names
            .Select(name => name?.Trim())
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name) && !LooksLikeShortcutId(name));

    private static Dictionary<string, string> LoadShortcutMetadata()
    {
        try
        {
            if (!File.Exists(ShortcutMetadataFilePath))
                return new(StringComparer.OrdinalIgnoreCase);

            return JsonSerializer.Deserialize<Dictionary<string, string>>(
                       File.ReadAllText(ShortcutMetadataFilePath))
                   ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void SaveShortcutDisplayName(string shortcutId, string displayName)
    {
        if (string.IsNullOrWhiteSpace(shortcutId) || string.IsNullOrWhiteSpace(displayName))
            return;

        try
        {
            Directory.CreateDirectory(AppDataDirectory);
            var metadata = LoadShortcutMetadata();
            metadata[shortcutId] = displayName.Trim();
            File.WriteAllText(
                ShortcutMetadataFilePath,
                JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Missing metadata should not block adding or displaying shortcuts.
        }
    }

    private static void CreateWindowsShortcut(string sourcePath, string shortcutPath)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell is not available.");
        dynamic shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Could not create WScript.Shell.");
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = sourcePath;

        var workingDirectory = Directory.Exists(sourcePath)
            ? sourcePath
            : Path.GetDirectoryName(sourcePath);
        if (!string.IsNullOrWhiteSpace(workingDirectory))
            shortcut.WorkingDirectory = workingDirectory;

        if (File.Exists(sourcePath))
            shortcut.IconLocation = sourcePath;

        shortcut.Save();
    }
}
