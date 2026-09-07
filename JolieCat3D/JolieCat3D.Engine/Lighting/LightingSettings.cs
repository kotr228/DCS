using System.Numerics;
using JolieCat3D.Core.Numerics;

namespace JolieCat3D.Engine.Lighting
{
    /// <summary>
    /// Configuration for <see cref="SceneLightingFactory.CreateStandardLighting"/>: one
    /// directional light (a distant, parallel-rayed source - the sun, in effect) plus one
    /// ambient light (a flat fill with no direction or falloff, so nothing ever renders
    /// pure black in shadow). Plain settings, not a WPF type itself, so
    /// <c>JolieCat3D.Engine</c>'s own consumers (<c>JolieCat3D.UI</c> included) can
    /// configure lighting without a `using System.Windows.Media.Media3D;` of their own.
    /// </summary>
    public sealed class LightingSettings
    {
        /// <summary>The direction light travels, i.e. pointing away from the source -
        /// WPF's own <c>DirectionalLight.Direction</c> convention. Defaults to a
        /// standard three-quarter key light: from above, slightly ahead and to one side.</summary>
        public Vector3 DirectionalLightDirection { get; set; } = Vector3.Normalize(new Vector3(-0.5f, -1f, -0.5f));

        public Color4 DirectionalLightColor { get; set; } = Color4.White;

        /// <summary>A dim, neutral fill so unlit surfaces are dark, not pitch black.</summary>
        public Color4 AmbientLightColor { get; set; } = new(0.25f, 0.25f, 0.25f);

        public static LightingSettings CreateDefault() => new();
    }
}
