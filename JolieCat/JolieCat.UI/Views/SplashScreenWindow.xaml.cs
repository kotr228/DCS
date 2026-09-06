using System.Windows;

namespace JolieCat.UI.Views
{
    /// <summary>
    /// The startup splash screen: the logo, app title, and an indeterminate loading
    /// spinner, shown by <see cref="App.OnStartup"/> while <see cref="MainWindow"/>
    /// constructs. Holds no state or logic of its own beyond <c>InitializeComponent</c> -
    /// <see cref="App"/> owns the whole show/close lifecycle (including the minimum
    /// display duration so this doesn't just flash by unreadably on a fast machine).
    /// </summary>
    public partial class SplashScreenWindow : Window
    {
        public SplashScreenWindow()
        {
            InitializeComponent();
        }
    }
}
