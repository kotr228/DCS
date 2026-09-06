using System.Windows;

namespace JolieCat.UI.Views
{
    /// <summary>Levels: input/output black/white points, gamma, and a live histogram
    /// (see <see cref="ViewModels.LevelsViewModel"/>).</summary>
    public partial class LevelsDialog : Window
    {
        public LevelsDialog()
        {
            InitializeComponent();
        }

        private void Apply_Click(object sender, RoutedEventArgs e) => DialogResult = true;

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
