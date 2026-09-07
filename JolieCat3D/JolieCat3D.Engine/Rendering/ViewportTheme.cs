using System.Windows.Media;
using JolieCat3D.Core.Numerics;
using JolieCat3D.Engine.Lighting;

namespace JolieCat3D.Engine.Rendering
{
    /// <summary>
    /// The "BlackCat/JolieCat" dark professional viewport preset - a background brush
    /// and a matching <see cref="LightingSettings"/>, both tuned together (a background
    /// this dark needs brighter ambient/key light than a white one would, or every
    /// primitive reads as a near-black silhouette against it). Deliberately separate
    /// from <see cref="LightingSettings.CreateDefault"/>, which stays a neutral, brand-
    /// agnostic default for any consumer of this library - this class is the one place
    /// JolieCat3D's own brand hex values live in <c>JolieCat3D.Engine</c>, so a future
    /// window wanting the same look doesn't need to redeclare them.
    /// </summary>
    public static class ViewportTheme
    {
        // Matches JolieCat.UI\App.xaml's own PanelBackgroundBrush/WindowBackgroundBrush -
        // the same two tones that app's canvas "desk" area (CanvasRenderer.OutsideDocumentColor)
        // and docked panels already use, so a 3D viewport in this same product family reads
        // as one more panel of the same dark UI rather than a mismatched white void.
        private static readonly Color PanelDark = Color.FromRgb(0x24, 0x20, 0x20);
        private static readonly Color WindowDark = Color.FromRgb(0x1A, 0x17, 0x16);

        /// <summary>
        /// A smooth dark gradient background for a <see cref="HelixToolkit.Wpf.HelixViewport3D"/> -
        /// slightly lighter charcoal at the top fading to the same near-black
        /// <c>WindowBackgroundBrush</c> tone at the bottom, rather than one flat fill,
        /// for a bit of depth without introducing any new brand color. Frozen: this is a
        /// shared, never-mutated brush, safe to assign to any number of viewports.
        /// </summary>
        public static Brush CreateDarkBackground()
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0.5, 0),
                EndPoint = new System.Windows.Point(0.5, 1),
            };
            brush.GradientStops.Add(new GradientStop(PanelDark, 0.0));
            brush.GradientStops.Add(new GradientStop(WindowDark, 1.0));
            brush.Freeze();
            return brush;
        }

        /// <summary>
        /// Ambient/directional lighting tuned to keep primitives crisp against
        /// <see cref="CreateDarkBackground"/> - both brighter than
        /// <see cref="LightingSettings.CreateDefault"/>'s own values, which were tuned
        /// for a light/neutral backdrop and would otherwise leave every surface reading
        /// as a dim, low-contrast silhouette against this much darker background. The
        /// directional light's color is a slightly warm near-white (a gentle "key light"
        /// tint, not a stark cold one) so it complements the warm gold/emerald accents
        /// <c>JolieCat3D.UI</c>'s own demo materials use rather than washing them out.
        /// </summary>
        public static LightingSettings CreateDarkThemeLighting() => new()
        {
            DirectionalLightColor = new Color4(0.95f, 0.92f, 0.85f),
            AmbientLightColor = new Color4(0.32f, 0.32f, 0.35f),
        };
    }
}
