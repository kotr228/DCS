using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using HelixToolkit.Wpf;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Materials;
using JolieCat3D.Core.Modifiers;
using JolieCat3D.Core.Numerics;
using JolieCat3D.Core.Skinning;
using CoreNode = JolieCat3D.Core.Scene.Node;
using CoreRay = JolieCat3D.Core.Geometry.Ray;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

namespace JolieCat3D.Engine.Editing
{
    /// <summary>
    /// Texture Paint mode's own per-target state and brush entry point - the same
    /// "world hit point + radius, one tick per mouse-move sample" shape
    /// <see cref="VertexPaintSession"/>/<see cref="WeightPaintSession"/> already
    /// established, except the brush stamps a <see cref="Material.PaintedTextureBuffer"/>
    /// (a flat 2D pixel grid - <see cref="BrushRadius"/> is a PIXEL distance, not a
    /// world-space one) instead of a mesh's own per-vertex color/weight - the actual "3D
    /// viewport paints directly onto a material's own 2D texture" bridge the task's own
    /// wording asks for. <see cref="RaycastUVHitPoint"/> is what turns a 3D mouse
    /// ray into the exact UV coordinate to paint at: it hits <see cref="Target"/>'s own
    /// CURRENT (modifier/skinning-evaluated) triangle via <see cref="RayIntersection.IntersectTriangleBarycentric"/>
    /// (not the plain <see cref="RayIntersection.IntersectTriangle"/> every other
    /// session's own raycast uses - THIS one specifically needs the barycentric U/V it
    /// throws away), then interpolates that triangle's own 3 vertex UVs the identical
    /// barycentric way.
    /// </summary>
    public sealed class TexturePaintSession
    {
        /// <summary>A brand new material's own canvas starts at this resolution - modest
        /// enough to paint/rebuild (<see cref="Geometry.MaterialFactory.CreateDiffuseBrush"/>
        /// rebuilds the whole <see cref="WriteableBitmap"/> fresh on every render - see
        /// its own remarks) cheaply, generous enough that a brush stroke doesn't look
        /// visibly blocky at this app's own typical viewport zoom levels.</summary>
        private const int DefaultCanvasSize = 512;

        public CoreNode? Target { get; private set; }

        public Color4 Color { get; set; } = Color4.Red;

        /// <summary>A PIXEL distance (this material's own painted texture resolution),
        /// unlike every other brush's own world-space <c>BrushRadius</c> - see this
        /// class's own remarks.</summary>
        public float BrushRadius { get; set; } = 20f;

        public float Strength { get; set; } = 0.5f;

        public void Attach(CoreNode? node) => Target = node;

        /// <summary>Lazily installs <see cref="Target"/>'s own <see cref="Core.Geometry.Mesh.Material"/>
        /// (falling back to a brand new default material, assigned onto the mesh, if it
        /// has none at all - the same "unconfigured means create one now" latitude
        /// <see cref="Core.Skinning.SkinBindingFactory"/>'s own "Bind to Armature" step
        /// already gives) with a <see cref="Material.PaintedTextureBuffer"/> to actually
        /// paint onto - a no-op if one is already installed (painting keeps building on
        /// the SAME buffer across an entire session, never silently resetting it back to
        /// the source file on every stroke). The buffer's own initial pixels come from
        /// DECODING <see cref="Material.DiffuseTexturePath"/>, if set and readable (via
        /// WPF's own <see cref="BitmapImage"/> - the actual image-codec step
        /// <c>JolieCat3D.Core</c> itself can never do, see <see cref="Core.Materials.TextureBuffer"/>'s
        /// own remarks) - or a blank white <see cref="DefaultCanvasSize"/>-square canvas
        /// otherwise (no path set, or the file is missing/unreadable), never throwing for
        /// a broken texture reference, the same "degrade to a sane fallback, don't crash
        /// the paint session" latitude <see cref="Geometry.MaterialFactory.CreateDiffuseBrush"/>'s
        /// own file-loading already has. Called once, right when Texture Paint mode is
        /// entered (see <c>Rendering.Scene3DRenderer.EnterTexturePaintMode</c>), not on
        /// every single brush tick.</summary>
        public void EnsureTextureBuffer()
        {
            if (Target?.Mesh is not { } mesh) return;

            mesh.Material ??= Material.CreateDefault();
            var material = mesh.Material;
            if (material.PaintedTextureBuffer is not null) return;

            material.PaintedTextureBuffer = TryDecodeExistingTexture(material.DiffuseTexturePath)
                ?? new TextureBuffer(DefaultCanvasSize, DefaultCanvasSize, Color4.White);
        }

        private static TextureBuffer? TryDecodeExistingTexture(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

            try
            {
                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.UriSource = new Uri(Path.GetFullPath(path), UriKind.Absolute);
                bitmapImage.EndInit();

                var converted = new FormatConvertedBitmap(bitmapImage, System.Windows.Media.PixelFormats.Bgra32, null, 0);
                var width = converted.PixelWidth;
                var height = converted.PixelHeight;
                if (width <= 0 || height <= 0) return null;

                var stride = width * 4;
                var bytes = new byte[stride * height];
                converted.CopyPixels(bytes, stride, 0);

                var buffer = new TextureBuffer(width, height);
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        var offset = y * stride + x * 4;
                        var color = Color4.FromBytes(bytes[offset + 2], bytes[offset + 1], bytes[offset], bytes[offset + 3]);
                        buffer.SetPixel(x, y, color);
                    }
                }

                return buffer;
            }
            catch (Exception)
            {
                // A missing/unreadable/invalid image degrades to the blank-canvas
                // fallback in EnsureTextureBuffer - never crashes entering Texture Paint
                // mode.
                return null;
            }
        }

        /// <summary>Casts a ray from <paramref name="position"/> against <see cref="Target"/>'s
        /// own CURRENT (modifier/skinning-evaluated) geometry, in world space - the same
        /// "what's actually on screen right now" raycast <see cref="VertexPaintSession.RaycastWorldHitPoint"/>/
        /// <see cref="WeightPaintSession.RaycastWorldHitPoint"/> already use, except this
        /// returns the hit's own INTERPOLATED UV coordinate (via <see cref="RayIntersection.IntersectTriangleBarycentric"/>'s
        /// own barycentric U/V, applied to that triangle's own 3 vertex UVs) rather than
        /// a world-space point - there is no "world position to paint at" for a texture
        /// brush, only a 2D coordinate into the material's own image.</summary>
        public Vector2? RaycastUVHitPoint(HelixViewport3D viewport, Point position)
        {
            ArgumentNullException.ThrowIfNull(viewport);
            if (Target?.Mesh is not { } mesh) return null;

            var ray3D = Viewport3DHelper.Point2DtoRay3D(viewport.Viewport, position);
            var ray = new CoreRay(
                new Vector3((float)ray3D.Origin.X, (float)ray3D.Origin.Y, (float)ray3D.Origin.Z),
                new Vector3((float)ray3D.Direction.X, (float)ray3D.Direction.Y, (float)ray3D.Direction.Z));

            var worldTransform = Target.GetWorldTransform();
            var evaluatedMesh = ModifierStack.Evaluate(mesh, Target.Modifiers, Target);
            var skinnedMesh = Target.SkinBinding is { } binding
                ? SkinningEvaluator.Deform(evaluatedMesh, worldTransform, binding)
                : evaluatedMesh;

            float? nearestDistance = null;
            Vector2? nearestUV = null;

            foreach (var face in skinnedMesh.GetRenderFaces())
            {
                var a = Vector3.Transform(skinnedMesh.Vertices[face.A].Position, worldTransform);
                var b = Vector3.Transform(skinnedMesh.Vertices[face.B].Position, worldTransform);
                var c = Vector3.Transform(skinnedMesh.Vertices[face.C].Position, worldTransform);

                if (RayIntersection.IntersectTriangleBarycentric(ray, a, b, c) is not { } hit) continue;
                if (nearestDistance is not null && hit.Distance >= nearestDistance) continue;

                var uvA = skinnedMesh.Vertices[face.A].UV;
                var uvB = skinnedMesh.Vertices[face.B].UV;
                var uvC = skinnedMesh.Vertices[face.C].UV;

                nearestDistance = hit.Distance;
                nearestUV = uvA + hit.U * (uvB - uvA) + hit.V * (uvC - uvA);
            }

            return nearestUV;
        }

        /// <summary>Applies one brush tick at <paramref name="uv"/> - a no-op if
        /// <see cref="Target"/> has no mesh/material, or <see cref="EnsureTextureBuffer"/>
        /// hasn't installed a buffer yet (paints the RAW <see cref="Material.PaintedTextureBuffer"/>
        /// directly - the actual mutable canvas, same "mutate the real backing store, not
        /// some evaluated copy" shape every other brush in this project already
        /// has).</summary>
        public void PaintAt(Vector2 uv)
        {
            if (Target?.Mesh?.Material?.PaintedTextureBuffer is not { } buffer) return;
            TexturePaintBrush.Apply(buffer, uv, BrushRadius, Color, Strength);
        }
    }
}
