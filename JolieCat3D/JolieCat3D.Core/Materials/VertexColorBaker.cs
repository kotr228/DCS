using System.Numerics;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Numerics;

namespace JolieCat3D.Core.Materials
{
    /// <summary>
    /// Bakes <see cref="Vertex.Color"/> (multiplied by the material's own diffuse
    /// texture, sampled at each vertex's own UV, if one exists) into a small generated
    /// RGBA8 texture atlas, remapping a throwaway copy's own UVs to sample it - the
    /// "bake vertex colors into a generated texture" workaround
    /// <c>JolieCat3D.Engine.Geometry.MeshGeometryFactory.Create</c>'s own doc comment
    /// names as the one available option, since WPF's fixed-function 3D pipeline has
    /// no native per-vertex color channel at all (see that comment's own remarks).
    /// Unlike <see cref="Skinning.WeightVisualization.BuildHeatmapMesh"/> (a single
    /// scalar mapped through a 1D gradient - Weight Paint mode's own, simpler need),
    /// vertex color is 3 independent RGB channels, so this needs an actual 2D texture,
    /// not a 1D brush - built here, applied in <c>JolieCat3D.Engine</c>'s own DEFAULT
    /// render path (not only a dedicated paint-mode overlay), so a vertex-painted mesh
    /// renders correctly tinted everywhere, all the time.
    ///
    /// Each render TRIANGLE (not vertex - see <see cref="Geometry.Mesh.SplitVertsPerFace"/>'s
    /// own remarks on why one mesh corner can't share a UV with another that needs a
    /// different bake) gets its own private 2x2 texel block, its 3 real corners placed
    /// at exactly 3 of those 4 texels' own CENTERS (A at (0,0), B at (1,0), C at (0,1))
    /// and the 4th, otherwise-unused corner set to the exact value <c>B + C - A</c> -
    /// a deliberately chosen identity, not an arbitrary filler: standard bilinear
    /// interpolation of a unit square, `(1-u)(1-v)P00 + u(1-v)P10 + (1-u)vP01 + uvP11`,
    /// becomes EXACTLY the barycentric blend `(1-u-v)A + uB + vC` for every (u,v) in
    /// the valid triangle (u+v&lt;=1) once P11 = B+C-A (verified by direct algebraic
    /// substitution, and independently confirmed by simulating WPF's own bilinear
    /// sampling formula in a standalone scratch script before this was relied on here) -
    /// so ordinary GPU/WPF bilinear texture sampling, needing no shader of its own,
    /// reproduces a mathematically EXACT continuous per-pixel vertex-color blend
    /// across each triangle, not merely a flat per-triangle average. Because every
    /// triangle's own 3 real UV samples never stray outside its own dedicated 2x2
    /// block (bilinear filtering only ever reaches as far as a sample's own 4
    /// immediately-surrounding texels), no two triangles' blocks can ever bleed into
    /// each other - no padding between blocks is needed at all.
    ///
    /// Known, disclosed simplification: a material's own diffuse TEXTURE is sampled
    /// only at each triangle's 3 corner UVs, not at every interior pixel - fine detail
    /// within a texture that varies faster than the mesh's own triangle density will
    /// therefore only be approximated (blended from its 3 corner samples), not exactly
    /// reproduced.
    ///
    /// A second, more subtle disclosed limitation, specific to vertex COLOR itself:
    /// the exact bilinear-equals-barycentric identity above requires storing the 4th
    /// corner's own literal, possibly OUT-OF-[0,1]-RANGE value <c>B + C - A</c> (e.g. a
    /// bright A with two much darker B/C easily pushes a channel negative) - but the
    /// atlas is an ordinary 8-bit-per-channel buffer, so that value is clamped back
    /// into range before it's ever written. This never affects the 3 real corners
    /// themselves, nor the two edges directly touching A (A-B and A-C - the 4th
    /// corner's own bilinear weight, <c>u*v</c>, is exactly zero along both, since one
    /// of u/v is itself zero there); it can introduce a small, BOUNDED error - never
    /// unbounded or NaN, always still a weighted blend of in-range colors - along the
    /// third edge (B-C) and in the mesh's strict interior, exactly where a channel's
    /// own B+C-A would have needed clamping. A full-precision (floating-point) atlas
    /// would remove this entirely, at the cost of real extra complexity integrating an
    /// HDR pixel format into WPF's own <c>ImageBrush</c>/<c>DiffuseMaterial</c> pipeline -
    /// judged not worth it for what is already a purely visual, sub-pixel-scale
    /// approximation on top of an approximation (texture sampling) this method already
    /// accepts above.
    /// </summary>
    public static class VertexColorBaker
    {
        /// <summary>Bakes <paramref name="mesh"/>'s current vertex colors (and, if
        /// given, <paramref name="sourcePixels"/> - a top-left-origin RGBA8 buffer,
        /// <paramref name="sourceWidth"/>*<paramref name="sourceHeight"/>*4 bytes, the
        /// SAME row-major/top-left convention <see cref="Vertex.UV"/>'s own (0,0)-at-
        /// top-left already implies for a WPF <c>MeshGeometry3D</c>) into a new atlas
        /// texture, returning it alongside a REMAPPED copy of <paramref name="mesh"/>
        /// (never mutating the original) whose own UVs point into that atlas. A
        /// <paramref name="sourcePixels"/> of null treats the material as an untextured
        /// flat white surface (so the baked result is exactly the vertex colors alone).
        /// An empty input mesh returns a trivial 1x1 white pixel and an empty remapped
        /// mesh, never throwing. <paramref name="triangles"/> defaults to EVERY render
        /// triangle in <paramref name="mesh"/> (<see cref="Mesh.GetRenderFaces"/>) - a
        /// caller doing its own Multi-Material Support grouping (one distinct source
        /// texture per material) instead passes just the one material group's own
        /// triangle subset, baking each group into its own separate atlas.</summary>
        public static (byte[] Pixels, int Width, int Height, Mesh RemappedMesh) Bake(Mesh mesh, byte[]? sourcePixels, int sourceWidth, int sourceHeight, IReadOnlyList<Face>? triangles = null)
        {
            ArgumentNullException.ThrowIfNull(mesh);

            var remapped = new Mesh(mesh.Name) { Material = mesh.Material };
            triangles ??= mesh.GetRenderFaces().ToList();
            if (triangles.Count == 0) return (new byte[] { 255, 255, 255, 255 }, 1, 1, remapped);

            var columns = (int)Math.Ceiling(Math.Sqrt(triangles.Count));
            var rows = (int)Math.Ceiling((double)triangles.Count / columns);
            var width = columns * 2;
            var height = rows * 2;
            var pixels = new byte[width * height * 4];

            Color4 SampleSource(Vector2 uv)
            {
                if (sourcePixels is null || sourceWidth <= 0 || sourceHeight <= 0) return Color4.White;

                var u = uv.X - MathF.Floor(uv.X);
                var v = uv.Y - MathF.Floor(uv.Y);
                var px = Math.Clamp((int)(u * sourceWidth), 0, sourceWidth - 1);
                var py = Math.Clamp((int)(v * sourceHeight), 0, sourceHeight - 1);
                var offset = (py * sourceWidth + px) * 4;
                return Color4.FromBytes(sourcePixels[offset], sourcePixels[offset + 1], sourcePixels[offset + 2], sourcePixels[offset + 3]);
            }

            void WriteTexel(int x, int y, Color4 color)
            {
                var offset = (y * width + x) * 4;
                pixels[offset] = (byte)(Math.Clamp(color.R, 0f, 1f) * 255f);
                pixels[offset + 1] = (byte)(Math.Clamp(color.G, 0f, 1f) * 255f);
                pixels[offset + 2] = (byte)(Math.Clamp(color.B, 0f, 1f) * 255f);
                pixels[offset + 3] = (byte)(Math.Clamp(color.A, 0f, 1f) * 255f);
            }

            Vector2 TexelCenterUV(int x, int y) => new((x + 0.5f) / width, (y + 0.5f) / height);

            for (var i = 0; i < triangles.Count; i++)
            {
                var face = triangles[i];
                var va = mesh.Vertices[face.A];
                var vb = mesh.Vertices[face.B];
                var vc = mesh.Vertices[face.C];

                var colorA = va.Color * SampleSource(va.UV);
                var colorB = vb.Color * SampleSource(vb.UV);
                var colorC = vc.Color * SampleSource(vc.UV);
                var colorD = new Color4(
                    Math.Clamp(colorB.R + colorC.R - colorA.R, 0f, 1f),
                    Math.Clamp(colorB.G + colorC.G - colorA.G, 0f, 1f),
                    Math.Clamp(colorB.B + colorC.B - colorA.B, 0f, 1f),
                    Math.Clamp(colorB.A + colorC.A - colorA.A, 0f, 1f));

                var blockX = (i % columns) * 2;
                var blockY = (i / columns) * 2;

                WriteTexel(blockX, blockY, colorA);
                WriteTexel(blockX + 1, blockY, colorB);
                WriteTexel(blockX, blockY + 1, colorC);
                WriteTexel(blockX + 1, blockY + 1, colorD);

                var newA = remapped.AddVertex(va.WithUV(TexelCenterUV(blockX, blockY)));
                var newB = remapped.AddVertex(vb.WithUV(TexelCenterUV(blockX + 1, blockY)));
                var newC = remapped.AddVertex(vc.WithUV(TexelCenterUV(blockX, blockY + 1)));
                remapped.AddTriangle(newA, newB, newC);
            }

            return (pixels, width, height, remapped);
        }
    }
}
