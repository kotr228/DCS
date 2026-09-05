using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace JolieCat.UI.ViewModels
{
    /// <summary>
    /// View model for <see cref="MainWindow"/>: owns the visibility of the four docking
    /// zones (Left/Right/Bottom) around the central canvas.
    /// </summary>
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty]
        private bool isLeftPanelVisible = true;

        [ObservableProperty]
        private bool isRightPanelVisible = true;

        [ObservableProperty]
        private bool isBottomPanelVisible = true;

        [RelayCommand]
        private void ToggleLeftPanel() => IsLeftPanelVisible = !IsLeftPanelVisible;

        [RelayCommand]
        private void ToggleRightPanel() => IsRightPanelVisible = !IsRightPanelVisible;

        [RelayCommand]
        private void ToggleBottomPanel() => IsBottomPanelVisible = !IsBottomPanelVisible;
    }
}
