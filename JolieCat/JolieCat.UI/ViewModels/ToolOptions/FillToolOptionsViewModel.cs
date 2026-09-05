using CommunityToolkit.Mvvm.ComponentModel;

namespace JolieCat.UI.ViewModels.ToolOptions
{
    /// <summary>Options shown for the Paint Bucket tool.</summary>
    public partial class FillToolOptionsViewModel : ObservableObject
    {
        [ObservableProperty]
        private double tolerance = 32;

        [ObservableProperty]
        private bool contiguous = true;
    }
}
