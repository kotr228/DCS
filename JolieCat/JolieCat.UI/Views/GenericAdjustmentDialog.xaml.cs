using System.Windows;

namespace JolieCat.UI.Views
{
    /// <summary>Settings dialog for Exposure, Black &amp; White, Color Balance, Photo
    /// Filter and Gradient Map (see <see cref="ViewModels.GenericAdjustmentViewModel"/>).</summary>
    public partial class GenericAdjustmentDialog : Window
    {
        public GenericAdjustmentDialog()
        {
            InitializeComponent();
        }

        private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    }
}
