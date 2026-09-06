using System.Windows;

namespace JolieCat.UI.Views
{
    /// <summary>
    /// "Resize Canvas..." - reports back via <see cref="Window.DialogResult"/> only,
    /// same as every other adjustment dialog; <c>MainViewModel.ResizeCanvas</c> reads
    /// its <see cref="ViewModels.ResizeCanvasViewModel"/>'s own Width/Height once this
    /// returns true.
    /// </summary>
    public partial class ResizeCanvasDialog : Window
    {
        public ResizeCanvasDialog()
        {
            InitializeComponent();
        }

        private void Resize_Click(object sender, RoutedEventArgs e) => DialogResult = true;

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
