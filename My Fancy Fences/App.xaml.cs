using System.Threading;
using System.IO;
using System.Windows;

namespace My_Fancy_Fences;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\InfitisStudio.MyFancyFences.SingleInstance";
    private Mutex? _singleInstanceMutex;

    public App()
    {
        LogStartup("Application created: " + Environment.ProcessPath);
        DispatcherUnhandledException += (_, args) => LogStartup(args.Exception.ToString());
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogStartup(args.ExceptionObject.ToString() ?? "Unhandled exception");
    }

    private static void LogStartup(string message)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "My Fancy Fences");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "startup.log"), $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        LogStartup("Startup entered");
        SmoothScrollService.Register();

        if (ApplicationUpdater.TryResumePendingUpdateOnStartup())
        {
            LogStartup("Closing to resume a pending update");
            Shutdown();
            return;
        }

        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            SingleInstanceMutexName,
            out var isFirstInstance);
        if (!isFirstInstance)
        {
            LogStartup("Closing because another instance owns the mutex");
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        FirstRunService.Initialize();
        WallpaperPersistenceService.RepairMissingWallpaperFromWindowsCache();
        LocalizationService.Initialize();
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is Window window)
                    LocalizationService.Apply(window);
            }));
        base.OnStartup(e);
        MainWindow = new MainWindow();
        MainWindow.Show();
        LogStartup("Startup initialized");
        _ = LiveWallpaperService.RestoreLastLiveWallpaperAsync();
        _ = UpdateService.CheckAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LogStartup($"Exit {e.ApplicationExitCode}");
        LiveWallpaperService.Shutdown();

        if (_singleInstanceMutex is not null)
        {
            _singleInstanceMutex.ReleaseMutex();
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }

        base.OnExit(e);
    }
}
