using System.Numerics;
using System.Linq;

namespace JolieCat3D.Core.Geometry
{
    /// <summary>
    /// Sweeps a circular cross-section of <see cref="GenerateTube"/>'s own
    /// <c>radius</c> along an already-sampled centerline (see
    /// <see cref="Scene.CurveData.SampleCenterline"/>, which turns a Bezier control-point
    /// chain into the flat polyline this class actually consumes) - the "Geometry: Bevel
    /// Depth" tube/cable mesh a <see cref="Scene.CurveData"/> renders as. Kept
    /// independent of <see cref="Scene.CurveData"/> itself (plain
    /// <c>IReadOnlyList&lt;Vector3&gt;</c> in, plain <see cref="Mesh"/> out) so it can be
    /// exercised directly from a standalone script against the real compiled assembly,
    /// the same "small, independently testable, no Scene/Node coupling" shape
    /// <see cref="UVProjector"/> and <see cref="CsgSolid"/> already have.
    ///
    /// The cross-section's own orientation is carried along the centerline via a
    /// rotation-minimizing frame (the "double reflection" method - Wang, Jüttler, Zheng
    /// &amp; Liu, 2008): starting from an arbitrary-but-valid frame at the first sample,
    /// each next frame is derived from the previous one by two reflections (through the
    /// midpoint plane of the position step, then through the midpoint plane of the
    /// tangent change) rather than by, say, re-deriving "right" from a fixed world "up"
    /// at every sample - the latter would visibly SNAP/twist the cross-section wherever
    /// the tangent direction crosses that fixed reference (e.g. a curve that passes
    /// through straight-up), while double reflection introduces no unnecessary twist at
    /// all, exactly the property a smoothly-swept cable/tube needs.
    /// </summary>
    public static class CurveMesher
    {
        public static Mesh GenerateTube(IReadOnlyList<Vector3> centerline, float radius, int radialSegments, bool closed)
        {
            ArgumentNullException.ThrowIfNull(centerline);

            var mesh = new Mesh();
            var points = DedupeConsecutive(centerline, closed);
            if (points.Count < 2) return mesh;

            radius = MathF.Max(radius, 1e-4f);
            radialSegments = Math.Max(3, radialSegments);

            var tangents = ComputeTangents(points, closed);
            var (rights, ups) = ComputeRotationMinimizingFrames(points, tangents);

            var n = points.Count;
            var ringIndices = new int[n][];
            for (var i = 0; i < n; i++)
            {
                ringIndices[i] = new int[radialSegments];
                for (var j = 0; j < radialSegments; j++)
                {
                    var angle = 2f * MathF.PI * j / radialSegments;
                    var offset = radius * (MathF.Cos(angle) * rights[i] + MathF.Sin(angle) * ups[i]);
                    ringIndices[i][j] = mesh.AddVertex(new Vertex(points[i] + offset, Vector3.Normalize(offset)));
                }
            }

            // Side walls - see this class's own remarks for the concrete, hand-verified
            // (cross-product-checked against a specific tangent=+Z/right=+X/up=+Y test
            // case) reasoning behind this exact vertex order: AddQuad(ring i @ angle j,
            // ring i @ angle j+1, ring i+1 @ angle j+1, ring i+1 @ angle j) is the one
            // that points radially OUTWARD given up = cross(tangent, right) below.
            var ringCount = closed ? n : n - 1;
            for (var i = 0; i < ringCount; i++)
            {
                var next = (i + 1) % n;
                for (var j = 0; j < radialSegments; j++)
                {
                    var jNext = (j + 1) % radialSegments;
                    mesh.AddQuad(ringIndices[i][j], ringIndices[i][jNext], ringIndices[next][jNext], ringIndices[next][j]);
                }
            }

            if (!closed)
            {
                AddCap(mesh, points[0], rights[0], ups[0], radius, radialSegments, -tangents[0]);
                AddCap(mesh, points[n - 1], rights[n - 1], ups[n - 1], radius, radialSegments, tangents[n - 1]);
            }

            return mesh;
        }

        /// <summary>A flat N-gon cap, facing <paramref name="outwardNormal"/> - its own,
        /// separate vertex set (axial normal, not the side walls' radial one) rather
        /// than reusing the matching ring's own vertices, the same "duplicate vertices
        /// for a differently-shaded cap" convention <see cref="Primitives.CreateCylinder"/>'s
        /// own <c>AddCylinderCap</c> already uses.</summary>
        private static void AddCap(Mesh mesh, Vector3 center, Vector3 right, Vector3 up, float radius, int radialSegments, Vector3 outwardNormal)
        {
            var rim = new int[radialSegments];
            for (var j = 0; j < radialSegments; j++)
            {
                var angle = 2f * MathF.PI * j / radialSegments;
                var offset = radius * (MathF.Cos(angle) * right + MathF.Sin(angle) * up);
                rim[j] = mesh.AddVertex(new Vertex(center + offset, outwardNormal));
            }

            // Natural increasing-angle order winds toward +tangent (see this class's own
            // remarks); reverse it when the cap should instead face -tangent (the
            // start-of-tube cap).
            var faceForward = Vector3.Dot(outwardNormal, Vector3.Cross(right, up)) > 0f;
            mesh.AddPolygon(new Polygon(faceForward ? rim : rim.Reverse()));
        }

        /// <summary>Central-difference tangent estimate at every sample (clamped to the
        /// nearest valid neighbor at the two open ends, wrapped around for
        /// <paramref name="closed"/>) - the direction <see cref="GenerateTube"/>'s own
        /// cross-section frame at that sample should point straight through.</summary>
        private static Vector3[] ComputeTangents(IReadOnlyList<Vector3> points, bool closed)
        {
            var n = points.Count;
            var tangents = new Vector3[n];
            for (var i = 0; i < n; i++)
            {
                Vector3 prev, next;
                if (closed)
                {
                    prev = points[(i - 1 + n) % n];
                    next = points[(i + 1) % n];
                }
                else
                {
                    prev = points[Math.Max(0, i - 1)];
                    next = points[Math.Min(n - 1, i + 1)];
                }

                var direction = next - prev;
                tangents[i] = direction.LengthSquared() > 1e-12f ? Vector3.Normalize(direction) : Vector3.UnitZ;
            }

            return tangents;
        }

        /// <summary>The double-reflection rotation-minimizing frame propagation - see
        /// this class's own remarks. Returns, per sample, a unit "right" vector
        /// perpendicular to that sample's own tangent, and "up" = cross(tangent, right)
        /// completing a right-handed (tangent, right, up) basis (verified by hand via
        /// <see cref="GenerateTube"/>'s own side-wall winding derivation, for exactly
        /// this cross-product relationship).</summary>
        private static (Vector3[] Rights, Vector3[] Ups) ComputeRotationMinimizingFrames(IReadOnlyList<Vector3> points, Vector3[] tangents)
        {
            var n = points.Count;
            var rights = new Vector3[n];
            var ups = new Vector3[n];

            var t0 = tangents[0];
            var arbitrary = MathF.Abs(Vector3.Dot(t0, Vector3.UnitY)) < 0.99f ? Vector3.UnitY : Vector3.UnitX;
            var right0 = Vector3.Cross(arbitrary, t0);
            if (right0.LengthSquared() < 1e-8f) right0 = Vector3.Cross(Vector3.UnitX, t0);
            right0 = Vector3.Normalize(right0);

            rights[0] = right0;
            ups[0] = Vector3.Normalize(Vector3.Cross(t0, right0));

            for (var i = 1; i < n; i++)
            {
                var v1 = points[i] - points[i - 1];
                var c1 = Vector3.Dot(v1, v1);

                var previousRight = rights[i - 1];
                var previousTangent = tangents[i - 1];

                Vector3 reflectedRight, reflectedTangent;
                if (c1 > 1e-12f)
                {
                    reflectedRight = previousRight - 2f / c1 * Vector3.Dot(v1, previousRight) * v1;
                    reflectedTangent = previousTangent - 2f / c1 * Vector3.Dot(v1, previousTangent) * v1;
                }
                else
                {
                    reflectedRight = previousRight;
                    reflectedTangent = previousTangent;
                }

                var currentTangent = tangents[i];
                var v2 = currentTangent - reflectedTangent;
                var c2 = Vector3.Dot(v2, v2);
                var right = c2 > 1e-12f
                    ? reflectedRight - 2f / c2 * Vector3.Dot(v2, reflectedRight) * v2
                    : reflectedRight;

                // Re-orthonormalize against the ACTUAL current tangent - the reflection
                // math already keeps this near-exact, but this guards against float
                // drift accumulating over a long centerline.
                right = Vector3.Normalize(right - Vector3.Dot(right, currentTangent) * currentTangent);

                rights[i] = right;
                ups[i] = Vector3.Normalize(Vector3.Cross(currentTangent, right));
            }

            return (rights, ups);
        }

        /// <summary>Drops any sample that sits (within float tolerance) on top of the
        /// PREVIOUS one kept - a degenerate, zero-length centerline segment would
        /// otherwise divide-by-zero <see cref="ComputeTangents"/>'s own direction
        /// normalization. <paramref name="closed"/> also checks the wrap-around
        /// last-to-first segment.</summary>
        private static List<Vector3> DedupeConsecutive(IReadOnlyList<Vector3> points, bool closed)
        {
            var result = new List<Vector3>();
            foreach (var point in points)
                if (result.Count == 0 || Vector3.DistanceSquared(result[^1], point) > 1e-12f)
                    result.Add(point);

            if (closed && result.Count > 1 && Vector3.DistanceSquared(result[^1], result[0]) <= 1e-12f)
                result.RemoveAt(result.Count - 1);

            return result;
        }
    }
}
