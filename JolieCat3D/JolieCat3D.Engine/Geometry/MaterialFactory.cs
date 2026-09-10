using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

            var group = new MaterialGroup();
            group.Children.Add(new DiffuseMaterial(CreateDiffuseBrush(material)));

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

        /// <summary>An <see cref="ImageBrush"/> sampling <see cref="CoreMaterial.DiffuseTexturePath"/>'s
        /// own <see cref="CoreMaterial.DiffuseTextureOffset"/>/<see cref="CoreMaterial.DiffuseTextureScale"/>
        /// sub-rectangle (via <see cref="ImageBrush.Viewbox"/> with
        /// <see cref="BrushMappingMode.RelativeToBoundingBox"/> - exactly the "shared
        /// atlas, many named sub-rects" shape those two properties document, so a
        /// sprite-sheet cell built by <c>JolieCat3D.Service.Interop.TwoDAssetBridge</c>
        /// renders as just that one cell, not the whole sheet) when a texture path is
        /// set - the plain flat <see cref="SolidColorBrush"/> this project has always
        /// used otherwise, for full backward compatibility with every material that has
        /// no texture at all. A texture path that's missing, unreadable, or not a valid
        /// image falls back to the flat color too, rather than throwing and taking the
        /// whole scene's render down with it - a broken texture reference is a data
        /// problem to recover from visibly (the mesh still renders, just untextured),
        /// not a reason to crash.</summary>
        private static Brush CreateDiffuseBrush(CoreMaterial material)
        {
            if (!string.IsNullOrWhiteSpace(material.DiffuseTexturePath) && File.Exists(material.DiffuseTexturePath))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(Path.GetFullPath(material.DiffuseTexturePath), UriKind.Absolute);
                    bitmap.EndInit();
                    bitmap.Freeze();

                    var offset = material.DiffuseTextureOffset;
                    var scale = material.DiffuseTextureScale;

                    var brush = new ImageBrush(bitmap)
                    {
                        Viewbox = new Rect(offset.X, offset.Y, scale.X, scale.Y),
                        ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
                        TileMode = TileMode.None,
                        Stretch = Stretch.Fill,
                        Opacity = Clamp01(material.Opacity),
                    };
                    brush.Freeze();
                    return brush;
                }
                catch (Exception)
                {
                    // Falls through to the flat-color brush below - see this method's
                    // own remarks on why a broken texture reference shouldn't crash the
                    // render.
                }
            }

            return new SolidColorBrush(ToWpfColor(material.DiffuseColor, material.Opacity));
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
