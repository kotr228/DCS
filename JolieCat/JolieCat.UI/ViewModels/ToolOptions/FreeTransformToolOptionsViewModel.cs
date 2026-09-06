namespace JolieCat.UI.ViewModels.ToolOptions
{
    /// <summary>Options/usage instructions shown for the Free Transform tool. Free
    /// Transform's own live state (the bounding box, scale/rotation/translation) lives
    /// on <c>CanvasViewModel</c>, not here - this view model only carries the static
    /// instructional text the Properties panel shows while it's active.</summary>
    public sealed class FreeTransformToolOptionsViewModel
    {
        public string Instructions { get; } =
            "Drag a corner to scale, the handle above the box to rotate, or inside the box to move. " +
            "Press Enter to apply, Escape to cancel.";
    }
}
