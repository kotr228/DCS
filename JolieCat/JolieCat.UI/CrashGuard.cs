using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace JolieCat.UI
{
    /// <summary>
    /// Keeps a bug from silently closing the app with unsaved work: errors are logged
    /// to <see cref="LogPath"/> and shown, and the app keeps running so the user can
    /// save. If the same error keeps coming back (say, on every repaint), it stops
    /// showing dialogs after a few so the app stays usable.
    /// </summary>
    internal static class CrashGuard
    {
        public static string LogPath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JolieCat", "crash.log");

        private static string? _lastMessage;
        private static int _repeats;
        private static DateTime _lastShown = DateTime.MinValue;

        public static void Install(Application app)
        {
            app.DispatcherUnhandledException += OnDispatcherException;
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Log(e.ExceptionObject as Exception, "fatal");
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                Log(e.Exception, "background task");
                e.SetObserved();
            };
        }

        private static void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log(e.Exception, "ui");
            e.Handled = true;

            if (_showing) return; // a message is already up (it pumps messages, so errors can arrive meanwhile)
            var message = e.Exception.GetBaseException().Message;
            if (message == _lastMessage && DateTime.Now - _lastShown < TimeSpan.FromSeconds(10))
            {
                if (++_repeats >= 3) return; // repeating: logged, but no more dialogs
            }
            else
            {
                _repeats = 0;
            }
            _lastMessage = message;
            _lastShown = DateTime.Now;

            _showing = true;
            try
            {
                MessageBox.Show(
                    "Something went wrong, but your documents are still open - consider saving your work (File > Save As).\n\n" +
                    message + "\n\nDetails were written to " + LogPath,
                    "JolieCat", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                _showing = false;
            }
        }

        private static bool _showing;

        internal static void Log(Exception? error, string where)
        {
            if (error is null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ({where}) {error}\n\n");
            }
            catch
            {
                // Logging must never cause a second failure.
            }
        }
    }
}
