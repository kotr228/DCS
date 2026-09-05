using CommunityToolkit.Mvvm.ComponentModel;

namespace JolieCat.UI.ViewModels.ToolOptions
{
    /// <summary>
    /// Options shown for every brush-like tool: Brush, Pencil, Eraser, and the
    /// retouching tools (Clone Stamp, Healing Brush, Blur, Sharpen, Sponge, Dodge, Burn).
    /// </summary>
    public partial class PaintToolOptionsViewModel : ObservableObject
    {
        [ObservableProperty]
        private double size = 24;

        [ObservableProperty]
        private double hardness = 80;

        [ObservableProperty]
        private double opacity = 100;

        /// <summary>Distance between consecutive Brush/Eraser dabs, as a percentage of
        /// the brush's own diameter - low values (well under 100) read as a smooth
        /// continuous stroke; high values (well over 100) read as a dotted/dashed one.
        /// Doesn't affect Pencil, which always draws a plain continuous hard-edged line.</summary>
        [ObservableProperty]
        private double spacing = 20;

        /// <summary>
        /// Whether this tool draws with the foreground color, so its Properties panel
        /// should show the color picker. True for Brush/Pencil; false for Eraser (which
        /// only clears pixels) and the retouching tools (which don't apply a flat color).
        /// </summary>
        public bool ShowColorPicker { get; }

        public PaintToolOptionsViewModel(bool showColorPicker = true)
        {
            ShowColorPicker = showColorPicker;
        }
    }
}
