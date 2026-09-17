using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Modifiers;
using CoreScene = JolieCat3D.Core.Scene.Scene3D;

namespace JolieCat3D.Engine.Rendering
{
    /// <summary>
    /// Turns <see cref="ContactOcclusion.FindConcaveEdges"/>'s own geometric crease
    /// analysis into visible darkening decals - this project's own "SSAO" (see that
    /// class's own remarks on why a REAL Screen Space Ambient Occlusion pass, needing
    /// per-pixel depth/normal buffers this fixed-function WPF pipeline has nowhere at
    /// all, is not one of the things it can actually build). For every concave edge of
    /// every node's own evaluated mesh, this builds a thin, dark, semi-transparent quad
    /// straddling the crease (offset slightly outward along the fold's own averaged
    /// normal, the same "sits just above the real surface, avoiding z-fighting" trick
    /// <see cref="ShadowVisualFactory"/> already uses for its own ground decals), wide
    /// enough to visibly darken the area right around the seam - a real, computed
    /// approximation of contact shadows in exactly the kind of crevice a true AO pass
    /// would darken, not a cosmetic placeholder.
    /// </summary>
    public static class AmbientOcclusionVisualFactory
    {
        private static readonly Material DecalMaterial = CreateDecalMaterial();

        /// <summary>The decal ribbon's own half-width, as a fraction of each mesh's own
        /// local bounding-box max extent - the same "size relative to the object, not a
        /// fixed world-unit constant" reasoning <c>Gizmos.TransformGizmo.GetTargetMaxExtent</c>
        /// already established, so a tiny prop and a huge building both get a
        /// proportionally sensible (never swallowing, never invisible) decal width.</summary>
        private const float HalfWidthFactor = 0.015f;

        private const float MinHalfWidth = 0.005f;

        // A hair off the real surface, along the crease's own averaged normal - avoids
        // z-fighting against the mesh geometry the decal is meant to darken.
        private const float SurfaceOffset = 0.002f;

        private static Material CreateDecalMaterial()
        {
            var brush = new SolidColorBrush(Color.FromArgb(0x50, 0x00, 0x00, 0x00));
            brush.Freeze();
            var material = new EmissiveMaterial(brush); // unlit - see ShadowVisualFactory's own remarks on why a decal shouldn't itself brighten under direct light
            material.Freeze();
            return material;
        }

        /// <summary>Every meshed node's own concave-edge decals, combined into one
        /// <see cref="Model3D"/> - null if nothing in <paramref name="scene"/> has any
        /// (a scene with no mesh-having nodes at all, or one whose every mesh is
        /// entirely convex/flat, like a plain cube or sphere).</summary>
        public static Model3D? CreateOcclusionDecals(CoreScene scene)
        {
            ArgumentNullException.ThrowIfNull(scene);

            var group = new Model3DGroup();

            foreach (var node in scene.Traverse())
            {
                if (node.Mesh is not { } mesh) continue;

                var evaluatedMesh = ModifierStack.Evaluate(mesh, node.Modifiers, node);
                var concaveEdges = ContactOcclusion.FindConcaveEdges(evaluatedMesh).ToList();
                if (concaveEdges.Count == 0) continue;

                var halfWidth = MathF.Max(MinHalfWidth, GetMaxExtent(evaluatedMesh) * HalfWidthFactor);
                var world = node.GetWorldTransform();

                var geometry = new MeshGeometry3D();
                foreach (var edge in concaveEdges)
                    AddDecalRibbon(geometry, edge, halfWidth, world);

                if (geometry.TriangleIndices.Count == 0) continue;

                geometry.Freeze();
                // BackMaterial too - see SceneGraphBuilder's own remarks on why every
                // mesh in this project renders double-sided: a crease can face away
                // from the camera on a convoluted mesh just as easily as toward it,
                // unlike a ground shadow which only ever needs to face up.
                group.Children.Add(new GeometryModel3D(geometry, DecalMaterial) { BackMaterial = DecalMaterial });
            }

            if (group.Children.Count == 0) return null;
            group.Freeze();
            return group;
        }

        /// <summary>Appends one crease's own decal ribbon - a thin quad (2 triangles)
        /// running along <paramref name="edge"/>'s own A-&gt;B, offset outward along its
        /// averaged normal (<see cref="SurfaceOffset"/>) and spanning
        /// <paramref name="halfWidth"/> to each side along the direction perpendicular
        /// to both the edge and that normal - straddling the crease itself, the same
        /// "darken the seam, not just draw a line along it" shape a real contact shadow
        /// actually has.</summary>
        private static void AddDecalRibbon(MeshGeometry3D geometry, ContactOcclusion.ConcaveEdge edge, float halfWidth, Matrix4x4 world)
        {
            var edgeDirection = edge.B - edge.A;
            if (edgeDirection.LengthSquared() < 1e-12f) return;
            edgeDirection = Vector3.Normalize(edgeDirection);

            var side = Vector3.Cross(edgeDirection, edge.Normal);
            if (side.LengthSquared() < 1e-12f) return; // degenerate - the edge runs parallel to its own fold normal, no well-defined ribbon width direction
            side = Vector3.Normalize(side) * halfWidth;

            var offset = edge.Normal * SurfaceOffset;

            var a1 = Vector3.Transform(edge.A + offset - side, world);
            var a2 = Vector3.Transform(edge.A + offset + side, world);
            var b1 = Vector3.Transform(edge.B + offset - side, world);
            var b2 = Vector3.Transform(edge.B + offset + side, world);

            var baseIndex = geometry.Positions.Count;
            geometry.Positions.Add(new Point3D(a1.X, a1.Y, a1.Z));
            geometry.Positions.Add(new Point3D(a2.X, a2.Y, a2.Z));
            geometry.Positions.Add(new Point3D(b1.X, b1.Y, b1.Z));
            geometry.Positions.Add(new Point3D(b2.X, b2.Y, b2.Z));

            var worldNormal = Vector3.TransformNormal(edge.Normal, world);
            var wpfNormal = new Vector3D(worldNormal.X, worldNormal.Y, worldNormal.Z);
            for (var i = 0; i < 4; i++) geometry.Normals.Add(wpfNormal);

            geometry.TriangleIndices.Add(baseIndex + 0);
            geometry.TriangleIndices.Add(baseIndex + 2);
            geometry.TriangleIndices.Add(baseIndex + 1);
            geometry.TriangleIndices.Add(baseIndex + 1);
            geometry.TriangleIndices.Add(baseIndex + 2);
            geometry.TriangleIndices.Add(baseIndex + 3);
        }

        private static float GetMaxExtent(Mesh mesh)
        {
            if (mesh.Vertices.Count == 0) return 1f;

            var min = mesh.Vertices[0].Position;
            var max = min;
            foreach (var vertex in mesh.Vertices)
            {
                min = Vector3.Min(min, vertex.Position);
                max = Vector3.Max(max, vertex.Position);
            }

            var size = max - min;
            var maxExtent = MathF.Max(size.X, MathF.Max(size.Y, size.Z));
            return maxExtent > 1e-4f ? maxExtent : 1f;
        }
    }
}
