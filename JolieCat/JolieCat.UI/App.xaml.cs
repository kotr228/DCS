using System;
using System.Threading.Tasks;
using System.Windows;

namespace JolieCat.UI
{
    /// <summary>
    /// Interaction logic for App.xaml. Owns the startup sequence: show the splash
    /// screen immediately, then construct and show <see cref="MainWindow"/> once it's
    /// ready, closing the splash right as it appears - see <see cref="OnStartup"/>.
    /// </summary>
    public partial class App : Application
    {
        /// <summary>How long the splash screen stays up at minimum, so it's actually
        /// readable rather than flashing by faster than a user could register it -
        /// constructing <see cref="MainWindow"/> (its whole view-model graph: the
        /// shared Toolbox, the first document's Layers/Canvas/Timeline) is real work,
        /// but modest enough on any reasonable machine that without this the splash
        /// would barely show at all.</summary>
        private static readonly TimeSpan MinimumSplashDuration = TimeSpan.FromMilliseconds(1200);

        /// <summary>
        /// Replaces the XAML-declared <c>StartupUri="MainWindow.xaml"</c> (removed from
        /// App.xaml) with a manual sequence: show <see cref="Views.SplashScreenWindow"/>
        /// first, build <see cref="MainWindow"/> alongside a minimum-duration delay (so
        /// the two overlap instead of the delay always paying on top of construction),
        /// then show the main window and close the splash. <c>async void</c> here
        /// mirrors the base <see cref="Application.OnStartup"/> override it replaces -
        /// an event-handler shape, not a value anything else awaits - and the delay
        /// specifically needs to run without blocking the UI thread, or the splash's
        /// own spinner animation would freeze right along with it.
        /// </summary>
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var splash = new Views.SplashScreenWindow();
            splash.Show();

            try
            {
                var minimumDelay = Task.Delay(MinimumSplashDuration);

                var mainWindow = new MainWindow();

                await minimumDelay;

                MainWindow = mainWindow;
                mainWindow.Show();
            }
            finally
            {
                // Always closes the splash, even if constructing MainWindow above
                // threw - a stuck splash with no way forward would be worse than
                // letting ShutdownMode's own default (OnLastWindowClose) end the app
                // once this, the only open window, closes.
                splash.Close();
            }
        }
    }
}
