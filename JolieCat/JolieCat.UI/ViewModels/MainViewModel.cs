using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Tools;
using JolieCat.UI.ViewModels.Timeline;

namespace JolieCat.UI.ViewModels
{
    /// <summary>
    /// View model for <see cref="MainWindow"/>: owns the visibility of the four docking
    /// zones (Left/Right/Bottom) around the central canvas, and composes the panels'
    /// own view models (toolbox, canvas, timeline) rather than flattening their state
    /// in here as the editor grows more panels.
    /// </summary>
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty]
        private bool isLeftPanelVisible = true;

        [ObservableProperty]
        private bool isRightPanelVisible = true;

        [ObservableProperty]
        private bool isBottomPanelVisible = true;

        public ToolboxViewModel Toolbox { get; }

        public CanvasViewModel Canvas { get; }

        public TimelineViewModel Timeline { get; }

        public MainViewModel()
        {
            Toolbox = new ToolboxViewModel();
            Canvas = new CanvasViewModel(Toolbox);
            Timeline = new TimelineViewModel();
        }

        [RelayCommand]
        private void ToggleLeftPanel() => IsLeftPanelVisible = !IsLeftPanelVisible;

        [RelayCommand]
        private void ToggleRightPanel() => IsRightPanelVisible = !IsRightPanelVisible;

        [RelayCommand]
        private void ToggleBottomPanel() => IsBottomPanelVisible = !IsBottomPanelVisible;

        /// <summary>
        /// Thin pass-through so tool selection can also be driven from MainViewModel
        /// (e.g. future keyboard shortcuts), in addition to the Tools panel's own list
        /// selection binding directly to <see cref="Toolbox"/>.
        /// </summary>
        [RelayCommand]
        private void SelectTool(ToolDefinition? tool) => Toolbox.SelectToolCommand.Execute(tool);
    }
}
