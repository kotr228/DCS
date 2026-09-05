using System.Windows.Controls;

namespace JolieCat.UI.Views
{
    /// <summary>
    /// Compact, always-expanded color picker (preview swatch, quick-pick palette, RGB
    /// sliders) embedded directly in the Properties panel for whichever tool is drawing
    /// with color right now - Brush, Pencil, Paint Bucket, or Gradient. Purely a view:
    /// all state (the active color, the swatch/slider commands) lives on whatever
    /// <see cref="ViewModels.CanvasViewModel"/> this control's DataContext is bound to.
    /// </summary>
    public partial class ColorPickerPanel : UserControl
    {
        public ColorPickerPanel()
        {
            InitializeComponent();
        }
    }
}
