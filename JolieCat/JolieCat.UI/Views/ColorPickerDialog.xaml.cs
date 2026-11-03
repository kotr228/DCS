using System.Windows;
using System.Windows.Input;
using JolieCat.UI.ViewModels;

namespace JolieCat.UI.Views
{
    /// <summary>The Color Picker dialog (see <see cref="ColorPickerDialogViewModel"/>).</summary>
    public partial class ColorPickerDialog : Window
    {
        public ColorPickerDialog(ColorPickerDialogViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        /// <summary>Shows the picker for <paramref name="hex"/>; returns the chosen
        /// "#RRGGBB", or null if cancelled.</summary>
        public static string? Pick(string? hex, Window? owner)
        {
            var vm = new ColorPickerDialogViewModel(hex);
            var dialog = new ColorPickerDialog(vm) { Owner = owner ?? Application.Current?.MainWindow };
            return dialog.ShowDialog() == true ? vm.Hex : null;
        }

        private void Original_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var vm = (ColorPickerDialogViewModel)DataContext;
            var c = vm.OriginalBrush.Color;
            vm.Hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }

        private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    }
}
