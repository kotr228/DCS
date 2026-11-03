using System.Windows;
using System.Windows.Controls;
using JolieCat.UI.Media;
using JolieCat.UI.ViewModels;

namespace JolieCat.UI.Views
{
    /// <summary>
    /// Compact, always-expanded color picker (Saturation/Brightness box, Hue strip, hex
    /// code, preview swatch, quick-pick palette, the user's own swatches) embedded
    /// directly in the Properties panel for whichever tool is drawing with color right
    /// now. The color state lives on the <see cref="CanvasViewModel"/> this control is
    /// bound to; the user swatches live in <see cref="SwatchLibrary.Shared"/>.
    /// </summary>
    public partial class ColorPickerPanel : UserControl
    {
        public ColorPickerPanel()
        {
            InitializeComponent();
            UserSwatches.ItemsSource = SwatchLibrary.Shared.Colors;
        }

        private CanvasViewModel? Canvas => DataContext as CanvasViewModel;

        private void AddSwatch_Click(object sender, RoutedEventArgs e)
        {
            if (Canvas is { } canvas) SwatchLibrary.Shared.Add(canvas.HexCode);
        }

        private void UserSwatch_Click(object sender, RoutedEventArgs e)
        {
            if (Canvas is { } canvas && sender is FrameworkElement { Tag: string hex })
                canvas.HexCode = hex;
        }

        private void DeleteSwatch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string hex }) SwatchLibrary.Shared.Remove(hex);
        }
    }
}
