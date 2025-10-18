namespace GrayCat.UI.Controls;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GrayCat.UI.ViewModels;

public partial class PropertiesPanel : UserControl
{
    public PropertiesPanel()
    {
        InitializeComponent();
    }

    private void ColorPreview_Click(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || viewModel.SelectedBlock == null)
            return;

        var colorDialog = new System.Windows.Forms.ColorDialog
        {
            Color = ColorFromHex(viewModel.SelectedBlock.Color)
        };

        if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            viewModel.SelectedBlock.Color = $"#{colorDialog.Color.R:X2}{colorDialog.Color.G:X2}{colorDialog.Color.B:X2}";
        }
    }

    private void BackgroundPreview_Click(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || viewModel.SelectedBlock == null)
            return;

        var colorDialog = new System.Windows.Forms.ColorDialog
        {
            Color = ColorFromHex(viewModel.SelectedBlock.Background)
        };

        if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            viewModel.SelectedBlock.Background = $"#{colorDialog.Color.R:X2}{colorDialog.Color.G:X2}{colorDialog.Color.B:X2}";
        }
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || viewModel.SelectedBlock == null)
            return;

        var result = MessageBox.Show(
            "Reset this block to default properties?",
            "Confirm Reset",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            viewModel.SelectedBlock.Color = "#000000";
            viewModel.SelectedBlock.Background = "#FFFFFF";
            viewModel.SelectedBlock.FontSize = 14;
            viewModel.SelectedBlock.TextAlignment = "Left";
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || viewModel.SelectedBlock == null)
            return;

        var result = MessageBox.Show(
            $"Delete this {viewModel.SelectedBlock.Type} block?",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            viewModel.RemoveBlock(viewModel.SelectedBlock);
        }
    }

    private System.Drawing.Color ColorFromHex(string hex)
    {
        try
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 6)
            {
                return System.Drawing.Color.FromArgb(
                    Convert.ToInt32(hex.Substring(0, 2), 16),
                    Convert.ToInt32(hex.Substring(2, 2), 16),
                    Convert.ToInt32(hex.Substring(4, 2), 16));
            }
        }
        catch { }

        return System.Drawing.Color.White;
    }
}