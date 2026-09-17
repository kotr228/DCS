using System.Numerics;

namespace JolieCat3D.Core.Geometry
{
    /// <summary>
    /// Builds a bone's own small visual/selectable shape from its Head to its Tail - the
    /// classic "octahedron" bone shape most 3D tools draw (a thin pyramid from Head to a
    /// waist ring partway up, then another thin pyramid from that waist ring to Tail).
    /// This exists purely so a bone (a <see cref="Scene.Node"/> with <see cref="Scene.BoneData"/>)
    /// has SOMETHING for <c>JolieCat3D.Engine.Geometry.SceneGraphBuilder</c> to build a
    /// <c>GeometryModel3D</c> from at all - without a <see cref="Scene.Node.Mesh"/>, a
    /// node is invisible AND unclickable in the viewport (see
    /// <c>Engine.Selection.SceneHitTester</c>'s own <c>modelToNode</c> mapping, which is
    /// only ever populated for a meshed node), exactly the same reason
    /// <see cref="Scene.CurveData.GenerateMesh"/> exists for a curve node. Kept
    /// independent of <see cref="Scene.BoneData"/>/<see cref="Scene.Node"/> themselves
    /// (plain <see cref="Vector3"/> in, plain <see cref="Mesh"/> out) so it can be
    /// exercised directly from a standalone script, the same "small, independently
    /// testable" shape <see cref="CurveMesher"/> already has.
    /// </summary>
    public static class BoneMesher
    {
        public static Mesh CreateOctahedron(Vector3 head, Vector3 tail)
        {
            var mesh = new Mesh();

            var direction = tail - head;
            var length = direction.Length();

            // A degenerate (zero-length) bone still needs a well-defined, non-NaN shape
            // to render - falls back to a small nominal +Y bone rather than collapsing
            // every vertex onto the same point.
            if (length < 1e-6f)
            {
                direction = Vector3.UnitY;
                length = 0.1f;
                tail = head + direction * length;
            }
            else
            {
                direction /= length;
            }

            var arbitrary = MathF.Abs(Vector3.Dot(direction, Vector3.UnitY)) < 0.99f ? Vector3.UnitY : Vector3.UnitX;
            var right = Vector3.Normalize(Vector3.Cross(arbitrary, direction));
            var up = Vector3.Normalize(Vector3.Cross(direction, right));

            var waistDistance = length * 0.1f;
            var waistRadius = length * 0.1f;
            var waistCenter = head + direction * waistDistance;

            var headIndex = mesh.AddVertex(new Vertex(head));
            var tailIndex = mesh.AddVertex(new Vertex(tail));

            var waist = new int[4];
            for (var i = 0; i < 4; i++)
            {
                var angle = i * MathF.PI / 2f;
                var offset = waistRadius * (MathF.Cos(angle) * right + MathF.Sin(angle) * up);
                waist[i] = mesh.AddVertex(new Vertex(waistCenter + offset));
            }

            for (var i = 0; i < 4; i++)
            {
                var next = (i + 1) % 4;
                // Verified winding (outward-facing, both pyramids) via a computational
                // signed-volume check, the same discipline this project's own
                // Solidify/CurveMesher wall-winding derivations already used - a naive
                // "just connect Head/Tail to the waist ring in ring order" guess is
                // exactly the kind of thing that comes out backwards on one of the two
                // pyramids without checking.
                mesh.AddTriangle(headIndex, waist[next], waist[i]);
                mesh.AddTriangle(tailIndex, waist[i], waist[next]);
            }

            mesh.RecalculateNormals();
            return mesh;
        }
    }
}
