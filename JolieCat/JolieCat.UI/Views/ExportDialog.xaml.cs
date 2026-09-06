using System.Windows;

namespace JolieCat.UI.Views
{
    /// <summary>
    /// Collects export options (target, format, quality) and reports them back via
    /// <see cref="Window.DialogResult"/> - the actual file-destination prompt and the
    /// export itself happen in <c>MainViewModel.ExportImageAsync</c> once this returns
    /// true, exactly like every other file operation in this app owns its own
    /// <c>SaveFileDialog</c>/<c>OpenFileDialog</c> rather than a dialog view doing it.
    /// </summary>
    public partial class ExportDialog : Window
    {
        public ExportDialog()
        {
            InitializeComponent();
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
