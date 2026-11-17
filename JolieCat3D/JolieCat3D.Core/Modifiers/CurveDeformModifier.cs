using System.Numerics;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.Core.Modifiers
{
    /// <summary>
    /// Curve modifier (Blender): bends the mesh along <see cref="Target"/>'s curve. The
    /// mesh's local X (in the curve object's space) becomes distance along the curve; Y and
    /// Z become offsets in a twist-free frame carried along it (parallel transport), so an
    /// Array of a road or rail segment follows the path smoothly. Past either end the
    /// curve continues straight along its end tangent.
    /// </summary>
    public sealed class CurveDeformModifier : Modifier
    {
        public override string Name => "Curve";

        /// <summary>A node with <see cref="Node.Curve"/>.</summary>
        public Node? Target { get; set; }

        public override Mesh Apply(Mesh input, Node? owner = null)
        {
            ArgumentNullException.ThrowIfNull(input);
            if (Target?.Curve is not { } curve || owner is null || ReferenceEquals(Target, owner)) return input;
            var path = curve.SampleCenterline();
            if (path.Count < 2) return input;

            var meshWorld = owner.GetWorldTransform();
            var curveWorld = Target.GetWorldTransform();
            if (!Matrix4x4.Invert(curveWorld, out var curveInverse) || !Matrix4x4.Invert(meshWorld, out var meshInverse)) return input;
            var meshToCurve = meshWorld * curveInverse;
            var curveToMesh = curveWorld * meshInverse;

            // Drop repeated points, then cumulative lengths and parallel-transport frames.
            var points = new List<Vector3> { path[0] };
            foreach (var p in path.Skip(1)) if (Vector3.DistanceSquared(p, points[^1]) > 1e-12f) points.Add(p);
            if (points.Count < 2) return input;
            var n = points.Count;
            var lengths = new float[n];
            var tangents = new Vector3[n];
            for (var i = 1; i < n; i++) lengths[i] = lengths[i - 1] + Vector3.Distance(points[i - 1], points[i]);
            for (var i = 0; i < n; i++)
            {
                var a = points[Math.Max(0, i - 1)];
                var b = points[Math.Min(n - 1, i + 1)];
                tangents[i] = Vector3.Normalize(b - a);
            }
            var ups = new Vector3[n];
            var up0 = Vector3.UnitY - tangents[0] * Vector3.Dot(Vector3.UnitY, tangents[0]);
            if (up0.LengthSquared() < 1e-6f) up0 = Vector3.UnitZ - tangents[0] * Vector3.Dot(Vector3.UnitZ, tangents[0]);
            ups[0] = Vector3.Normalize(up0);
            for (var i = 1; i < n; i++)
            {
                var rotation = RotationBetween(tangents[i - 1], tangents[i]);
                var u = Vector3.Transform(ups[i - 1], rotation);
                u -= tangents[i] * Vector3.Dot(u, tangents[i]);
                ups[i] = u.LengthSquared() > 1e-12f ? Vector3.Normalize(u) : ups[i - 1];
            }
            var total = lengths[^1];

            var output = input.Clone();
            for (var v = 0; v < input.Vertices.Count; v++)
            {
                var local = Vector3.Transform(input.Vertices[v].Position, meshToCurve);
                var d = local.X;
                Vector3 position, tangent, up;
                if (d <= 0)
                {
                    tangent = tangents[0]; up = ups[0];
                    position = points[0] + tangent * d;
                }
                else if (d >= total)
                {
                    tangent = tangents[^1]; up = ups[^1];
                    position = points[^1] + tangent * (d - total);
                }
                else
                {
                    var index = Array.BinarySearch(lengths, d);
                    if (index < 0) index = ~index - 1;
                    index = Math.Clamp(index, 0, n - 2);
                    var span = lengths[index + 1] - lengths[index];
                    var t = span > 1e-12f ? (d - lengths[index]) / span : 0f;
                    position = Vector3.Lerp(points[index], points[index + 1], t);
                    tangent = Vector3.Normalize(Vector3.Lerp(tangents[index], tangents[index + 1], t));
                    up = Vector3.Lerp(ups[index], ups[index + 1], t);
                    up = Vector3.Normalize(up - tangent * Vector3.Dot(up, tangent));
                }
                var side = Vector3.Cross(tangent, up); // X x Y = Z keeps handedness
                var bent = position + up * local.Y + side * local.Z;
                output.SetVertexPosition(v, Vector3.Transform(bent, curveToMesh));
            }
            output.RecalculateNormals();
            return output;
        }

        private static Quaternion RotationBetween(Vector3 from, Vector3 to)
        {
            var d = Vector3.Dot(from, to);
            if (d > 0.999999f) return Quaternion.Identity;
            if (d < -0.999999f)
            {
                var axis = Vector3.Cross(Vector3.UnitX, from);
                if (axis.LengthSquared() < 1e-6f) axis = Vector3.Cross(Vector3.UnitY, from);
                return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.PI);
            }
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(from, to)), MathF.Acos(Math.Clamp(d, -1f, 1f)));
        }

        public override Modifier Clone() => new CurveDeformModifier { IsEnabled = IsEnabled, Target = Target };
    }
}
