using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace JolieCat.UI.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty]
        private bool isLeftPanelVisible = true;

        [ObservableProperty]
        private bool isRightPanelVisible = true;

        [ObservableProperty]
        private bool isBottomPanelVisible = true;

        [RelayCommand]
        public void ToggleLeftPanel()
        {
            IsLeftPanelVisible = !IsLeftPanelVisible;
        }

        [RelayCommand]
        public void ToggleRightPanel()
        {
            IsRightPanelVisible = !IsRightPanelVisible;
        }

        [RelayCommand]
        public void ToggleBottomPanel()
        {
            IsBottomPanelVisible = !IsBottomPanelVisible;
        }
    }
}