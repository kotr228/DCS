using System.Windows.Controls;

namespace JolieCat.UI.Views
{
    /// <summary>
    /// Toolbar color picker: a swatch button that opens a popup with preset swatches and
    /// RGB sliders. Purely a view - all state (the active color, the swatch/slider
    /// commands) lives on whatever <see cref="ViewModels.CanvasViewModel"/> this control's
    /// DataContext is bound to.
    /// </summary>
    public partial class ColorPickerButton : UserControl
    {
        public ColorPickerButton()
        {
            InitializeComponent();
        }
    }
}
