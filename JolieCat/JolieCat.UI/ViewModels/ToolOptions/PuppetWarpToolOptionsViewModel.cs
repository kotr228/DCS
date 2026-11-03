using CommunityToolkit.Mvvm.ComponentModel;

namespace JolieCat.UI.ViewModels.ToolOptions
{
    /// <summary>Options shown for the Puppet Warp tool.</summary>
    public partial class PuppetWarpToolOptionsViewModel : ObservableObject
    {
        /// <summary>Mesh cell size in pixels (smaller = smoother bends, slower). Used
        /// when a puppet warp starts.</summary>
        [ObservableProperty]
        private double density = 16;

        /// <summary>0.5..3: low bends broadly like soft rubber, high keeps each pin's
        /// surroundings stiffer.</summary>
        [ObservableProperty]
        private double rigidity = 1;

        [ObservableProperty]
        private bool showMesh = true;
    }
}
