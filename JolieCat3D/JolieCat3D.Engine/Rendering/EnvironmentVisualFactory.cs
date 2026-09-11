using System.IO;
using System.Numerics;
using HelixToolkit.Wpf;
using JolieCat3D.Core.Caching;
using JolieCat3D.Engine.Geometry;
using CoreEnvironment = JolieCat3D.Core.Scene.EnvironmentSettings;

namespace JolieCat3D.Engine.Rendering
{
    /// <summary>
    /// Turns a <see cref="CoreEnvironment"/> into the two things
    /// <see cref="Scene3DRenderer"/> actually needs from it: a skybox visual (see
    /// <see cref="CreateSkybox"/>) and an average color to feed
    /// <see cref="MaterialFactory.EnvironmentTint"/> (see
    /// <see cref="TrySampleAverageColor"/>) - "Pure PBR materials require an environment
    /// to reflect" the task's own words ask for, within what this project's actual WPF
    /// fixed-function rendering pipeline can really do (see
    /// <see cref="MaterialFactory.EnvironmentTint"/>'s own remarks on why a true,
    /// per-pixel reflection-mapped IBL is not one of those things).
    ///
    /// Both halves independently re-derive the same 6 face file paths from
    /// <see cref="CoreEnvironment.SkyboxSource"/>, following the EXACT convention
    /// <see cref="PanoramaCube3D.Source"/> itself documents (a directory - implying
    /// filename prefix "cube" - or a literal shared prefix directly), rather than one
    /// resolving through the other: <see cref="PanoramaCube3D"/> resolves its own
    /// <see cref="PanoramaCube3D.Source"/> internally with no way for a caller to read
    /// back the 6 paths it actually used, so <see cref="TrySampleAverageColor"/> has no
    /// choice but its own independent (but convention-identical) resolution to load the
    /// same 6 images for averaging.
    /// </summary>
    public static class EnvironmentVisualFactory
    {
        /// <summary>Front/back/left/right/up/down, in exactly this order and with
        /// exactly these suffixes - <see cref="PanoramaCube3D.Source"/>'s own documented
        /// convention.</summary>
        private static readonly string[] FaceSuffixes = { "_f", "_b", "_l", "_r", "_u", "_d" };

        private static readonly string[] SupportedExtensions = { ".jpg", ".jpeg", ".png", ".bmp" };

        /// <summary>Cache of the last-computed average color for a given
        /// <see cref="CoreEnvironment.SkyboxSource"/> string, alongside the latest of its
        /// own 6 face files' <see cref="File.GetLastWriteTimeUtc(string)"/> at the time it
        /// was computed - the same "keyed on a write time so a hot-reloaded skybox face
        /// is picked up, bounded so a session that tries many different skyboxes over its
        /// own lifetime doesn't leak" reasoning <see cref="MaterialFactory.MetallicRoughnessAverageCache"/>
        /// already established.</summary>
        private static readonly BoundedCache<string, (DateTime NewestWriteTimeUtc, Vector3 AverageColor)> AverageColorCache = new(capacity: 16);

        /// <summary>The skybox visual for <paramref name="environment"/> - null (nothing
        /// to add to the viewport at all) for a null <paramref name="environment"/>, an
        /// unset/blank <see cref="CoreEnvironment.SkyboxSource"/>, or one whose 6 face
        /// files don't actually all resolve (rather than handing
        /// <see cref="PanoramaCube3D"/> a source it would just silently fail to render
        /// anything useful from anyway - see <see cref="TryResolveFacePaths"/>'s own "all
        /// or nothing" contract).</summary>
        public static PanoramaCube3D? CreateSkybox(CoreEnvironment? environment)
        {
            if (string.IsNullOrWhiteSpace(environment?.SkyboxSource)) return null;
            if (!TryResolveFacePaths(environment.SkyboxSource, out _)) return null;

            return new PanoramaCube3D { Source = environment.SkyboxSource };
        }

        /// <summary>The average color across all 6 of <paramref name="environment"/>'s
        /// own resolved skybox faces (each individually downsampled/averaged - see
        /// <see cref="ImageAverageSampler"/> - then averaged again across the 6) - false
        /// (with <paramref name="averageColor"/> left at its default) for a null
        /// <paramref name="environment"/>, an unset <see cref="CoreEnvironment.SkyboxSource"/>,
        /// an unresolvable one, or one where every single face somehow failed to decode.
        /// <c>Rendering.Scene3DRenderer</c> feeds this straight into
        /// <see cref="MaterialFactory.EnvironmentTint"/> whenever the scene's own
        /// environment changes.</summary>
        public static bool TrySampleAverageColor(CoreEnvironment? environment, out Vector3 averageColor)
        {
            averageColor = default;

            if (string.IsNullOrWhiteSpace(environment?.SkyboxSource)) return false;
            if (!TryResolveFacePaths(environment.SkyboxSource, out var facePaths)) return false;

            DateTime newestWriteTimeUtc;
            try
            {
                newestWriteTimeUtc = facePaths.Max(File.GetLastWriteTimeUtc);
            }
            catch (Exception)
            {
                return false;
            }

            if (AverageColorCache.TryGetValue(environment.SkyboxSource, out var cached) && cached.NewestWriteTimeUtc == newestWriteTimeUtc)
            {
                averageColor = cached.AverageColor;
                return true;
            }

            var sum = Vector3.Zero;
            var sampledFaces = 0;

            foreach (var facePath in facePaths)
            {
                if (ImageAverageSampler.TryDecodeSmallBgra32(facePath, out var width, out var height) is not { } pixels) continue;

                long r = 0, g = 0, b = 0;
                var pixelCount = width * height;
                for (var i = 0; i < pixels.Length; i += 4)
                {
                    b += pixels[i];
                    g += pixels[i + 1];
                    r += pixels[i + 2];
                }

                sum += new Vector3(r / 255f / pixelCount, g / 255f / pixelCount, b / 255f / pixelCount);
                sampledFaces++;
            }

            if (sampledFaces == 0) return false;

            averageColor = sum / sampledFaces;
            AverageColorCache.Set(environment.SkyboxSource, (newestWriteTimeUtc, averageColor));
            return true;
        }

        /// <summary>Resolves <paramref name="source"/> (a directory or a literal file
        /// prefix - see <see cref="PanoramaCube3D.Source"/>'s own documented convention)
        /// into its own 6 concrete face file paths, trying each of
        /// <see cref="SupportedExtensions"/> in turn per face - false (with
        /// <paramref name="facePaths"/> left empty) unless ALL 6 faces resolve to an
        /// actually-existing file; a partially-resolvable set (5 faces present, 1
        /// missing) is treated the same as none at all, since a real cube-mapped skybox
        /// with one face missing isn't a usable skybox, just a broken one.</summary>
        private static bool TryResolveFacePaths(string source, out string[] facePaths)
        {
            facePaths = Array.Empty<string>();

            string prefix;
            try
            {
                prefix = Directory.Exists(source) ? Path.Combine(source, "cube") : source;
            }
            catch (Exception)
            {
                return false;
            }

            var resolved = new string[FaceSuffixes.Length];
            for (var i = 0; i < FaceSuffixes.Length; i++)
            {
                var match = SupportedExtensions
                    .Select(extension => prefix + FaceSuffixes[i] + extension)
                    .FirstOrDefault(File.Exists);

                if (match is null) return false;
                resolved[i] = match;
            }

            facePaths = resolved;
            return true;
        }
    }
}
