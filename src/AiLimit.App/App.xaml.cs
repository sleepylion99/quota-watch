using System.IO;
using AiLimit.App.Services;
using AiLimit.App.Tray;

namespace AiLimit.App;

public partial class App : System.Windows.Application
{
    private const long MaxCrashLogBytes = 1_000_000;
    private const string SingleInstanceMutexName = @"Local\AiLimitDashboard-A4D216A2-2D4F-4EA7-90AA-FCC7EBC809D4";

    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private AppState? _state;
    private TrayController? _trayController;

    protected override async void OnStartup(System.Windows.StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        _ownsSingleInstanceMutex = true;
        base.OnStartup(e);

        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        _state = new AppState();
        await _state.InitializeAsync();

        // Apply theme before showing any window so the first paint uses the correct colors.
        try
        {
            var probe = new RegistrySystemThemeProbe();
            var swapper = new ApplicationResourceSwapper();
            var themeService = new ThemeService(probe, swapper, watchSystemThemeChanges: true);
            themeService.Apply(_state.CurrentSettings.ThemeMode);
            _state.ThemeService = themeService;
        }
        catch (Exception ex)
        {
            // Theme application must never crash the app — fall back to dark (the default Colors.Dark.xaml).
            WriteCrashLog(ex);
        }

        _trayController = new TrayController(_state);
        _state.ShowDashboard();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _state?.ThemeService?.Dispose();
        _trayController?.Dispose();
        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(
        object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog(e.Exception);
        e.Handled = true;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            WriteCrashLog(exception);
        }
    }

    private static void WriteCrashLog(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.AppDataDirectory);
            var path = Path.Combine(AppPaths.AppDataDirectory, "crash.log");
            var message = $"[{DateTimeOffset.Now:O}]{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}";
            var mode = File.Exists(path) && new FileInfo(path).Length > MaxCrashLogBytes
                ? FileMode.Create
                : FileMode.Append;

            using var stream = new FileStream(path, mode, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream);
            writer.Write(message);
        }
        catch
        {
            // Last-resort logging must never trigger another UI crash.
        }
    }

}
