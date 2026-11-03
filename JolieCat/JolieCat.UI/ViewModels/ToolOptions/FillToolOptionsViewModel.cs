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

        /// <summary>False for the Magic Eraser, which shares these options but erases.</summary>
        public bool ShowColorPicker { get; init; } = true;
    }

    /// <summary>Options shown for the Red Eye tool.</summary>
    public partial class RedEyeToolOptionsViewModel : ObservableObject
    {
        /// <summary>Radius of the area around the click that is searched for red, in pixels.</summary>
        [ObservableProperty]
        private double pupilSize = 30;

        /// <summary>0..100: how much darker the corrected pupil gets.</summary>
        [ObservableProperty]
        private double darkenAmount = 50;
    }
}
