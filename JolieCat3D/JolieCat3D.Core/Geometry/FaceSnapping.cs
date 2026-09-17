using System.Numerics;

namespace JolieCat3D.Core.Geometry
{
    /// <summary>
    /// The actual "raycast against other mesh geometry in the scene... snap to the
    /// exact point of intersection on the target face" math behind Precision Modeling's
    /// Face Snapping (<c>Engine.Gizmos.TransformGizmo</c>'s own snapping) - kept here,
    /// not in <c>JolieCat3D.Engine</c>, so it's testable with no WPF runtime at all, the
    /// same Core/Engine split <see cref="VertexEdgeSnapping"/> (Face Snapping's own
    /// sibling feature) already established.
    /// </summary>
    public static class FaceSnapping
    {
        /// <summary>The single closest triangle <paramref name="ray"/> actually hits
        /// among <paramref name="worldTriangles"/> (the standard "closest to the ray's
        /// own origin" tie-break every other raycast in this project already uses) - the
        /// exact <see cref="RayIntersection.IntersectTriangle"/> hit point, plus that
        /// triangle's own face normal (unit length, wound the same
        /// counter-clockwise-A-B-C way every <see cref="Face"/> already is - see
        /// <see cref="Mesh.RecalculateNormals"/>'s own convention). Null if the ray
        /// doesn't hit ANY triangle at all - the same "nothing actually under the
        /// cursor" convention <see cref="VertexEdgeSnapping.FindNearestVertexOrEdge"/>
        /// already follows, rather than snapping to geometry the cursor isn't even
        /// pointing at. A degenerate (zero-area) triangle is skipped entirely - it has
        /// no well-defined normal to align to, and <see cref="RayIntersection.IntersectTriangle"/>
        /// itself already can't produce a hit against one anyway (a zero cross product
        /// determinant), so this is only ever reachable for an already-hit, genuinely
        /// non-degenerate triangle - checked again explicitly here anyway since a normal
        /// this method hands back gets fed straight into a rotation (see
        /// <see cref="RotationMath.ShortestArcRotation"/>), not just compared against
        /// a threshold the way <see cref="RayIntersection.IntersectTriangle"/>'s own
        /// internal determinant check already is.</summary>
        public static (Vector3 Point, Vector3 Normal)? FindNearestFaceHit(Ray ray, IEnumerable<(Vector3 A, Vector3 B, Vector3 C)> worldTriangles)
        {
            ArgumentNullException.ThrowIfNull(worldTriangles);

            (Vector3 A, Vector3 B, Vector3 C)? nearestFace = null;
            var nearestDistance = float.PositiveInfinity;

            foreach (var triangle in worldTriangles)
            {
                if (RayIntersection.IntersectTriangle(ray, triangle.A, triangle.B, triangle.C) is not { } distance) continue;
                if (distance >= nearestDistance) continue;

                nearestDistance = distance;
                nearestFace = triangle;
            }

            if (nearestFace is not { } face) return null;

            var normal = Vector3.Cross(face.B - face.A, face.C - face.A);
            if (normal.LengthSquared() < 1e-12f) return null;
            normal = Vector3.Normalize(normal);

            var point = ray.Origin + ray.Direction * nearestDistance;
            return (point, normal);
        }
    }
}
