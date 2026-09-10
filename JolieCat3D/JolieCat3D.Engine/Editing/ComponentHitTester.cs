using System.Numerics;
using System.Windows;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using JolieCat3D.Core.Geometry;
using CoreNode = JolieCat3D.Core.Scene.Node;
// HelixToolkit.Wpf declares its own Polygon type too - alias JolieCat3D.Core.Geometry's
// own one explicitly so every reference to it here is unambiguous.
using CorePolygon = JolieCat3D.Core.Geometry.Polygon;

namespace JolieCat3D.Engine.Editing
{
    /// <summary>
    /// Resolves a 2D viewport click to the nearest mesh component (vertex/edge/face) of
    /// a single <see cref="CoreNode"/> - Edit Mode's own equivalent of
    /// <c>Selection.SceneHitTester</c>, which only ever resolves to a whole node. WPF's
    /// native 3D hit-testing (<see cref="System.Windows.Media.VisualTreeHelper.HitTest(System.Windows.Media.Visual,System.Windows.Media.HitTestFilterCallback,System.Windows.Media.HitTestResultCallback,HitTestParameters)"/>,
    /// already used there) only ever reports which whole triangle a ray hit, never a
    /// discrete vertex or edge - so this instead projects each candidate component's own
    /// world-space position to 2D screen space (via
    /// <see cref="Viewport3DHelper.Point3DtoPoint2D(System.Windows.Controls.Viewport3D,Point3D)"/>
    /// - confirmed, via its own decoded method signature, to return a plain
    /// <see cref="Point"/>, not something that can report "unprojectable" - the one
    /// HelixToolkit.Wpf helper that does this at all) and measures plain 2D
    /// screen-space distance from the click, the same "project every candidate and pick
    /// whichever lands nearest the cursor" approach any 3D editor's own vertex-snap/
    /// click-select tool uses.
    /// </summary>
    public static class ComponentHitTester
    {
        public const double DefaultPixelThreshold = 12.0;

        /// <summary>The index of <paramref name="node"/>'s own mesh vertex nearest (in
        /// screen space) to <paramref name="screenPosition"/>, within
        /// <paramref name="pixelThreshold"/> pixels - null if none is that close, or
        /// <paramref name="node"/> has no mesh/vertices.</summary>
        public static int? HitTestVertex(HelixViewport3D viewport, CoreNode node, Point screenPosition, double pixelThreshold = DefaultPixelThreshold)
        {
            ArgumentNullException.ThrowIfNull(viewport);
            ArgumentNullException.ThrowIfNull(node);

            if (node.Mesh is not { } mesh || mesh.Vertices.Count == 0) return null;

            var world = node.GetWorldTransform();
            int? nearestIndex = null;
            var nearestDistance = double.MaxValue;

            for (var i = 0; i < mesh.Vertices.Count; i++)
            {
                var screen = Project(viewport, mesh.Vertices[i].Position, world);
                var distance = (screen - screenPosition).Length;
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestIndex = i;
                }
            }

            return nearestDistance <= pixelThreshold ? nearestIndex : null;
        }

        /// <summary>The nearest edge (an (A, B) vertex-index pair, from
        /// <see cref="Mesh.GetEdges"/>) to <paramref name="screenPosition"/>, by 2D
        /// point-to-segment distance between its own two projected endpoints - null if
        /// none is within <paramref name="pixelThreshold"/> pixels.</summary>
        public static (int A, int B)? HitTestEdge(HelixViewport3D viewport, CoreNode node, Point screenPosition, double pixelThreshold = DefaultPixelThreshold)
        {
            ArgumentNullException.ThrowIfNull(viewport);
            ArgumentNullException.ThrowIfNull(node);

            if (node.Mesh is not { } mesh) return null;

            var world = node.GetWorldTransform();
            (int A, int B)? nearestEdge = null;
            var nearestDistance = double.MaxValue;

            foreach (var (a, b) in mesh.GetEdges())
            {
                var screenA = Project(viewport, mesh.Vertices[a].Position, world);
                var screenB = Project(viewport, mesh.Vertices[b].Position, world);
                var distance = DistanceToSegment(screenPosition, screenA, screenB);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestEdge = (a, b);
                }
            }

            return nearestDistance <= pixelThreshold ? nearestEdge : null;
        }

        /// <summary>The face (as a <see cref="CorePolygon"/> - a <see cref="Face"/> triangle
        /// is wrapped in one so either source reads back uniformly) whose own projected
        /// outline actually contains <paramref name="screenPosition"/> - a standard 2D
        /// point-in-polygon test against each candidate's own projected vertices, not
        /// just "nearest centroid" (correct even for a face clicked well off its own
        /// center), preferring the smallest such outline (in screen-space area) when
        /// more than one candidate's outline contains the point - the frontmost/smallest
        /// face an overlapping click could plausibly mean, the same "prefer the nearer
        /// of two overlapping hits" reasoning <c>Selection.SceneHitTester</c> applies via
        /// ray distance. Null if the click lands on none of this node's faces at all.
        /// </summary>
        public static CorePolygon? HitTestFace(HelixViewport3D viewport, CoreNode node, Point screenPosition)
        {
            ArgumentNullException.ThrowIfNull(viewport);
            ArgumentNullException.ThrowIfNull(node);

            if (node.Mesh is not { } mesh) return null;

            var world = node.GetWorldTransform();
            CorePolygon? bestFace = null;
            var bestArea = double.MaxValue;

            void Consider(IReadOnlyList<int> indices, CorePolygon polygon)
            {
                var screenPoints = new Point[indices.Count];
                for (var i = 0; i < indices.Count; i++)
                    screenPoints[i] = Project(viewport, mesh.Vertices[indices[i]].Position, world);

                if (!PointInPolygon(screenPosition, screenPoints)) return;

                var area = PolygonScreenArea(screenPoints);
                if (area < bestArea)
                {
                    bestArea = area;
                    bestFace = polygon;
                }
            }

            foreach (var polygon in mesh.Polygons) Consider(polygon.Indices, polygon);

            foreach (var face in mesh.Faces)
            {
                var indices = new[] { face.A, face.B, face.C };
                Consider(indices, new CorePolygon(indices));
            }

            return bestFace;
        }

        private static Point Project(HelixViewport3D viewport, Vector3 localPosition, Matrix4x4 world)
        {
            var worldPosition = Vector3.Transform(localPosition, world);
            var point3D = new Point3D(worldPosition.X, worldPosition.Y, worldPosition.Z);
            return Viewport3DHelper.Point3DtoPoint2D(viewport.Viewport, point3D);
        }

        /// <summary>Standard 2D point-to-segment distance: the distance from
        /// <paramref name="point"/> to the nearest point on the segment
        /// <paramref name="a"/> -&gt; <paramref name="b"/>, clamping the projection
        /// parameter to [0,1] so a click beyond either endpoint measures to that
        /// endpoint rather than the segment's infinite extension.</summary>
        private static double DistanceToSegment(Point point, Point a, Point b)
        {
            var ab = b - a;
            var lengthSquared = ab.X * ab.X + ab.Y * ab.Y;
            if (lengthSquared < 1e-9) return (point - a).Length;

            var t = ((point.X - a.X) * ab.X + (point.Y - a.Y) * ab.Y) / lengthSquared;
            t = Math.Clamp(t, 0.0, 1.0);

            var closest = new Point(a.X + ab.X * t, a.Y + ab.Y * t);
            return (point - closest).Length;
        }

        /// <summary>Standard ray-casting point-in-polygon test (an odd number of edge
        /// crossings along a horizontal ray from <paramref name="point"/> means
        /// inside) - correct for the convex screen-space outlines a projected
        /// quad/triangle/n-gon produces, even where the underlying 3D face is viewed
        /// edge-on or foreshortened.</summary>
        private static bool PointInPolygon(Point point, IReadOnlyList<Point> polygon)
        {
            var inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                var pi = polygon[i];
                var pj = polygon[j];

                if ((pi.Y > point.Y) != (pj.Y > point.Y) &&
                    point.X < (pj.X - pi.X) * (point.Y - pi.Y) / (pj.Y - pi.Y) + pi.X)
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        /// <summary>The shoelace formula - a projected polygon's own 2D screen-space
        /// area, used only to rank overlapping face candidates by size (see
        /// <see cref="HitTestFace"/>'s own remarks), not for anything metric.</summary>
        private static double PolygonScreenArea(IReadOnlyList<Point> polygon)
        {
            double sum = 0;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
                sum += (polygon[j].X + polygon[i].X) * (polygon[j].Y - polygon[i].Y);
            return Math.Abs(sum) / 2.0;
        }
    }
}
