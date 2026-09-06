using CommunityToolkit.Mvvm.ComponentModel;

namespace JolieCat.UI.ViewModels.ToolOptions
{
    /// <summary>
    /// Options shown for the Pen tool - and, since they edit the very same working
    /// path, also while Path Selection or Direct Selection is active (see
    /// <see cref="ToolboxViewModel.CreateOptions"/>). Purely data here (the stroke
    /// width a "Stroke Path" paints with); the "Convert to Selection"/"Stroke Path"/
    /// "Fill Path" actions themselves are commands on <c>CanvasViewModel</c> - the
    /// same split <see cref="PaintToolOptionsViewModel"/> uses for its color picker
    /// (see <c>Views.Properties.PenToolOptionsView</c>'s DataContext rebind).
    /// </summary>
    public partial class PenToolOptionsViewModel : ObservableObject
    {
        [ObservableProperty]
        private double strokeWidth = 2;
    }
}
