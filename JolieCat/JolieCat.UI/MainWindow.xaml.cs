using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JolieCat.UI.ViewModels;
using JolieCat.UI.ViewModels.Layers;

namespace JolieCat.UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
        }

        /// <summary>Double-clicking a layer's name label swaps in its inline rename textbox.</summary>
        private void LayerName_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2) return;
            if (sender is FrameworkElement { DataContext: LayerViewModel layer })
                layer.IsEditingName = true;
        }

        /// <summary>Focuses and selects the rename textbox's text as soon as it appears,
        /// so the user can start typing (or Ctrl+A/retype) immediately.</summary>
        private void LayerNameEditBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox textBox) return;

            textBox.Focus();
            textBox.SelectAll();
        }

        /// <summary>
        /// Enter commits the new name and exits edit mode; Escape exits without
        /// committing. The textbox's Text binding uses TextBox's default
        /// UpdateSourceTrigger (LostFocus), so simply hiding it on Escape discards
        /// whatever was typed - the bound <see cref="LayerViewModel.Name"/> (and the
        /// underlying layer) was never touched. Enter forces that same binding to commit
        /// early, since we're not actually moving focus away.
        /// </summary>
        private void LayerNameEditBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter && e.Key != Key.Escape) return;
            if (sender is not TextBox { DataContext: LayerViewModel layer } textBox) return;

            if (e.Key == Key.Enter)
                textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

            layer.IsEditingName = false;
            e.Handled = true;
        }

        /// <summary>Clicking away from the rename textbox commits it (TextBox's own
        /// LostFocus-triggered binding already did that before this handler runs) and
        /// exits edit mode.</summary>
        private void LayerNameEditBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: LayerViewModel layer })
                layer.IsEditingName = false;
        }
    }
}
