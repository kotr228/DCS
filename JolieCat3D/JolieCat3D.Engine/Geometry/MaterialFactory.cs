using System.Windows.Media;
using System.Windows.Media.Media3D;
using CoreMaterial = JolieCat3D.Core.Materials.Material;

namespace JolieCat3D.Engine.Geometry
{
    /// <summary>
    /// Converts a platform-agnostic <see cref="CoreMaterial"/> into a WPF
    /// <see cref="Material"/> - a <see cref="MaterialGroup"/> combining a
    /// <see cref="DiffuseMaterial"/> and a <see cref="SpecularMaterial"/>, WPF's own
    /// fixed-function equivalent of the diffuse+specular model <see cref="CoreMaterial"/>
    /// describes. Named <c>MaterialFactory</c> rather than reusing "Material" as a type
    /// name in this namespace specifically so <c>using</c>-ing both
    /// <c>JolieCat3D.Core.Materials</c> and <c>System.Windows.Media.Media3D</c> (both of
    /// which declare their own <c>Material</c> type) never becomes ambiguous by accident
    /// anywhere in <c>JolieCat3D.Engine</c>.
    /// </summary>
    public static class MaterialFactory
    {
        /// <summary>A plain, flat mid-gray fallback - what <see cref="Create"/> returns
        /// for a null <paramref name="material"/>, matching <see cref="CoreMaterial.CreateDefault"/>'s
        /// own look without needing a live Core instance to build it from.</summary>
        public static Material CreateDefault() => Create(null);

        public static Material Create(CoreMaterial? material)
        {
            material ??= CoreMaterial.CreateDefault();

            var diffuseColor = ToWpfColor(material.DiffuseColor, material.Opacity);
            var group = new MaterialGroup();
            group.Children.Add(new DiffuseMaterial(new SolidColorBrush(diffuseColor)));

            // A specular contribution of pure black is indistinguishable from none at
            // all, so skip adding the SpecularMaterial entirely rather than pay for a
            // highlight calculation that can never actually show up.
            if (material.SpecularColor.R > 0f || material.SpecularColor.G > 0f || material.SpecularColor.B > 0f)
            {
                var specularColor = ToWpfColor(material.SpecularColor, 1f);
                group.Children.Add(new SpecularMaterial(new SolidColorBrush(specularColor), material.SpecularPower));
            }

            group.Freeze();
            return group;
        }

        private static Color ToWpfColor(Core.Numerics.Color4 color, float opacity) => Color.FromScRgb(
            Clamp01(color.A * opacity),
            Clamp01(color.R),
            Clamp01(color.G),
            Clamp01(color.B));

        // Color.FromScRgb doesn't clamp its own arguments - an out-of-0-1-range channel
        // (a material tinted brighter than white, an Opacity fed a stray value outside
        // 0-1) would otherwise carry through as a technically-valid but likely
        // nonsensical scRGB color instead of a safe, visibly-clipped one.
        private static float Clamp01(float value) => System.Math.Clamp(value, 0f, 1f);
    }
}
