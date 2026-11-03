using System.Windows;

namespace JolieCat.UI.Views
{
    /// <summary>Filter &gt; Blur Gallery (see <see cref="ViewModels.BlurGalleryViewModel"/>).</summary>
    public partial class BlurGalleryDialog : Window
    {
        public BlurGalleryDialog()
        {
            InitializeComponent();
        }

        private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    }
}
