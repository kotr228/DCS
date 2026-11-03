using System.Windows;
using System.Windows.Input;
using JolieCat.UI.ViewModels;

namespace JolieCat.UI.Views
{
    /// <summary>Edit &gt; Manage Patterns (see <see cref="PatternManagerViewModel"/>).</summary>
    public partial class PatternManagerDialog : Window
    {
        public PatternManagerDialog()
        {
            InitializeComponent();
        }

        private void NameBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            ((PatternManagerViewModel)DataContext).RenameCommand.Execute(null);
            e.Handled = true;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
