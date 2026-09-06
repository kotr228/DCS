using System.Windows;

namespace JolieCat.UI.Views
{
    /// <summary>
    /// The shared shell for Gaussian Blur, Box Blur, Sharpen, and Noise (see
    /// <see cref="ViewModels.FilterOptionsViewModel"/>) - reports back via
    /// <see cref="Window.DialogResult"/> only; the live preview and the actual
    /// commit/cancel of the adjustment happen in <c>MainViewModel</c>, which owns
    /// the canvas the preview renders onto.
    /// </summary>
    public partial class FilterDialog : Window
    {
        public FilterDialog()
        {
            InitializeComponent();
        }

        private void Apply_Click(object sender, RoutedEventArgs e) => DialogResult = true;

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
