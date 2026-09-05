using CommunityToolkit.Mvvm.ComponentModel;

namespace JolieCat.UI.ViewModels.ToolOptions
{
    /// <summary>Options shown for the Pen (Path) and Shape tools.</summary>
    public partial class ShapeToolOptionsViewModel : ObservableObject
    {
        [ObservableProperty]
        private double strokeWidth = 2;

        [ObservableProperty]
        private bool fillEnabled = true;
    }
}
