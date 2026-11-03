using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

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
            CrashGuard.Install(this);

            // WPF never applies the implicit Window style in App.xaml to a *derived*
            // window class, so every dialog (Filters, Levels, Export, ...) used to open
            // with the default white background - and white text from the global
            // TextBlock style on top of it. This handler gives every window the dark
            // theme background and a dark OS title bar instead.
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnAnyWindowLoaded));

            // The user's own patterns, so layer styles that use them draw right away.
            Media.PatternLibrary.EnsureLoaded();

            var splash = new Views.SplashScreenWindow();
            splash.Show();

            try
            {
                var minimumDelay = Task.Delay(MinimumSplashDuration);

                var mainWindow = new MainWindow();

                await minimumDelay;

                MainWindow = mainWindow;
                mainWindow.Show();

                // Files passed on the command line ("Open with", or dropped on the exe).
                if (mainWindow.DataContext is ViewModels.MainViewModel main)
                    foreach (var path in e.Args)
                        if (System.IO.File.Exists(path))
                            main.OpenPath(System.IO.Path.GetFullPath(path));
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

        private static void OnAnyWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Window window) return;

            if (window.ReadLocalValue(Control.BackgroundProperty) == DependencyProperty.UnsetValue &&
                Current.TryFindResource("WindowBackgroundBrush") is System.Windows.Media.Brush background)
            {
                window.Background = background;
            }

            if (window.ReadLocalValue(Control.ForegroundProperty) == DependencyProperty.UnsetValue &&
                Current.TryFindResource("TextPrimaryBrush") is System.Windows.Media.Brush foreground)
            {
                window.Foreground = foreground;
            }

            if (window.ReadLocalValue(Control.FontFamilyProperty) == DependencyProperty.UnsetValue)
                window.FontFamily = new System.Windows.Media.FontFamily("Segoe UI");

            UseDarkTitleBar(window);
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        /// <summary>Asks Windows 10 (20H1+) / 11 to draw this window's native title bar
        /// dark. Harmless no-op on older systems and on the main window, which draws its
        /// own title bar.</summary>
        private static void UseDarkTitleBar(Window window)
        {
            const int DwmwaUseImmersiveDarkMode = 20;
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;

            var enabled = 1;
            _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
        }
    }
}
