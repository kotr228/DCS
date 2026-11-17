namespace GrayCat.UI;

using GrayCat.UI.Localization;
using System.IO;
using System.Windows;
using GrayCat.UI.Views;
using System.Windows.Threading;
using GrayCat.Core.Constants;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Saved choice, or the Windows language on first run.
        Loc.Instance.Set(Loc.Normalize(GrayCat.UI.Services.UiSettings.Load().Language));
        base.OnStartup(e);

        Directory.CreateDirectory(AppConstants.Paths.AppData);
        Directory.CreateDirectory(AppConstants.Paths.Logs);
        Directory.CreateDirectory(AppConstants.Paths.Projects);

        DispatcherUnhandledException += OnUnhandledException;
        // Errors outside the UI thread: log them; a forgotten background task must not take the editor down.
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogError(args.Exception);
            args.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogError(args.ExceptionObject);
    }

    private static void LogError(object error)
    {
        try
        {
            File.AppendAllText(Path.Combine(AppConstants.Paths.Logs, "ui-errors.txt"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {error}\n\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Log and keep the editor alive instead of crashing and losing work.
        LogError(e.Exception);
        ThemedMessageBox.Show(Loc.F("Something went wrong:\n\n{0}\n\nYour project is saved automatically.", e.Exception.Message),
            "GrayCat Studio", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
