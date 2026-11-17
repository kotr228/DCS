using CommunityToolkit.Mvvm.Input;

namespace JolieCat.UI.ViewModels
{
    public partial class MainViewModel
    {
        /// <summary>Ctrl+2 (all channels), Ctrl+3/4/5 (Red/Green/Blue alone).</summary>
        [RelayCommand]
        private void ViewChannel(string channel)
        {
            if (int.TryParse(channel, out var n)) Canvas.ViewChannel = n is >= 0 and <= 3 ? n : 0;
        }
    }
}
