using System.IO;

namespace My_Fancy_Fences;

public static class PanelStorageService
{
    private static readonly HashSet<string> ShortcutExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".lnk", ".url", ".website" };

    public static string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "My Fancy Fences",
        "Panels");

    public static string EnsurePanelFolder(string? currentFolder, int panelIndex, string? title)
    {
        if (!string.IsNullOrWhiteSpace(currentFolder) &&
            Directory.Exists(currentFolder) &&
            IsManagedPanelFolder(currentFolder))
        {
            return currentFolder;
        }

        var folder = GetPanelFolder(panelIndex, title);
        Directory.CreateDirectory(folder);

        if (!string.IsNullOrWhiteSpace(currentFolder) &&
            Directory.Exists(currentFolder) &&
            !IsManagedPanelFolder(currentFolder))
        {
            ImportExistingShortcuts(currentFolder, folder);
        }

        return folder;
    }

    public static string GetPanelFolder(int panelIndex, string? title)
    {
        var safeTitle = SanitizeFileName(string.IsNullOrWhiteSpace(title) ? "Panel" : title);
        return Path.Combine(RootDirectory, $"panel-{panelIndex:D3}-{safeTitle}");
    }

    public static string ClonePanelFolder(
        string sourceFolder,
        string layoutId,
        int panelIndex,
        string? title)
    {
        var safeTitle = SanitizeFileName(string.IsNullOrWhiteSpace(title) ? "Panel" : title);
        var layoutFolder = Path.Combine(RootDirectory, "layouts", $"layout-{layoutId}");
        Directory.CreateDirectory(layoutFolder);

        var destination = Path.Combine(layoutFolder, $"panel-{panelIndex:D3}-{safeTitle}");
        if (Directory.Exists(sourceFolder) &&
            string.Equals(
                Path.GetFullPath(sourceFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            return destination;
        }

        if (Directory.Exists(destination))
            Directory.Delete(destination, recursive: true);
        else if (File.Exists(destination))
            File.Delete(destination);

        Directory.CreateDirectory(destination);
        if (Directory.Exists(sourceFolder))
            CopyDirectory(sourceFolder, destination);

        return destination;
    }

    public static bool IsManagedPanelFolder(string path)
    {
        try
        {
            var root = Path.GetFullPath(RootDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static List<string> AddShortcuts(IEnumerable<string> sourcePaths, string panelFolder)
    {
        Directory.CreateDirectory(panelFolder);
        var failures = new List<string>();

        foreach (var sourcePath in sourcePaths)
        {
            try
            {
                if (ShortcutExtensions.Contains(Path.GetExtension(sourcePath)))
                {
                    var requestedName = Path.GetFileName(sourcePath);
                    var existingDestination = Path.Combine(panelFolder, requestedName);
                    if (File.Exists(existingDestination))
                        continue;

                    var destination = GetUniqueDestinationPath(panelFolder, requestedName);
                    File.Copy(sourcePath, destination, overwrite: false);
                }
                else
                {
                    CreateWindowsShortcut(sourcePath, panelFolder);
                }
            }
            catch
            {
                failures.Add(Path.GetFileName(
                    sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
            }
        }

        return failures;
    }

    private static void ImportExistingShortcuts(string sourceFolder, string panelFolder)
    {
        foreach (var shortcutPath in Directory.EnumerateFiles(sourceFolder, "*", SearchOption.TopDirectoryOnly)
                     .Where(path => ShortcutExtensions.Contains(Path.GetExtension(path))))
        {
            try
            {
                var destination = GetUniqueDestinationPath(panelFolder, Path.GetFileName(shortcutPath));
                if (!File.Exists(destination))
                    File.Copy(shortcutPath, destination, overwrite: false);
            }
            catch
            {
                // A single broken shortcut should not block panel migration.
            }
        }
    }

    private static void CreateWindowsShortcut(string sourcePath, string panelFolder)
    {
        var trimmed = sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        if (string.IsNullOrWhiteSpace(name))
            name = "Shortcut";

        var shortcutPath = GetUniqueDestinationPath(panelFolder, $"{Path.GetFileNameWithoutExtension(name)}.lnk");
        var preferredShortcutPath = Path.Combine(panelFolder, $"{Path.GetFileNameWithoutExtension(name)}.lnk");
        if (File.Exists(preferredShortcutPath))
            return;

        shortcutPath = preferredShortcutPath;
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

    private static string GetUniqueDestinationPath(string folder, string requestedName)
    {
        var extension = Path.GetExtension(requestedName);
        var baseName = Path.GetFileNameWithoutExtension(requestedName);
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "Shortcut";

        var destination = Path.Combine(folder, $"{baseName}{extension}");
        for (var suffix = 2; File.Exists(destination) || Directory.Exists(destination); suffix++)
            destination = Path.Combine(folder, $"{baseName} ({suffix}){extension}");

        return destination;
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var filePath in Directory.EnumerateFiles(sourceDirectory))
            File.Copy(filePath, Path.Combine(destinationDirectory, Path.GetFileName(filePath)), overwrite: false);

        foreach (var directoryPath in Directory.EnumerateDirectories(sourceDirectory))
            CopyDirectory(
                directoryPath,
                Path.Combine(destinationDirectory, Path.GetFileName(directoryPath)));
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character =>
            invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "Panel" : sanitized;
    }
}
