using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Threading;

namespace JolieCat3D.UI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App() => DispatcherUnhandledException += OnDispatcherUnhandledException;

        /// <summary>The last-resort safety net for an exception that escapes every
        /// narrower guard this app already has (<c>MainWindow.TryRun</c>/<c>TryRunAsync</c>
        /// around every button/menu action that can fail, the <c>_isInitialized</c> check
        /// every event handler starts with, <c>JolieWorkspaceWatcher</c>'s own internal
        /// try/catch around its background file-system events) - by definition something
        /// this handler ever sees was NOT anticipated by one of those. WPF's own default
        /// behavior for an unhandled <see cref="DispatcherUnhandledException"/> is to tear
        /// down the whole application with no chance to save work in progress; for a
        /// modeling tool where a crash can mean losing an unsaved scene, showing the error
        /// and marking it <see cref="DispatcherUnhandledExceptionEventArgs.Handled"/>
        /// (letting the app keep running) is the safer default - a visibly broken feature
        /// the user can work around beats an invisible one that silently destroyed their
        /// session. This is deliberately a BLUNT, generic net, not a substitute for the
        /// specific, contextual handling <c>TryRun</c> already gives most operations their
        /// own descriptive failure message - reaching here at all is itself the signal
        /// something needs a narrower fix.</summary>
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(
                $"An unexpected error occurred:\n{e.Exception.Message}\n\nJolieCat3D will try to keep running, but consider saving your work under a new file name as a precaution.",
                "JolieCat3D", MessageBoxButton.OK, MessageBoxImage.Error);

            e.Handled = true;
        }
    }

}
