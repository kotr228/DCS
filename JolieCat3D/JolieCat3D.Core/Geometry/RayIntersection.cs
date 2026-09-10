using System.Numerics;

namespace JolieCat3D.Core.Geometry
{
    /// <summary>
    /// Plain, WPF-free 3D ray-intersection math - the actual "triangle/vertex
    /// intersection math" <c>JolieCat3D.Engine.Editing.ComponentHitTester</c>/
    /// <c>JolieCat3D.Engine.Selection.SceneHitTester</c> build on top of, kept here
    /// (rather than in <c>JolieCat3D.Engine</c>) so it's testable with no WPF/Windows
    /// runtime involved at all, the same "the math lives in Core, the WPF adapter lives
    /// in Engine" split every other geometry algorithm in this project already follows.
    /// Every method takes world-space inputs and returns a distance ALONG THE RAY (not
    /// a perpendicular/screen-space one) precisely so a caller comparing several
    /// candidate hits can pick the one nearest the ray's own origin (the camera) -
    /// "closest to the camera" in the task's own words - by comparing these distances
    /// directly, with no separate depth computation of its own needed.
    /// </summary>
    public static class RayIntersection
    {
        /// <summary>The Moller-Trumbore ray-triangle intersection algorithm - the
        /// standard, robust way to test a ray against a single triangle without ever
        /// computing the triangle's own plane equation up front. Returns the distance
        /// along <paramref name="ray"/> to the intersection point, or null if the ray:
        /// is parallel to the triangle's own plane, hits outside the triangle's own
        /// bounds (via the standard barycentric u/v tests), or would only intersect
        /// BEHIND <paramref name="ray"/>'s own origin (a mesh's own back side, as seen
        /// from a camera in front of it, is never a valid hit).</summary>
        public static float? IntersectTriangle(Ray ray, Vector3 a, Vector3 b, Vector3 c)
        {
            const float epsilon = 1e-6f;

            var edge1 = b - a;
            var edge2 = c - a;
            var pvec = Vector3.Cross(ray.Direction, edge2);
            var determinant = Vector3.Dot(edge1, pvec);

            // Near-zero determinant means the ray runs parallel to the triangle's own
            // plane - not "a very thin hit", genuinely no well-defined intersection at all.
            if (MathF.Abs(determinant) < epsilon) return null;

            var inverseDeterminant = 1f / determinant;
            var originToA = ray.Origin - a;

            var u = Vector3.Dot(originToA, pvec) * inverseDeterminant;
            if (u < 0f || u > 1f) return null;

            var qvec = Vector3.Cross(originToA, edge1);
            var v = Vector3.Dot(ray.Direction, qvec) * inverseDeterminant;
            if (v < 0f || u + v > 1f) return null;

            var distance = Vector3.Dot(edge2, qvec) * inverseDeterminant;
            return distance > epsilon ? distance : null;
        }

        /// <summary>How close <paramref name="ray"/> passes to <paramref name="point"/> -
        /// <see cref="PerpendicularDistance"/> (the actual "how many world units off the
        /// ray is this vertex" a caller thresholds against) plus
        /// <see cref="DistanceAlongRay"/> (that closest approach's own depth - negative
        /// for a point behind the ray's origin, the same front/back tie-breaker
        /// <see cref="IntersectTriangle"/>'s own returned distance already doubles as).
        /// Null only if <paramref name="ray"/>'s own <see cref="Ray.Direction"/> is the
        /// degenerate zero vector.</summary>
        public static (float DistanceAlongRay, float PerpendicularDistance)? DistanceToPoint(Ray ray, Vector3 point)
        {
            if (ray.Direction == Vector3.Zero) return null;

            var originToPoint = point - ray.Origin;
            var distanceAlongRay = Vector3.Dot(originToPoint, ray.Direction); // Direction is unit length (see Ray's own remarks), so this IS the projection length directly
            var closestPointOnRay = ray.Origin + ray.Direction * distanceAlongRay;
            var perpendicularDistance = Vector3.Distance(closestPointOnRay, point);

            return (distanceAlongRay, perpendicularDistance);
        }

        /// <summary>The closest approach between <paramref name="ray"/> (unbounded) and
        /// the SEGMENT <paramref name="a"/>-&gt;<paramref name="b"/> (bounded to its own
        /// two endpoints) - the standard closest-points-between-two-lines derivation,
        /// with the segment's own parameter clamped to [0,1] so the result is always a
        /// point actually ON the segment, never its infinite extension. Returns the same
        /// (DistanceAlongRay, PerpendicularDistance) shape as <see cref="DistanceToPoint"/>,
        /// for the same reason. Null only for a degenerate ray direction.</summary>
        public static (float DistanceAlongRay, float PerpendicularDistance)? DistanceToSegment(Ray ray, Vector3 a, Vector3 b)
        {
            if (ray.Direction == Vector3.Zero) return null;

            var segmentVector = b - a;
            var segmentLengthSquared = segmentVector.LengthSquared();

            // A degenerate (zero-length) segment is really just a point.
            if (segmentLengthSquared < 1e-12f) return DistanceToPoint(ray, a);

            var originToA = ray.Origin - a;
            var rayDotSegment = Vector3.Dot(ray.Direction, segmentVector);
            var rayDotOriginToA = Vector3.Dot(ray.Direction, originToA);
            var segmentDotOriginToA = Vector3.Dot(segmentVector, originToA);

            // The standard closest-points-between-two-lines linear system (Ericson,
            // "Real-Time Collision Detection" 5.1.9): with w0 = ray.Origin - a,
            // a_ = d1.d1 (== 1, Direction is unit length - see Ray's own remarks),
            // b_ = d1.d2, c_ = d2.d2, d_ = d1.w0, e_ = d2.w0, the two closest-approach
            // parameters are s = (b_*e_ - c_*d_)/(a_*c_ - b_^2), t = (a_*e_ - b_*d_)/(a_*c_ - b_^2).
            var denominator = segmentLengthSquared - rayDotSegment * rayDotSegment;

            float rayParameter, segmentParameter;
            if (MathF.Abs(denominator) < 1e-9f)
            {
                // Ray and segment are (nearly) parallel - there's no single well-defined
                // closest pair of points along the segment's own direction, so measure
                // against the segment's start point (A) instead of dividing by ~0: the
                // ray parameter that lands closest to A specifically.
                rayParameter = -rayDotOriginToA;
                segmentParameter = 0f;
            }
            else
            {
                var inverseDenominator = 1f / denominator;
                rayParameter = (rayDotSegment * segmentDotOriginToA - segmentLengthSquared * rayDotOriginToA) * inverseDenominator;
                segmentParameter = (segmentDotOriginToA - rayDotSegment * rayDotOriginToA) * inverseDenominator;
            }

            segmentParameter = Math.Clamp(segmentParameter, 0f, 1f);

            var pointOnRay = ray.Origin + ray.Direction * rayParameter;
            var pointOnSegment = a + segmentVector * segmentParameter;
            var perpendicularDistance = Vector3.Distance(pointOnRay, pointOnSegment);

            return (rayParameter, perpendicularDistance);
        }

        /// <summary>The standard "slab" ray-vs-axis-aligned-bounding-box test - the
        /// nearest distance along <paramref name="ray"/> at which it enters the box
        /// defined by <paramref name="min"/>/<paramref name="max"/>, clamped to 0 (a ray
        /// whose own origin already starts inside the box "enters" at distance 0, not a
        /// negative one), or null if the ray misses the box entirely (including a box
        /// that's only reachable BEHIND the ray's own origin). What
        /// <c>Selection.SceneHitTester</c> uses to make a non-meshed <see cref="Scene.Node"/>
        /// (a camera/light, with no <see cref="Mesh"/> and therefore no triangles of its
        /// own to test against) still clickable in the viewport, via a small fixed-size
        /// box around its own world position.</summary>
        public static float? IntersectAABB(Ray ray, Vector3 min, Vector3 max)
        {
            var tMin = float.NegativeInfinity;
            var tMax = float.PositiveInfinity;

            for (var axis = 0; axis < 3; axis++)
            {
                var origin = AxisComponent(ray.Origin, axis);
                var direction = AxisComponent(ray.Direction, axis);
                var axisMin = AxisComponent(min, axis);
                var axisMax = AxisComponent(max, axis);

                if (MathF.Abs(direction) < 1e-9f)
                {
                    // The ray runs parallel to this pair of slab faces - it only ever
                    // hits the box if its own (constant, along this axis) position is
                    // already between them.
                    if (origin < axisMin || origin > axisMax) return null;
                    continue;
                }

                var t1 = (axisMin - origin) / direction;
                var t2 = (axisMax - origin) / direction;
                if (t1 > t2) (t1, t2) = (t2, t1);

                tMin = MathF.Max(tMin, t1);
                tMax = MathF.Min(tMax, t2);
                if (tMin > tMax) return null;
            }

            return tMax < 0f ? null : MathF.Max(tMin, 0f);
        }

        private static float AxisComponent(Vector3 v, int axis) => axis switch
        {
            0 => v.X,
            1 => v.Y,
            _ => v.Z,
        };
    }
}
