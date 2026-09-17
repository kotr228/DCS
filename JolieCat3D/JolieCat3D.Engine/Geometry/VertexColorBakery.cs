using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Materials;
using JolieCat3D.Core.Numerics;
using CoreMaterial = JolieCat3D.Core.Materials.Material;
using CoreMesh = JolieCat3D.Core.Geometry.Mesh;

namespace JolieCat3D.Engine.Geometry
{
    /// <summary>
    /// The WPF-side half of <see cref="VertexColorBaker"/> - loading a material's own
    /// diffuse texture into the plain RGBA8 byte buffer that Core's (platform-agnostic)
    /// baking algorithm needs, and turning its own baked result back into a real WPF
    /// <see cref="ImageSource"/> a <see cref="DiffuseMaterial"/> can actually use.
    /// <see cref="Geometry.SceneGraphBuilder"/> is the one caller, for every material
    /// group (see <see cref="MeshGeometryFactory.CreateGroups"/>) whose own triangles
    /// touch at least one non-default-white <see cref="Vertex.Color"/> - the task's own
    /// "multiply the Albedo/Diffuse texture by the Vertex Colors" ask, applied to the
    /// DEFAULT render path (every shading mode), not only a dedicated paint-mode
    /// overlay the way Weight Paint mode's own heat-map is.
    /// </summary>
    internal static class VertexColorBakery
    {
        /// <summary>True if ANY of <paramref name="triangles"/>' own vertices carries a
        /// non-default (non-White) color - the cheap check <see cref="Geometry.SceneGraphBuilder"/>
        /// uses to skip baking entirely for the overwhelming majority of meshes (every
        /// one never touched by Vertex Paint), so this feature costs nothing at all
        /// until a mesh actually uses it.</summary>
        public static bool HasPaintedVertexColor(CoreMesh mesh, IReadOnlyList<Face> triangles)
        {
            var seen = new HashSet<int>();
            foreach (var face in triangles) { seen.Add(face.A); seen.Add(face.B); seen.Add(face.C); }

            const float tolerance = 1f / 512f; // well under one 8-bit quantization step
            foreach (var index in seen)
            {
                var color = mesh.Vertices[index].Color;
                if (MathF.Abs(color.R - 1f) > tolerance || MathF.Abs(color.G - 1f) > tolerance ||
                    MathF.Abs(color.B - 1f) > tolerance || MathF.Abs(color.A - 1f) > tolerance)
                    return true;
            }

            return false;
        }

        /// <summary>Bakes <paramref name="triangles"/>' own vertex colors (multiplied
        /// by <paramref name="material"/>'s own diffuse texture, if any - respecting
        /// its <see cref="CoreMaterial.DiffuseTextureOffset"/>/<see cref="CoreMaterial.DiffuseTextureScale"/>
        /// sub-rect the exact same way <see cref="MaterialFactory"/>'s own
        /// <c>ImageBrush.Viewbox</c> already does for ordinary rendering) into a real,
        /// ready-to-use <see cref="ImageSource"/> plus the remapped <see cref="CoreMesh"/>
        /// whose own UVs sample it - see <see cref="VertexColorBaker"/>'s own remarks
        /// for the actual baking algorithm.</summary>
        public static (ImageSource Texture, CoreMesh RemappedMesh) Bake(CoreMesh mesh, CoreMaterial? material, IReadOnlyList<Face> triangles)
        {
            var (sourcePixels, sourceWidth, sourceHeight) = TryExtractSourcePixels(material);

            var samplingMesh = mesh;
            if (material is { } m && (m.DiffuseTextureOffset != default || m.DiffuseTextureScale != System.Numerics.Vector2.One))
            {
                samplingMesh = mesh.Clone();
                for (var i = 0; i < samplingMesh.Vertices.Count; i++)
                {
                    var uv = samplingMesh.Vertices[i].UV;
                    samplingMesh.SetVertexUV(i, m.DiffuseTextureOffset + uv * m.DiffuseTextureScale);
                }
            }

            var (pixels, width, height, remappedMesh) = VertexColorBaker.Bake(samplingMesh, sourcePixels, sourceWidth, sourceHeight, triangles);
            return (BuildBitmap(pixels, width, height), remappedMesh);
        }

        /// <summary>Loads <paramref name="material"/>'s own diffuse texture file (if
        /// any) into a plain top-left-origin RGBA8 buffer - the SAME "OnLoad +
        /// IgnoreImageCache, fall through to null on any failure" loading convention
        /// <see cref="MaterialFactory"/>'s own <c>CreateDiffuseBrush</c> already
        /// established, so a missing/corrupt texture file never throws here either, it
        /// just bakes as if there were no texture at all (flat white, i.e. vertex
        /// color alone).</summary>
        private static (byte[]? Pixels, int Width, int Height) TryExtractSourcePixels(CoreMaterial? material)
        {
            if (material is null || string.IsNullOrWhiteSpace(material.DiffuseTexturePath) || !File.Exists(material.DiffuseTexturePath))
                return (null, 0, 0);

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.UriSource = new Uri(Path.GetFullPath(material.DiffuseTexturePath), UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();

                // WPF's own PixelFormats has no plain R,G,B,A 32bpp format - only
                // B,G,R,A ones (Bgra32/Pbgra32) - so this converts to Bgra32 and swaps
                // R/B per pixel on the way OUT, matching the R,G,B,A byte order
                // VertexColorBaker/Color4.FromBytes expect everywhere else.
                var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                var width = converted.PixelWidth;
                var height = converted.PixelHeight;
                var stride = width * 4;
                var bgraPixels = new byte[height * stride];
                converted.CopyPixels(bgraPixels, stride, 0);

                var rgbaPixels = new byte[bgraPixels.Length];
                for (var i = 0; i < bgraPixels.Length; i += 4)
                {
                    rgbaPixels[i] = bgraPixels[i + 2];
                    rgbaPixels[i + 1] = bgraPixels[i + 1];
                    rgbaPixels[i + 2] = bgraPixels[i];
                    rgbaPixels[i + 3] = bgraPixels[i + 3];
                }
                return (rgbaPixels, width, height);
            }
            catch (Exception)
            {
                return (null, 0, 0);
            }
        }

        /// <summary>Wraps a baked RGBA8 buffer as a frozen, ready-to-render WPF
        /// <see cref="ImageSource"/> - <see cref="PixelFormats.Bgra32"/> (WPF's own
        /// native 32bpp format) expects B,G,R,A byte order, the reverse of the R,G,B,A
        /// <see cref="VertexColorBaker"/> itself produces, so the channel swap happens
        /// once here, on the way OUT, entirely on this WPF-facing side of the
        /// Core/Engine boundary.</summary>
        private static ImageSource BuildBitmap(byte[] rgbaPixels, int width, int height)
        {
            var bgraPixels = new byte[rgbaPixels.Length];
            for (var i = 0; i < rgbaPixels.Length; i += 4)
            {
                bgraPixels[i] = rgbaPixels[i + 2];
                bgraPixels[i + 1] = rgbaPixels[i + 1];
                bgraPixels[i + 2] = rgbaPixels[i];
                bgraPixels[i + 3] = rgbaPixels[i + 3];
            }

            var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            bitmap.WritePixels(new Int32Rect(0, 0, width, height), bgraPixels, width * 4, 0);
            bitmap.Freeze();
            return bitmap;
        }
    }
}
