using CommunityToolkit.Mvvm.ComponentModel;

namespace JolieCat.UI.ViewModels
{
    public partial class CanvasViewModel
    {
        /// <summary>Which channel the canvas shows: 0 = all (RGB), 1..3 = only Red,
        /// Green or Blue, as gray (Channels panel, Ctrl+2..5). Only the view changes.</summary>
        [ObservableProperty] private int viewChannel;

        partial void OnViewChannelChanged(int value) => RaiseInvalidate();
    }
}
