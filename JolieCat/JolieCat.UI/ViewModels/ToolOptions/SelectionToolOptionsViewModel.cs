using CommunityToolkit.Mvvm.ComponentModel;

namespace JolieCat.UI.ViewModels.ToolOptions
{
    /// <summary>
    /// Options shown for the selection tools: Rectangular Marquee and Elliptical
    /// Marquee, the only two with real drag-to-select canvas behavior. (The lassos,
    /// Quick Selection, and Magic Wand were dropped from the toolbox - they had icons
    /// and this same options panel but no canvas behavior behind them.)
    /// </summary>
    public partial class SelectionToolOptionsViewModel : ObservableObject
    {
        [ObservableProperty]
        private double featherRadius;

        [ObservableProperty]
        private double tolerance = 32;

        [ObservableProperty]
        private bool antiAlias = true;
    }
}
