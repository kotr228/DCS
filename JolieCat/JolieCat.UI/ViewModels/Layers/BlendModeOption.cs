using System;
using System.Collections.Generic;
using System.Linq;
using JolieCat.Shared.Enums;

namespace JolieCat.UI.ViewModels.Layers
{
    /// <summary>One entry in the Layers panel's Blend Mode dropdown: a <see cref="BlendMode"/>
    /// value paired with the human-readable name it should show, since "ColorDodge" etc.
    /// isn't fit to display as-is.</summary>
    public sealed record BlendModeOption(BlendMode Value, string DisplayName)
    {
        /// <summary>Every <see cref="BlendMode"/>, in enum declaration order, for the
        /// dropdown's ItemsSource (bound via <c>x:Static</c> - it never changes at
        /// runtime, so it's computed once rather than per <see cref="LayerViewModel"/>).</summary>
        public static IReadOnlyList<BlendModeOption> All { get; } = Enum.GetValues<BlendMode>()
            .Select(mode => new BlendModeOption(mode, GetDisplayName(mode)))
            .ToList();

        private static string GetDisplayName(BlendMode mode) => mode switch
        {
            BlendMode.Normal => "Normal",
            BlendMode.Multiply => "Multiply",
            BlendMode.Screen => "Screen",
            BlendMode.Overlay => "Overlay",
            BlendMode.Darken => "Darken",
            BlendMode.Lighten => "Lighten",
            BlendMode.ColorDodge => "Color Dodge",
            BlendMode.ColorBurn => "Color Burn",
            BlendMode.HardLight => "Hard Light",
            BlendMode.SoftLight => "Soft Light",
            BlendMode.Difference => "Difference",
            BlendMode.Exclusion => "Exclusion",
            _ => mode.ToString(),
        };
    }
}
