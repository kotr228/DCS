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
        /// <summary>Photoshop's order: normal, darkening, lightening, contrast, comparing, color.</summary>
        private static readonly BlendMode[] Order =
        {
            BlendMode.Normal, BlendMode.Dissolve,
            BlendMode.Darken, BlendMode.Multiply, BlendMode.ColorBurn, BlendMode.LinearBurn, BlendMode.DarkerColor,
            BlendMode.Lighten, BlendMode.Screen, BlendMode.ColorDodge, BlendMode.LinearDodge, BlendMode.LighterColor,
            BlendMode.Overlay, BlendMode.SoftLight, BlendMode.HardLight, BlendMode.VividLight, BlendMode.LinearLight, BlendMode.PinLight, BlendMode.HardMix,
            BlendMode.Difference, BlendMode.Exclusion, BlendMode.Subtract, BlendMode.Divide,
            BlendMode.Hue, BlendMode.Saturation, BlendMode.Color, BlendMode.Luminosity,
        };

        /// <summary>Every <see cref="BlendMode"/>, in enum declaration order, for the
        /// dropdown's ItemsSource (bound via <c>x:Static</c> - it never changes at
        /// runtime, so it's computed once rather than per <see cref="LayerViewModel"/>).</summary>
        public static IReadOnlyList<BlendModeOption> All { get; } = Order
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
            BlendMode.Hue => "Hue",
            BlendMode.Saturation => "Saturation",
            BlendMode.Color => "Color",
            BlendMode.Luminosity => "Luminosity",
            BlendMode.Dissolve => "Dissolve",
            BlendMode.LinearBurn => "Linear Burn",
            BlendMode.DarkerColor => "Darker Color",
            BlendMode.LinearDodge => "Linear Dodge (Add)",
            BlendMode.LighterColor => "Lighter Color",
            BlendMode.VividLight => "Vivid Light",
            BlendMode.LinearLight => "Linear Light",
            BlendMode.PinLight => "Pin Light",
            BlendMode.HardMix => "Hard Mix",
            BlendMode.Subtract => "Subtract",
            BlendMode.Divide => "Divide",
            _ => mode.ToString(),
        };
    }
}
