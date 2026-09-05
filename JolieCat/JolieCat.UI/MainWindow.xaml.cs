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
        /// committing. Enter forces the textbox's Text binding (TextBox's default
        /// UpdateSourceTrigger is LostFocus, so it wouldn't otherwise fire here) to commit
        /// early, since we're not actually moving focus away. Escape instead resets the
        /// textbox's Text back to the layer's actual name before hiding it - collapsing a
        /// focused control's Visibility (which IsEditingName's own binding does next)
        /// makes WPF move focus away on its own, which would trigger that same
        /// LostFocus-triggered binding to commit whatever was typed anyway; feeding it
        /// back its own current value first makes that commit a no-op instead of a silent
        /// save of the discarded edit.
        /// </summary>
        private void LayerNameEditBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter && e.Key != Key.Escape) return;
            if (sender is not TextBox { DataContext: LayerViewModel layer } textBox) return;

            if (e.Key == Key.Enter)
                textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            else
                textBox.Text = layer.Name;

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
