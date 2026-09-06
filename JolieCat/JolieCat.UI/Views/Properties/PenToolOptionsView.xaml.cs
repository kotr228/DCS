using System.Windows.Controls;

namespace JolieCat.UI.Views.Properties
{
    /// <summary>Stroke width, color, and the Convert to Selection/Stroke Path/Fill Path
    /// actions shown for the Pen, Path Selection, and Direct Selection tools - all three
    /// share <c>ViewModels.ToolOptions.PenToolOptionsViewModel</c> since they edit the
    /// very same working path (see <c>CanvasViewModel.PenPath</c>).</summary>
    public partial class PenToolOptionsView : UserControl
    {
        public PenToolOptionsView()
        {
            InitializeComponent();
        }
    }
}
