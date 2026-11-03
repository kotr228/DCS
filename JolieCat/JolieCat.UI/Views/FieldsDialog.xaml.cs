using System.Windows;
using JolieCat.UI.ViewModels;

namespace JolieCat.UI.Views
{
    /// <summary>A settings dialog made of fields (see <see cref="FieldsDialogViewModel"/>).</summary>
    public partial class FieldsDialog : Window
    {
        public FieldsDialog(FieldsDialogViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        /// <summary>Shows <paramref name="viewModel"/>'s dialog; true if OK was pressed.</summary>
        public static bool Show(FieldsDialogViewModel viewModel) =>
            new FieldsDialog(viewModel) { Owner = Application.Current?.MainWindow }.ShowDialog() == true;

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var vm = (FieldsDialogViewModel)DataContext;
            if (vm.Validate?.Invoke() is { } error)
            {
                vm.Error = error;
                return;
            }
            DialogResult = true;
        }
    }
}
