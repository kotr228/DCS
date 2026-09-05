using JolieCat.Shared.Enums;

namespace JolieCat.Core.Tools
{
    /// <summary>
    /// Static metadata describing one entry in the toolbox: its identity, the palette
    /// section it belongs to, and how it is presented in the Tools panel. Separate from
    /// <see cref="ToolType"/> itself so the UI has display text/icon/shortcuts to bind to
    /// without hard-coding them into the pure Shared enum.
    /// </summary>
    /// <param name="Type">The tool this entry describes.</param>
    /// <param name="Category">The toolbox section it is grouped under.</param>
    /// <param name="DisplayName">Full name shown in the Tools panel and Properties header.</param>
    /// <param name="IconData">
    /// WPF path mini-language geometry, normalized to a 24x24 box. Assigned straight to a
    /// <see cref="System.Windows.Shapes.Path.Data"/> binding via WPF's built-in
    /// <c>GeometryConverter</c> - no icon font or image asset needed. The mini-language's
    /// default EvenOdd fill rule is relied on by several icons (the marquees, the
    /// magnifying glass, canvas-rotate) that are "ring/frame" shapes built from a solid
    /// subpath minus an inset one, which only reads as a hole under EvenOdd.
    /// </param>
    /// <param name="Shortcut">Suggested keyboard shortcut; informational only until
    /// keyboard routing is implemented.</param>
    public sealed record ToolDefinition(ToolType Type, ToolCategory Category, string DisplayName, string IconData, string? Shortcut = null);
}
