namespace JolieCat.UI.ViewModels.ToolOptions
{
    /// <summary>
    /// Fallback shown for tools that do not yet have dedicated options - currently the
    /// navigation tools (Hand, Zoom, Canvas Rotate).
    /// </summary>
    public sealed class EmptyToolOptionsViewModel
    {
        public string Message { get; } = "This tool has no adjustable properties yet.";
    }
}
