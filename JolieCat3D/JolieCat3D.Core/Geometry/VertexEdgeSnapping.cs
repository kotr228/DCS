using System.Numerics;

namespace JolieCat3D.Core.Geometry
{
    /// <summary>
    /// The actual "raycast against other mesh geometry in the scene... snap to the
    /// nearest target vertex or edge" math behind Precision Modeling's Shift-held
    /// Vertex/Edge Snapping (<c>Engine.Gizmos.TransformGizmo</c>'s own Shift-drag
    /// handling) - kept here, not in <c>JolieCat3D.Engine</c>, so it's testable with no
    /// WPF runtime at all (the same Core/Engine split every other geometry algorithm in
    /// this project already follows).
    /// </summary>
    public static class VertexEdgeSnapping
    {
        /// <summary>The single closest vertex-or-edge point (in WORLD space) among
        /// <paramref name="worldTriangles"/> to <paramref name="ray"/> - null if the ray
        /// doesn't hit ANY triangle in <paramref name="worldTriangles"/> at all (nothing
        /// actually under the cursor to snap to), even if some vertex happens to sit
        /// geometrically close to the ray's own infinite line - matching a
        /// raycast-driven "what's actually under the cursor" convention (the same one
        /// <c>Engine.Selection.SceneHitTester</c>'s own click-to-select already uses)
        /// rather than a looser "nearest point anywhere in the whole scene" search that
        /// could snap to something the cursor isn't even pointing at.
        ///
        /// Once the single NEAREST-hit triangle is found (the standard "closest to the
        /// ray's own origin" tie-break every other raycast in this project already
        /// uses), this picks whichever ONE of that triangle's own 3 vertices or 3 edges
        /// passes closest to the ray itself - "vertex or edge", not "vertex and then
        /// edge as a fallback": a drag aimed at the middle of a long edge snaps to a
        /// point along it, not to whichever of its two endpoints happens to be
        /// (arbitrarily) nearer.</summary>
        public static Vector3? FindNearestVertexOrEdge(Ray ray, IEnumerable<(Vector3 A, Vector3 B, Vector3 C)> worldTriangles)
        {
            ArgumentNullException.ThrowIfNull(worldTriangles);

            (Vector3 A, Vector3 B, Vector3 C)? nearestFace = null;
            var nearestFaceDistance = float.PositiveInfinity;

            foreach (var triangle in worldTriangles)
            {
                if (RayIntersection.IntersectTriangle(ray, triangle.A, triangle.B, triangle.C) is not { } distance) continue;
                if (distance >= nearestFaceDistance) continue;

                nearestFaceDistance = distance;
                nearestFace = triangle;
            }

            if (nearestFace is not { } face) return null;

            Vector3? bestPoint = null;
            var bestPerpendicular = float.PositiveInfinity;

            void ConsiderVertex(Vector3 vertex)
            {
                if (RayIntersection.DistanceToPoint(ray, vertex) is not { } result) return;
                if (result.PerpendicularDistance >= bestPerpendicular) return;

                bestPerpendicular = result.PerpendicularDistance;
                bestPoint = vertex;
            }

            void ConsiderEdge(Vector3 a, Vector3 b)
            {
                if (RayIntersection.ClosestPoints(ray, a, b) is not { } result) return;
                if (result.PerpendicularDistance >= bestPerpendicular) return;

                bestPerpendicular = result.PerpendicularDistance;
                bestPoint = result.PointOnSegment;
            }

            // Vertices considered before edges, with a strict "<" comparison throughout,
            // means a genuine tie (the closest point on an edge landing exactly on one
            // of its own endpoints) resolves to the VERTEX reading - the more useful of
            // the two identical results to report, and a deterministic one either way.
            ConsiderVertex(face.A);
            ConsiderVertex(face.B);
            ConsiderVertex(face.C);
            ConsiderEdge(face.A, face.B);
            ConsiderEdge(face.B, face.C);
            ConsiderEdge(face.C, face.A);

            return bestPoint;
        }
    }
}
