using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using JolieCat3D.Engine.Rendering;
using CoreColor4 = JolieCat3D.Core.Numerics.Color4;
using CoreMaterial = JolieCat3D.Core.Materials.Material;

namespace JolieCat3D.Engine.Geometry
{
    /// <summary>
    /// Converts a platform-agnostic <see cref="CoreMaterial"/> into a WPF
    /// <see cref="Material"/> - a <see cref="MaterialGroup"/> combining a
    /// <see cref="DiffuseMaterial"/> and (except in <see cref="ShadingMode.Solid"/>/
    /// <see cref="ShadingMode.Material"/>) a <see cref="SpecularMaterial"/>, WPF's own
    /// fixed-function equivalent of the diffuse+specular model <see cref="CoreMaterial"/>
    /// describes. Named <c>MaterialFactory</c> rather than reusing "Material" as a type
    /// name in this namespace specifically so <c>using</c>-ing both
    /// <c>JolieCat3D.Core.Materials</c> and <c>System.Windows.Media.Media3D</c> (both of
    /// which declare their own <c>Material</c> type) never becomes ambiguous by accident
    /// anywhere in <c>JolieCat3D.Engine</c>.
    /// </summary>
    public static class MaterialFactory
    {
        /// <summary>The flat, textureless gray every mesh renders as in
        /// <see cref="ShadingMode.Solid"/>, regardless of its own material - see
        /// <see cref="ShadingMode.Solid"/>'s own remarks.</summary>
        private static readonly Color NeutralSolidColor = Color.FromRgb(0xB0, 0xB0, 0xB0);

        /// <summary>A plain, flat mid-gray fallback - what <see cref="Create(CoreMaterial?)"/>
        /// returns for a null material, matching <see cref="CoreMaterial.CreateDefault"/>'s
        /// own look without needing a live Core instance to build it from.</summary>
        public static Material CreateDefault() => Create(null);

        /// <summary>The <see cref="ShadingMode.Rendered"/> (full-quality, this project's
        /// long-standing default) material for <paramref name="material"/> - see the
        /// <see cref="Create(CoreMaterial?,ShadingMode)"/> overload for every other
        /// shading mode.</summary>
        public static Material Create(CoreMaterial? material) => Create(material, ShadingMode.Rendered);

        /// <summary>
        /// Builds <paramref name="material"/>'s WPF material for <paramref name="mode"/> -
        /// <see cref="ShadingMode.Solid"/> ignores <paramref name="material"/> entirely
        /// (a fixed neutral gray, no texture); <see cref="ShadingMode.Material"/> and
        /// <see cref="ShadingMode.Rendered"/> both use its real diffuse color/texture,
        /// differing only in whether a specular highlight layer is added at all (see
        /// this method's own <see cref="ShadingMode"/> cases). <see cref="ShadingMode.Wireframe"/>
        /// is not handled here at all - <see cref="Scene3DRenderer"/> skips building any
        /// filled geometry for it in the first place (see <see cref="WireframeVisualFactory"/>),
        /// so this method is never even called for it.
        /// </summary>
        public static Material Create(CoreMaterial? material, ShadingMode mode)
        {
            material ??= CoreMaterial.CreateDefault();

            if (mode == ShadingMode.Solid)
            {
                var neutral = new MaterialGroup();
                neutral.Children.Add(new DiffuseMaterial(new SolidColorBrush(NeutralSolidColor)));
                neutral.Freeze();
                return neutral;
            }

            var group = new MaterialGroup();
            group.Children.Add(new DiffuseMaterial(CreateDiffuseBrush(material)));

            if (mode == ShadingMode.Rendered)
            {
                var (specularColor, specularPower) = ComputeSpecular(material);

                // A specular contribution of pure black is indistinguishable from none
                // at all, so skip adding the SpecularMaterial entirely rather than pay
                // for a highlight calculation that can never actually show up.
                if (specularColor.R > 0f || specularColor.G > 0f || specularColor.B > 0f)
                {
                    var wpfSpecularColor = ToWpfColor(specularColor, 1f);
                    group.Children.Add(new SpecularMaterial(new SolidColorBrush(wpfSpecularColor), specularPower));
                }
            }
            // ShadingMode.Material: diffuse only, deliberately no SpecularMaterial layer
            // at all - see ShadingMode.Material's own remarks.

            group.Freeze();
            return group;
        }

        /// <summary>
        /// The effective specular color/power for <see cref="ShadingMode.Rendered"/>,
        /// factoring in <see cref="CoreMaterial.Roughness"/>/<see cref="CoreMaterial.Metallic"/>
        /// on top of <paramref name="material"/>'s own hand-authored
        /// <see cref="CoreMaterial.SpecularColor"/>/<see cref="CoreMaterial.SpecularPower"/> -
        /// WPF's <see cref="SpecularMaterial"/> has no roughness/metallic concept of its
        /// own to hand these to directly, so this is the approximation that stands in
        /// for one:
        /// <list type="bullet">
        /// <item><description><see cref="CoreMaterial.Metallic"/> blends
        /// <see cref="CoreMaterial.SpecularColor"/> toward <see cref="CoreMaterial.DiffuseColor"/>
        /// - the standard metallic-workflow rule (a fully metallic surface's
        /// reflections are tinted by its own albedo; a fully dielectric one's are
        /// neutral).</description></item>
        /// <item><description><see cref="CoreMaterial.Roughness"/> scales
        /// <see cref="CoreMaterial.SpecularPower"/> down toward (never quite reaching)
        /// zero - smoother is a tighter/brighter highlight (closer to the material's own
        /// authored power), rougher is broader/dimmer, without ever fully extinguishing
        /// it (a "fully rough" surface still shows *some* highlight, just a very broad,
        /// faint one) or letting <see cref="SpecularMaterial"/>'s own power argument hit
        /// an invalid non-positive value.</description></item>
        /// </list>
        /// At the default Roughness (0.5)/Metallic (0), every existing material (created
        /// before these two properties existed) now renders with roughly half its
        /// previously-authored specular power, rather than pixel-identically - an
        /// accepted, disclosed consequence of Roughness actually affecting shading at
        /// all (a "no-op unless deliberately tuned away from default" formula would make
        /// it a cosmetic-only property that never does anything unasked), not a
        /// regression this project can visually confirm either way in this sandbox (see
        /// this repository's own environment notes on why WPF/Engine work here is
        /// compile-and-review-only).
        /// </summary>
        private static (CoreColor4 Color, double Power) ComputeSpecular(CoreMaterial material)
        {
            var metallic = Math.Clamp(material.Metallic, 0f, 1f);
            var tintedColor = CoreColor4.Lerp(material.SpecularColor, material.DiffuseColor, metallic);

            var roughness = Math.Clamp(material.Roughness, 0f, 1f);
            var power = Math.Max(1.0, material.SpecularPower * (1.0 - roughness) + roughness);

            return (tintedColor, power);
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
                    // WPF's own per-Uri decoded-frame cache would otherwise keep serving
                    // the bytes a texture file had the FIRST time this exact path was
                    // ever loaded, even from a brand new BitmapImage instance - which
                    // would silently defeat live refresh (JolieCat3D.Service.Interop's
                    // file-watching bridge re-writing the same path after the 2D editor
                    // re-exports it) the moment a path got reused. Every reload here
                    // always reflects whatever bytes are on disk right now.
                    bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
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

        private static Color ToWpfColor(CoreColor4 color, float opacity) => Color.FromScRgb(
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
