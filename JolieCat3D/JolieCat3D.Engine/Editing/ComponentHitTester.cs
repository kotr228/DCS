using System.Windows;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using JolieCat3D.Core.Geometry;
using CoreNode = JolieCat3D.Core.Scene.Node;
using CoreRay = JolieCat3D.Core.Geometry.Ray;
// HelixToolkit.Wpf declares its own Polygon type too - alias JolieCat3D.Core.Geometry's
// own one explicitly so every reference to it here is unambiguous.
using CorePolygon = JolieCat3D.Core.Geometry.Polygon;
using Vector3 = System.Numerics.Vector3;

namespace JolieCat3D.Engine.Editing
{
    /// <summary>
    /// Resolves a 2D viewport click to the nearest mesh component (vertex/edge/face) of
    /// a single <see cref="CoreNode"/> - Edit Mode's own equivalent of
    /// <c>Selection.SceneHitTester</c>, which only ever resolves to a whole node. Builds
    /// a real world-space <see cref="CoreRay"/> from the click (via HelixToolkit.Wpf's
    /// own <see cref="Viewport3DHelper.Point2DtoRay3D(System.Windows.Controls.Viewport3D,Point)"/>)
    /// and does all its actual intersection math through <see cref="RayIntersection"/> -
    /// <c>JolieCat3D.Core</c>'s own WPF-free ray/triangle/point/segment math - so a
    /// candidate is ranked by <see cref="RayIntersection.DistanceToPoint"/>/
    /// <see cref="RayIntersection.DistanceToSegment"/>'s own <c>DistanceAlongRay</c> (a
    /// real depth, i.e. how far along the ray from the CAMERA the candidate sits), not
    /// merely by 2D screen-space distance the way an earlier, purely-2D-projection
    /// version of this class did: looking THROUGH a mesh at two vertices that happen to
    /// project to nearly the same screen point, the one nearer the camera now correctly
    /// wins, the same "prefer the frontmost of two overlapping candidates" reasoning
    /// <c>Selection.SceneHitTester</c> already applies via WPF's own hit-test distance.
    /// Screen-space projection is still what decides whether a candidate is within click
    /// range at all (<paramref name="pixelThreshold"/>'s own unit) - a purely
    /// world-space tolerance wouldn't scale with the viewport's own zoom level the way a
    /// pixel-radius one does - so this is a genuine combination of both, not a wholesale
    /// replacement of one by the other.
    /// </summary>
    public static class ComponentHitTester
    {
        public const double DefaultPixelThreshold = 12.0;

        /// <summary>The index of <paramref name="node"/>'s own mesh vertex nearest the
        /// CAMERA (i.e. the smallest <c>DistanceAlongRay</c>, not merely the smallest
        /// screen-space distance) among every vertex that projects within
        /// <paramref name="pixelThreshold"/> pixels of <paramref name="screenPosition"/>
        /// - null if none is that close (or <paramref name="node"/> has no mesh/vertices),
        /// or if every candidate within range is actually BEHIND the camera (never a
        /// valid pick, however small its screen-space distance).</summary>
        public static int? HitTestVertex(HelixViewport3D viewport, CoreNode node, Point screenPosition, double pixelThreshold = DefaultPixelThreshold)
        {
            ArgumentNullException.ThrowIfNull(viewport);
            ArgumentNullException.ThrowIfNull(node);

            if (node.Mesh is not { } mesh || mesh.Vertices.Count == 0) return null;

            var world = node.GetWorldTransform();
            var ray = BuildRay(viewport, screenPosition);

            int? nearestIndex = null;
            var nearestDepth = float.MaxValue;

            for (var i = 0; i < mesh.Vertices.Count; i++)
            {
                var worldPosition = System.Numerics.Vector3.Transform(mesh.Vertices[i].Position, world);
                if (!WithinPixelThreshold(viewport, worldPosition, screenPosition, pixelThreshold)) continue;

                if (RayIntersection.DistanceToPoint(ray, worldPosition) is not { } candidate || candidate.DistanceAlongRay < 0) continue;
                if (candidate.DistanceAlongRay >= nearestDepth) continue;

                nearestDepth = candidate.DistanceAlongRay;
                nearestIndex = i;
            }

            return nearestIndex;
        }

        /// <summary>The nearest-to-camera edge (an (A, B) vertex-index pair, from
        /// <see cref="Mesh.GetEdges"/>) among every edge whose own projected segment
        /// passes within <paramref name="pixelThreshold"/> pixels of
        /// <paramref name="screenPosition"/> - the same "screen-space gates eligibility,
        /// ray depth breaks ties" combination <see cref="HitTestVertex"/> uses, via
        /// <see cref="RayIntersection.DistanceToSegment"/>.</summary>
        public static (int A, int B)? HitTestEdge(HelixViewport3D viewport, CoreNode node, Point screenPosition, double pixelThreshold = DefaultPixelThreshold)
        {
            ArgumentNullException.ThrowIfNull(viewport);
            ArgumentNullException.ThrowIfNull(node);

            if (node.Mesh is not { } mesh) return null;

            var world = node.GetWorldTransform();
            var ray = BuildRay(viewport, screenPosition);

            (int A, int B)? nearestEdge = null;
            var nearestDepth = float.MaxValue;

            foreach (var (a, b) in mesh.GetEdges())
            {
                var worldA = System.Numerics.Vector3.Transform(mesh.Vertices[a].Position, world);
                var worldB = System.Numerics.Vector3.Transform(mesh.Vertices[b].Position, world);

                var screenA = Project(viewport, worldA);
                var screenB = Project(viewport, worldB);
                if (ScreenDistanceToSegment(screenPosition, screenA, screenB) > pixelThreshold) continue;

                if (RayIntersection.DistanceToSegment(ray, worldA, worldB) is not { } candidate || candidate.DistanceAlongRay < 0) continue;
                if (candidate.DistanceAlongRay >= nearestDepth) continue;

                nearestDepth = candidate.DistanceAlongRay;
                nearestEdge = (a, b);
            }

            return nearestEdge;
        }

        /// <summary>The face (as a <see cref="CorePolygon"/> - a <see cref="Face"/>
        /// triangle is wrapped in one so either source reads back uniformly) a real
        /// ray cast through <paramref name="screenPosition"/> actually hits FIRST - true
        /// ray-triangle intersection (<see cref="RayIntersection.IntersectTriangle"/>,
        /// the Moller-Trumbore algorithm) against every triangle each candidate
        /// n-gon/triangle fan-triangulates into (matching <see cref="Polygon.Triangulate"/>'s
        /// own convention), keeping whichever has the SMALLEST intersection distance -
        /// correctly resolving occlusion (the actual frontmost face wins) as a natural
        /// consequence of comparing real ray depths, not a separate screen-space-area
        /// heuristic the way an earlier, 2D-point-in-polygon version of this method
        /// needed. Null if the ray hits none of this node's faces at all.</summary>
        public static CorePolygon? HitTestFace(HelixViewport3D viewport, CoreNode node, Point screenPosition)
        {
            ArgumentNullException.ThrowIfNull(viewport);
            ArgumentNullException.ThrowIfNull(node);

            if (node.Mesh is not { } mesh) return null;

            var world = node.GetWorldTransform();
            var ray = BuildRay(viewport, screenPosition);

            CorePolygon? bestFace = null;
            var bestDistance = float.MaxValue;

            void Consider(IReadOnlyList<int> indices, CorePolygon polygon)
            {
                // Fan triangulation - Polygon.Triangulate's own convention: every
                // triangle shares indices[0] as one corner, matching how Mesh.GetRenderFaces
                // (and therefore what's actually drawn/clickable on screen) triangulates
                // this same polygon.
                for (var i = 1; i < indices.Count - 1; i++)
                {
                    var a = System.Numerics.Vector3.Transform(mesh.Vertices[indices[0]].Position, world);
                    var b = System.Numerics.Vector3.Transform(mesh.Vertices[indices[i]].Position, world);
                    var c = System.Numerics.Vector3.Transform(mesh.Vertices[indices[i + 1]].Position, world);

                    if (RayIntersection.IntersectTriangle(ray, a, b, c) is not { } distance || distance >= bestDistance) continue;

                    bestDistance = distance;
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

        /// <summary>Builds the real world-space ray <paramref name="screenPosition"/>
        /// projects to, via HelixToolkit.Wpf's own <see cref="Viewport3DHelper.Point2DtoRay3D"/> -
        /// already accounts for the viewport's current camera position/orientation AND
        /// projection (perspective or orthographic), so nothing here needs to know which
        /// kind of camera is active.</summary>
        private static CoreRay BuildRay(HelixViewport3D viewport, Point screenPosition)
        {
            var ray3D = Viewport3DHelper.Point2DtoRay3D(viewport.Viewport, screenPosition);
            return new CoreRay(
                new Vector3((float)ray3D.Origin.X, (float)ray3D.Origin.Y, (float)ray3D.Origin.Z),
                new Vector3((float)ray3D.Direction.X, (float)ray3D.Direction.Y, (float)ray3D.Direction.Z));
        }

        private static bool WithinPixelThreshold(HelixViewport3D viewport, Vector3 worldPosition, Point screenPosition, double pixelThreshold) =>
            (Project(viewport, worldPosition) - screenPosition).Length <= pixelThreshold;

        private static Point Project(HelixViewport3D viewport, Vector3 worldPosition)
        {
            var point3D = new Point3D(worldPosition.X, worldPosition.Y, worldPosition.Z);
            return Viewport3DHelper.Point3DtoPoint2D(viewport.Viewport, point3D);
        }

        /// <summary>Standard 2D point-to-segment distance: the distance from
        /// <paramref name="point"/> to the nearest point on the segment
        /// <paramref name="a"/> -&gt; <paramref name="b"/>, clamping the projection
        /// parameter to [0,1] so a click beyond either endpoint measures to that
        /// endpoint rather than the segment's infinite extension. Screen-space only -
        /// used purely as <see cref="HitTestEdge"/>'s own click-tolerance gate; see
        /// <see cref="RayIntersection.DistanceToSegment"/> for the real 3D depth that
        /// actually ranks candidates.</summary>
        private static double ScreenDistanceToSegment(Point point, Point a, Point b)
        {
            var ab = b - a;
            var lengthSquared = ab.X * ab.X + ab.Y * ab.Y;
            if (lengthSquared < 1e-9) return (point - a).Length;

            var t = ((point.X - a.X) * ab.X + (point.Y - a.Y) * ab.Y) / lengthSquared;
            t = Math.Clamp(t, 0.0, 1.0);

            var closest = new Point(a.X + ab.X * t, a.Y + ab.Y * t);
            return (point - closest).Length;
        }
    }
}
