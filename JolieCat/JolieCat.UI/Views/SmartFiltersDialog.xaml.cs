using System.Windows;
using System.Windows.Input;
using JolieCat.UI.ViewModels;

namespace JolieCat.UI.Views
{
    /// <summary>Layer &gt; Smart Filters (see <see cref="SmartFiltersViewModel"/>).</summary>
    public partial class SmartFiltersDialog : Window
    {
        public SmartFiltersDialog(SmartFiltersViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            viewModel.Rasterized += Close;
        }

        private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
            ((SmartFiltersViewModel)DataContext).EditCommand.Execute(null);

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
