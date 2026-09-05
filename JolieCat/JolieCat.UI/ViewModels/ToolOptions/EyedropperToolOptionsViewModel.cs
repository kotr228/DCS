namespace JolieCat.UI.ViewModels.ToolOptions
{
    /// <summary>
    /// Properties panel content for the Eyedropper. The tool has no size/hardness/
    /// opacity knobs of its own to expose - clicking the canvas samples a pixel and
    /// adopts it as the primary color (see <c>CanvasViewModel.SampleColor</c>) - so this
    /// is just a marker type whose view shows the shared color picker, which already
    /// reflects whatever was last sampled via its own preview swatch.
    /// </summary>
    public sealed class EyedropperToolOptionsViewModel
    {
    }
}
