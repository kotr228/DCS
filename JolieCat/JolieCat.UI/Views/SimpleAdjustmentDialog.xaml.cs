using System.Windows;

namespace JolieCat.UI.Views
{
    /// <summary>
    /// The shared shell for Brightness/Contrast and Hue/Saturation/Lightness (see
    /// <see cref="ViewModels.SimpleAdjustmentViewModel"/>) - reports back via
    /// <see cref="Window.DialogResult"/> only, same as <see cref="FilterDialog"/>.
    /// </summary>
    public partial class SimpleAdjustmentDialog : Window
    {
        public SimpleAdjustmentDialog()
        {
            InitializeComponent();
        }

        private void Apply_Click(object sender, RoutedEventArgs e) => DialogResult = true;

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
