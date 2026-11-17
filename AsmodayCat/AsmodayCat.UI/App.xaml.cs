using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using AsmodayCat.UI.Services;
using AsmodayCat.UI.ViewModels;
using AsmodayCat.UI.Views;

namespace AsmodayCat.UI;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public static string LogDirectory { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CatSuite", "AsmodayCat", "logs");

    public static void LogError(Exception ex)
    {
        try
        {
            System.IO.Directory.CreateDirectory(LogDirectory);
            System.IO.File.AppendAllText(System.IO.Path.Combine(LogDirectory, $"ui-{DateTime.Now:yyyyMMdd}.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [ERR] {ex}{Environment.NewLine}");
        }
        catch (Exception) { /* logging must never throw */ }
    }

    // One app per user: a second start (shortcut while running in the tray) just shows the first one
    private static Mutex?           _singleInstance;
    private static EventWaitHandle? _showSignal;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(true, @"Local\AsmodayCat.UI", out var isFirst);
        _showSignal     = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\AsmodayCat.UI.Show");
        if (!isFirst)
        {
            if (!e.Args.Contains("--tray")) _showSignal.Set();
            Shutdown();
            return;
        }
        new Thread(() =>
        {
            while (_showSignal.WaitOne())
                Dispatcher.InvokeAsync(() =>
                {
                    // Still on the startup screen: let it finish; otherwise bring the main window up
                    if (Windows.OfType<StartupView>().FirstOrDefault() is { } s) s.Activate();
                    else Services.GetRequiredService<TrayService>().ShowMainWindow();
                });
        }) { IsBackground = true, Name = "ShowSignal" }.Start();

        // Unexpected errors: write them to the log and tell the user instead of crashing silently
        DispatcherUnhandledException += (_, args) =>
        {
            LogError(args.Exception);
            MessageBox.Show(AsmodayCat.UI.Localization.Loc.F("app.crash", args.Exception.Message, LogDirectory),
                "AsmodayCat", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) LogError(ex);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogError(args.Exception);
            args.SetObserved();
        };

        var services = new ServiceCollection();

        services.AddSingleton<IpcClient>();
        services.AddSingleton<ApprovalService>();
        services.AddSingleton<TrayService>();
        services.AddSingleton<UiSettingsStore>();
        services.AddSingleton<LanguageService>();
        services.AddSingleton<AsmodayCat.UI.Voice.SpeechInput>();
        services.AddSingleton<AsmodayCat.UI.Voice.SpeechOutput>();

        // Screens are created once per session: they keep their state between tab switches
        // and their polling timers don't pile up.
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<HardwareViewModel>();
        services.AddSingleton<AgentRulesViewModel>();
        services.AddSingleton<TerminalViewModel>();
        services.AddSingleton<ChatViewModel>();
        services.AddSingleton<ModelsManagerViewModel>();
        services.AddSingleton<MemoryViewModel>();
        services.AddTransient<StartupViewModel>();

        services.AddSingleton<DashboardView>();
        services.AddSingleton<HardwareView>();
        services.AddSingleton<AgentRulesView>();
        services.AddSingleton<TerminalView>();
        services.AddSingleton<ChatView>();
        services.AddSingleton<ModelsManagerView>();
        services.AddSingleton<MemoryView>();
        services.AddSingleton<MainWindow>();
        services.AddSingleton<OverlayWindow>();
        services.AddTransient<StartupView>();

        Services = services.BuildServiceProvider();

        // Interface language first: every window reads its texts from it
        Services.GetRequiredService<LanguageService>().Init();

        // Shows agent actions that need the user's approval, from any screen
        Services.GetRequiredService<ApprovalService>().Start();

        // The app lives in the tray: closing windows doesn't exit, "Вийти" in the tray menu does
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Services.GetRequiredService<TrayService>().Start();

        if (e.Args.Contains("--tray"))
        {
            // Started with Windows: no windows, just make sure the service runs and hotkeys work
            _ = Task.Run(async () =>
            {
                if (await ServiceLauncher.EnsureRunningAsync(Services.GetRequiredService<IpcClient>()))
                    await Services.GetRequiredService<LanguageService>().SyncServiceAsync();
            });
            new System.Windows.Interop.WindowInteropHelper(Services.GetRequiredService<MainWindow>()).EnsureHandle();
            return;
        }

        var startup = Services.GetRequiredService<StartupView>();
        startup.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Services?.GetService<TrayService>()?.Dispose();
        base.OnExit(e);
    }
}
