using System.Windows.Controls;

namespace JolieCat.UI.Views
{
    /// <summary>
    /// Compact, always-expanded color picker (Saturation/Brightness box, Hue strip, hex
    /// code, preview swatch, quick-pick palette) embedded directly in the Properties
    /// panel for whichever tool is drawing with color right now - Brush, Pencil, Paint
    /// Bucket, Gradient, or the Eyedropper. Purely a view: all state (the active color,
    /// the Hue/Saturation/Brightness it's built from, the swatch/hex commands) lives on
    /// whatever <see cref="ViewModels.CanvasViewModel"/> this control's DataContext is
    /// bound to.
    /// </summary>
    public partial class ColorPickerPanel : UserControl
    {
        public ColorPickerPanel()
        {
            InitializeComponent();
        }
    }
}
