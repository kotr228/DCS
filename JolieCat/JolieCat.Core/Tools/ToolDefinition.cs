using JolieCat.Shared.Enums;

namespace JolieCat.Core.Tools
{
    /// <summary>
    /// Static metadata describing one entry in the toolbox: its identity, the palette
    /// section it belongs to, and how it is presented in the Tools panel. Separate from
    /// <see cref="ToolType"/> itself so the UI has display text/shortcuts to bind to
    /// without hard-coding them into the pure Shared enum.
    /// </summary>
    /// <param name="Type">The tool this entry describes.</param>
    /// <param name="Category">The toolbox section it is grouped under.</param>
    /// <param name="DisplayName">Full name shown in the Tools panel and Properties header.</param>
    /// <param name="Glyph">Short (2-letter) badge shown in place of a real icon asset.</param>
    /// <param name="Shortcut">Suggested keyboard shortcut; informational only until
    /// keyboard routing is implemented.</param>
    public sealed record ToolDefinition(ToolType Type, ToolCategory Category, string DisplayName, string Glyph, string? Shortcut = null);
}
