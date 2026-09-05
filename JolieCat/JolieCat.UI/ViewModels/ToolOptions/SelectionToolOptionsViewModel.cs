using CommunityToolkit.Mvvm.ComponentModel;

namespace JolieCat.UI.ViewModels.ToolOptions
{
    /// <summary>
    /// Options shown for the selection tools: the marquees, the lassos, Quick Selection,
    /// and Magic Wand.
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
