using System.Windows;

namespace JolieCat.UI.Views
{
    /// <summary>The Layer Style dialog (see <see cref="ViewModels.LayerStyleViewModel"/>).
    /// Reports back via DialogResult; MainViewModel previews and commits.</summary>
    public partial class LayerStyleDialog : Window
    {
        public LayerStyleDialog()
        {
            InitializeComponent();
        }

        private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    }
}
