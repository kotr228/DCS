using CommunityToolkit.Mvvm.ComponentModel;

namespace JolieCat.UI.ViewModels.ToolOptions
{
    /// <summary>Options shown for the Gradient tool.</summary>
    public partial class GradientToolOptionsViewModel : ObservableObject
    {
        [ObservableProperty]
        private double angle = 90;

        [ObservableProperty]
        private bool reverse;
    }
}
