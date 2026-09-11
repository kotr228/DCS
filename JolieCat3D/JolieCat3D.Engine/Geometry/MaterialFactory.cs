using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using JolieCat3D.Core.Caching;
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

        /// <summary>The current scene's own Image-Based Lighting tint - null (the
        /// default, matching every scene with no <c>Core.Scene.Scene3D.Environment</c>
        /// set) means every material's specular highlight is computed exactly as it
        /// always was, with no environment influence at all. Set by
        /// <c>Rendering.Scene3DRenderer</c> whenever the scene's own environment changes
        /// (via <c>Rendering.EnvironmentVisualFactory.TrySampleAverageColor</c>) - a
        /// plain static settable property, the same "static class doubling as a small
        /// piece of shared render-session state" shape this class's own
        /// <see cref="MetallicRoughnessAverageCache"/> already has.
        ///
        /// WPF's fixed-function <see cref="Model3D"/> pipeline has no real environment-
        /// reflection-mapping capability at all (the same structural limitation
        /// <see cref="ComputeSpecular"/>'s own remarks already disclose for Roughness/
        /// Metallic textures, and <c>CoreMaterial.NormalTexturePath</c>'s for bump
        /// mapping) - there is no way for a <see cref="SpecularMaterial"/> to actually
        /// sample a skybox. What THIS property enables instead is an honest, disclosed
        /// APPROXIMATION: a metallic, smooth (low-roughness) material's specular tint
        /// blends further toward this color, the same way a real mirror-like surface
        /// visibly picks up its surroundings' own dominant color even before you can make
        /// out a sharp reflection in it - see <see cref="ComputeSpecular"/>'s own blend
        /// formula for exactly how much.</summary>
        public static CoreColor4? EnvironmentTint { get; set; }

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
        /// (or, when <see cref="CoreMaterial.MetallicRoughnessTexturePath"/> is set, the
        /// AVERAGED roughness/metallic sampled from it instead - see
        /// <see cref="SampleAverageMetallicRoughness"/>'s own remarks on why an average,
        /// not a per-pixel sample, is the most this fixed-function pipeline can ever
        /// derive from a texture here) on top of <paramref name="material"/>'s own
        /// hand-authored <see cref="CoreMaterial.SpecularColor"/>/<see cref="CoreMaterial.SpecularPower"/> -
        /// WPF's <see cref="SpecularMaterial"/> has no roughness/metallic concept of its
        /// own to hand these to directly, so this is the approximation that stands in
        /// for one:
        /// <list type="bullet">
        /// <item><description>Metallic blends <see cref="CoreMaterial.SpecularColor"/>
        /// toward <see cref="CoreMaterial.DiffuseColor"/> - the standard metallic-workflow
        /// rule (a fully metallic surface's reflections are tinted by its own albedo; a
        /// fully dielectric one's are neutral).</description></item>
        /// <item><description>Roughness scales <see cref="CoreMaterial.SpecularPower"/>
        /// down toward (never quite reaching) zero - smoother is a tighter/brighter
        /// highlight (closer to the material's own authored power), rougher is
        /// broader/dimmer, without ever fully extinguishing it (a "fully rough" surface
        /// still shows *some* highlight, just a very broad, faint one) or letting
        /// <see cref="SpecularMaterial"/>'s own power argument hit an invalid
        /// non-positive value.</description></item>
        /// </list>
        /// At the default Roughness (0.5)/Metallic (0) with no texture set, every existing
        /// material (created before these properties existed) now renders with roughly
        /// half its previously-authored specular power, rather than pixel-identically - an
        /// accepted, disclosed consequence of Roughness actually affecting shading at
        /// all (a "no-op unless deliberately tuned away from default" formula would make
        /// it a cosmetic-only property that never does anything unasked), not a
        /// regression this project can visually confirm either way in this sandbox (see
        /// this repository's own environment notes on why WPF/Engine work here is
        /// compile-and-review-only).
        /// </summary>
        private static (CoreColor4 Color, double Power) ComputeSpecular(CoreMaterial material)
        {
            var roughnessScalar = material.Roughness;
            var metallicScalar = material.Metallic;

            // A texture path present OVERRIDES the scalar fields, exactly the same
            // "present means instead-of, not blended-with" convention DiffuseTexturePath
            // already established - not a blend of the two, and not applied at all if the
            // path is missing/unreadable (SampleAverageMetallicRoughness returns null,
            // same "broken texture reference degrades to the flat fallback, never
            // crashes" reasoning CreateDiffuseBrush already follows).
            if (!string.IsNullOrWhiteSpace(material.MetallicRoughnessTexturePath) &&
                SampleAverageMetallicRoughness(material.MetallicRoughnessTexturePath) is { } averaged)
            {
                roughnessScalar = averaged.Roughness;
                metallicScalar = averaged.Metallic;
            }

            var metallic = Math.Clamp(metallicScalar, 0f, 1f);
            var tintedColor = CoreColor4.Lerp(material.SpecularColor, material.DiffuseColor, metallic);

            // See EnvironmentTint's own remarks: only a fairly metallic AND fairly smooth
            // surface blends toward it at all (a rough or dielectric material shows
            // little to no clear "reflection" of its surroundings in reality either), and
            // not applied at all with no environment set - every scene without one keeps
            // rendering pixel-identically to before this feature existed.
            if (EnvironmentTint is { } environmentTint)
            {
                var roughnessForReflectivity = Math.Clamp(roughnessScalar, 0f, 1f);
                var reflectivity = Math.Clamp(metallic * (1f - roughnessForReflectivity), 0f, 1f);
                tintedColor = CoreColor4.Lerp(tintedColor, environmentTint, reflectivity);
            }

            var roughness = Math.Clamp(roughnessScalar, 0f, 1f);
            var power = Math.Max(1.0, material.SpecularPower * (1.0 - roughness) + roughness);

            return (tintedColor, power);
        }

        /// <summary>Cache of the last-computed average (roughness, metallic) pair for a
        /// given resolved file path, alongside the file's own <see cref="File.GetLastWriteTimeUtc(string)"/>
        /// at the time it was computed - keyed on the full path so a live file-watcher
        /// re-export (the same "the same path can legitimately change bytes underneath
        /// it while the app is running" scenario <see cref="CreateDiffuseBrush"/>'s own
        /// remarks describe for <see cref="CoreMaterial.DiffuseTexturePath"/>) is picked
        /// up on the next render rather than serving a stale average forever - this
        /// method is called on every single <see cref="Create(CoreMaterial?,ShadingMode)"/>
        /// (once per node, per render/refresh), so caching is what keeps repeatedly
        /// re-decoding and re-averaging the same unchanged texture off the hot path.
        /// Bounded (<see cref="BoundedCache{TKey,TValue}"/>), not a plain ever-growing
        /// Dictionary - a session that loads/hot-reloads many DIFFERENT texture files
        /// over its own lifetime (not just re-saving the same one) would otherwise never
        /// release the entries for textures nobody references anymore, a real,
        /// accumulating memory leak a bounded LRU cache doesn't have.</summary>
        private static readonly BoundedCache<string, (DateTime WriteTimeUtc, float AverageRoughness, float AverageMetallic)> MetallicRoughnessAverageCache = new(capacity: 64);

        /// <summary>
        /// The average roughness (image GREEN channel)/metallic (image BLUE channel)
        /// across <paramref name="path"/>'s own pixels, per the packed glTF
        /// metallicRoughness convention <see cref="CoreMaterial.MetallicRoughnessTexturePath"/>'s
        /// own remarks describe - null if the path is missing, unreadable, or not a valid
        /// image (degrades to the flat scalar fallback in <see cref="ComputeSpecular"/>,
        /// never throws). This is deliberately an AVERAGE, not a per-pixel sample: WPF's
        /// fixed-function <see cref="SpecularMaterial"/> takes one scalar
        /// <see cref="SpecularMaterial.SpecularPower"/> for the WHOLE material, with no
        /// per-pixel roughness/metallic hook of any kind to feed a real sample into (the
        /// same structural limitation <see cref="CoreMaterial.NormalTexturePath"/>'s own
        /// remarks describe for bump-mapping) - an average is the closest a
        /// once-per-material calculation can get to "the texture is actually
        /// influencing the shading" at all, and is exactly what the true per-pixel result
        /// in a real PBR renderer (the exported glTF, opened in a modern engine) averages
        /// out to look like from a distance on a roughly-uniform surface.
        ///
        /// Decodes at a small fixed 32x32 size rather than the image's own real
        /// resolution - an average is insensitive to resolution (a 32x32 downsample of
        /// even a 4K map still averages out to essentially the same result a full-size
        /// decode would), so this keeps the cost of computing it low regardless of how
        /// large the source texture actually is.
        /// </summary>
        private static (float Roughness, float Metallic)? SampleAverageMetallicRoughness(string path)
        {
            string fullPath;
            DateTime writeTimeUtc;
            try
            {
                fullPath = Path.GetFullPath(path);
                if (!File.Exists(fullPath)) return null;
                writeTimeUtc = File.GetLastWriteTimeUtc(fullPath);
            }
            catch (Exception)
            {
                return null;
            }

            if (MetallicRoughnessAverageCache.TryGetValue(fullPath, out var cached) && cached.WriteTimeUtc == writeTimeUtc)
                return (cached.AverageRoughness, cached.AverageMetallic);

            if (ImageAverageSampler.TryDecodeSmallBgra32(fullPath, out var width, out var height) is not { } pixels)
                return null; // missing/unreadable/invalid image - see CreateDiffuseBrush's own remarks on why this degrades rather than throws

            long roughnessSum = 0;
            long metallicSum = 0;
            var pixelCount = width * height;

            for (var i = 0; i < pixels.Length; i += 4)
            {
                // Bgra32's own byte order is [B, G, R, A] per pixel - the packed
                // glTF metallicRoughness convention puts metallic in the image's
                // BLUE channel (byte offset 0 here) and roughness in GREEN (offset 1).
                metallicSum += pixels[i];
                roughnessSum += pixels[i + 1];
            }

            var averageRoughness = roughnessSum / 255f / pixelCount;
            var averageMetallic = metallicSum / 255f / pixelCount;

            MetallicRoughnessAverageCache.Set(fullPath, (writeTimeUtc, averageRoughness, averageMetallic));
            return (averageRoughness, averageMetallic);
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
