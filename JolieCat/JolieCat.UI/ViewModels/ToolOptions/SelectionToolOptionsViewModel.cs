using CommunityToolkit.Mvvm.ComponentModel;
using JolieCat.Core.Documents;

namespace JolieCat.UI.ViewModels.ToolOptions
{
    /// <summary>Settings shared by the selection tools (marquees, lassos, Magic Wand,
    /// Quick Selection).</summary>
    public partial class SelectionToolOptionsViewModel : ObservableObject
    {
        /// <summary>Soft edge applied to new selections, in pixels.</summary>
        [ObservableProperty]
        private double featherRadius;

        /// <summary>Color tolerance for Magic Wand and Quick Selection.</summary>
        [ObservableProperty]
        private double tolerance = 32;

        [ObservableProperty]
        private bool antiAlias = true;

        /// <summary>How a new shape combines with the existing selection when no
        /// modifier key is held (Shift = add, Alt = subtract, Shift+Alt = intersect
        /// always override it).</summary>
        [ObservableProperty]
        private SelectionCombine mode = SelectionCombine.New;

        /// <summary>Quick Selection's brush diameter.</summary>
        [ObservableProperty]
        private double brushSize = 40;
    }
}
