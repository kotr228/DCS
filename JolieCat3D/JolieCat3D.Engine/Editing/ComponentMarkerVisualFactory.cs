using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using JolieCat3D.Core.Geometry;

namespace JolieCat3D.Engine.Editing
{
    /// <summary>
    /// Builds Edit Mode's own on-screen marker visuals for whichever node a
    /// <see cref="MeshEditSession"/> is currently attached to: a dot per vertex (via
    /// <see cref="PointsVisual3D"/>), a line per edge (via <see cref="LinesVisual3D"/>),
    /// and a translucent overlay over every fully-selected face - each colored to
    /// distinguish selected components from unselected ones, mirroring
    /// <c>Selection.SelectionHighlightFactory</c>'s own "a separate, additive visual,
    /// never a change to the mesh's own material" approach, so entering Edit Mode never
    /// risks altering how the mesh actually renders.
    /// </summary>
    public static class ComponentMarkerVisualFactory
    {
        private static readonly Color UnselectedVertexColor = Color.FromRgb(0x90, 0x90, 0x98);
        private static readonly Color SelectedColor = Color.FromRgb(0xC2, 0x9B, 0x58); // JolieCat AccentBrush (gold)
        private static readonly Color EdgeColor = Color.FromRgb(0x50, 0x50, 0x58);
        private static readonly Color SelectedFaceColor = Color.FromArgb(0x80, 0xC2, 0x9B, 0x58); // translucent gold

        /// <summary>Every marker visual for <paramref name="session"/>'s current
        /// <see cref="MeshEditSession.Target"/> and selection - empty with no target, no
        /// mesh, or an empty mesh. The caller (<c>Rendering.Scene3DRenderer</c>) adds
        /// these under one container visual it clears and rebuilds each call, the same
        /// "tear down and recreate the whole set" pattern <c>Gizmos.TransformGizmo.Rebuild</c>
        /// uses for its own handles.</summary>
        public static IEnumerable<Visual3D> CreateOverlay(MeshEditSession session)
        {
            ArgumentNullException.ThrowIfNull(session);

            if (session.Target?.Mesh is not { } mesh || mesh.Vertices.Count == 0)
                yield break;

            var world = session.Target.GetWorldTransform();

            // Edges first, drawn underneath the vertex dots - a plain wireframe of the
            // whole mesh, gold and thicker for a fully-selected edge (both endpoints
            // selected) versus a dim gray one for the rest.
            var unselectedEdgePoints = new Point3DCollection();
            var selectedEdgePoints = new Point3DCollection();

            foreach (var (a, b) in mesh.GetEdges())
            {
                var pointA = ToPoint3D(Vector3.Transform(mesh.Vertices[a].Position, world));
                var pointB = ToPoint3D(Vector3.Transform(mesh.Vertices[b].Position, world));

                var target = session.IsEdgeSelected(a, b) ? selectedEdgePoints : unselectedEdgePoints;
                target.Add(pointA);
                target.Add(pointB);
            }

            if (unselectedEdgePoints.Count > 0)
                yield return new LinesVisual3D { Points = unselectedEdgePoints, Color = EdgeColor, Thickness = 1.0 };
            if (selectedEdgePoints.Count > 0)
                yield return new LinesVisual3D { Points = selectedEdgePoints, Color = SelectedColor, Thickness = 2.0 };

            // Selected faces - a translucent tinted overlay over exactly the triangles
            // of every fully-selected polygon/face, so Face mode has a visible "this
            // whole face is selected" cue beyond just its corner dots lighting up.
            if (BuildSelectedFaceOverlay(session, mesh, world) is { } faceOverlay)
                yield return faceOverlay;

            // Vertex dots last, drawn on top of both - one PointsVisual3D per color, so
            // a selected vertex's dot is unambiguously distinguishable from an
            // unselected one at a glance.
            var unselectedVertices = new Point3DCollection();
            var selectedVertices = new Point3DCollection();

            for (var i = 0; i < mesh.Vertices.Count; i++)
            {
                var point = ToPoint3D(Vector3.Transform(mesh.Vertices[i].Position, world));
                (session.SelectedVertexIndices.Contains(i) ? selectedVertices : unselectedVertices).Add(point);
            }

            if (unselectedVertices.Count > 0)
                yield return new PointsVisual3D { Points = unselectedVertices, Color = UnselectedVertexColor, Size = 6.0 };
            if (selectedVertices.Count > 0)
                yield return new PointsVisual3D { Points = selectedVertices, Color = SelectedColor, Size = 9.0 };
        }

        /// <summary>A single translucent <see cref="GeometryModel3D"/> covering every
        /// triangle of every currently-fully-selected polygon/face - null if none is
        /// selected. A flat, unlit <see cref="EmissiveMaterial"/> (not
        /// <see cref="DiffuseMaterial"/>) so the highlight reads as a constant tint
        /// regardless of scene lighting, the same way a modeling tool's own
        /// selected-face highlight isn't shaded by the scene's own lights either.
        /// </summary>
        private static Visual3D? BuildSelectedFaceOverlay(MeshEditSession session, Mesh mesh, Matrix4x4 world)
        {
            var geometry = new MeshGeometry3D();
            var vertexRemap = new Dictionary<int, int>();

            int MapVertex(int meshIndex)
            {
                if (vertexRemap.TryGetValue(meshIndex, out var overlayIndex)) return overlayIndex;

                var worldPosition = Vector3.Transform(mesh.Vertices[meshIndex].Position, world);
                geometry.Positions.Add(ToPoint3D(worldPosition));
                overlayIndex = geometry.Positions.Count - 1;
                vertexRemap[meshIndex] = overlayIndex;
                return overlayIndex;
            }

            void AddTriangle(int a, int b, int c)
            {
                geometry.TriangleIndices.Add(MapVertex(a));
                geometry.TriangleIndices.Add(MapVertex(b));
                geometry.TriangleIndices.Add(MapVertex(c));
            }

            foreach (var polygon in mesh.Polygons)
            {
                if (!session.IsFaceSelected(polygon.Indices)) continue;
                foreach (var triangle in polygon.Triangulate())
                    AddTriangle(triangle.A, triangle.B, triangle.C);
            }

            foreach (var face in mesh.Faces)
            {
                if (!session.IsFaceSelected(new[] { face.A, face.B, face.C })) continue;
                AddTriangle(face.A, face.B, face.C);
            }

            if (geometry.TriangleIndices.Count == 0) return null;

            geometry.Freeze();

            var material = new EmissiveMaterial(new SolidColorBrush(SelectedFaceColor));
            material.Freeze();

            return new ModelVisual3D
            {
                Content = new GeometryModel3D(geometry, material) { BackMaterial = material },
            };
        }

        private static Point3D ToPoint3D(Vector3 v) => new(v.X, v.Y, v.Z);
    }
}
