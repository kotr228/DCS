using CommunityToolkit.Mvvm.ComponentModel;

namespace JolieCat.UI.ViewModels
{
    /// <summary>
    /// Foundation for the center canvas. Today the render surface only draws a test
    /// pattern and doesn't read from this view model yet - this is where zoom/pan and
    /// the active document will attach once the real per-document render pipeline
    /// (layers, brush strokes, vector paths) replaces the test render.
    /// </summary>
    public partial class CanvasViewModel : ObservableObject
    {
        [ObservableProperty]
        private double zoom = 1.0;
    }
}
