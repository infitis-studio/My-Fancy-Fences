using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace My_Fancy_Fences;

public static class WallpaperPersistenceService
{
    private const int SpiSetDeskWallpaper = 0x0014;
    private const int SpifUpdateIniFile = 0x01;
    private const int SpifSendWinIniChange = 0x02;
    private const string DesktopRegistryPath = @"Control Panel\Desktop";

    public static void RepairMissingWallpaperFromWindowsCache()
    {
        try
        {
            var currentPath = ReadCurrentWallpaperPath();
            if (!string.IsNullOrWhiteSpace(currentPath) && File.Exists(currentPath))
                return;

            var windowsCachePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft",
                "Windows",
                "Themes",
                "TranscodedWallpaper");
            if (File.Exists(windowsCachePath))
                SaveAndApplyStableCopy(windowsCachePath);
        }
        catch
        {
            // Brak możliwości odzyskania tapety nie może blokować startu aplikacji.
        }
    }

    public static void PreserveCurrentWallpaperBeforeRestart()
    {
        try
        {
            var currentPath = ReadCurrentWallpaperPath();
            if (!string.IsNullOrWhiteSpace(currentPath) && File.Exists(currentPath))
            {
                SaveAndApplyStableCopy(currentPath);
                return;
            }

            RepairMissingWallpaperFromWindowsCache();
        }
        catch
        {
            // Import ustawień może być kontynuowany nawet bez kopii tapety.
        }
    }

    private static string? ReadCurrentWallpaperPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(DesktopRegistryPath, writable: false);
        return key?.GetValue("Wallpaper") as string;
    }

    private static void SaveAndApplyStableCopy(string sourcePath)
    {
        var targetDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "My Fancy Fences",
            "Wallpapers");
        Directory.CreateDirectory(targetDirectory);
        var targetPath = Path.Combine(targetDirectory, "current-wallpaper.jpg");
        var temporaryPath = $"{targetPath}.{Guid.NewGuid():N}.tmp";

        using (var source = File.Open(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
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
            using var target = File.Create(temporaryPath);
            encoder.Save(target);
        }

        File.Move(temporaryPath, targetPath, overwrite: true);
        SystemParametersInfo(
            SpiSetDeskWallpaper,
            0,
            targetPath,
            SpifUpdateIniFile | SpifSendWinIniChange);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(
        int action,
        int parameter,
        string value,
        int flags);
}
