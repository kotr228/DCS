using System.Windows;

namespace JolieCat.UI.Views
{
    /// <summary>The Camera Raw Filter dialog (see <see cref="ViewModels.CameraRawViewModel"/>).</summary>
    public partial class CameraRawDialog : Window
    {
        public CameraRawDialog()
        {
            InitializeComponent();
        }

        private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    }
}
