using CommunityToolkit.Mvvm.ComponentModel;

namespace JolieCat.UI.ViewModels.ToolOptions
{
    /// <summary>Options shown for the Warp (mesh deformation) tool: how dense the
    /// control-point grid is. Read only when a warp starts (see
    /// <c>CanvasViewModel.StartWarp</c>) - changing it while a warp is already in
    /// progress takes effect the next time Warp activates (switch tools away and
    /// back), not live, since a differently-shaped grid can't reuse the old one's
    /// already-dragged control points.</summary>
    public partial class WarpToolOptionsViewModel : ObservableObject
    {
        /// <summary>3x3 or 4x4 control points, per this tool's own "basic" scope.</summary>
        [ObservableProperty]
        private int gridSize = 3;

        public string Instructions { get; } =
            "Drag any grid point to warp the layer beneath it. Press Enter to apply, Escape to cancel.";
    }
}
