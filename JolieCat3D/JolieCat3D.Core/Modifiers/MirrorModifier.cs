using System.Numerics;
using JolieCat3D.Core.Geometry;

namespace JolieCat3D.Core.Modifiers
{
    /// <summary>
    /// Reflects <see cref="Modifier.Apply"/>'s input mesh across the plane through its
    /// own local origin perpendicular to <see cref="Axis"/>, keeping BOTH the original
    /// geometry and the mirrored copy (the standard modeling-tool "Mirror" modifier
    /// workflow of modeling one half of a symmetric object and letting the modifier
    /// generate the other half) - not a destructive flip that replaces the original
    /// half. Mirrored faces are wound in REVERSED order (verified empirically - see this
    /// project's own scratch-script-first methodology - before being written here):
    /// reflecting a mesh flips its handedness, so copying a face's vertex order
    /// unchanged onto mirrored positions would wind every mirrored face the WRONG way,
    /// making it render back-to-front/inside-out from the correct side.
    ///
    /// A vertex already within <see cref="WeldThreshold"/> of the mirror plane is
    /// WELDED - the mirrored half reuses that exact same vertex instead of adding a
    /// coincident duplicate - the standard Mirror modifier's own "Merge"/"Clip" behavior
    /// for the seam where a model's own cut edge sits flush on the mirror axis (the
    /// common authoring convention: model one half with its boundary edge exactly on the
    /// origin plane, and the modifier seams it shut with no duplicate/gap). A face whose
    /// every vertex welds this way (the whole face lies ON the plane) has its mirrored
    /// copy skipped entirely rather than added as a zero-area duplicate - the original,
    /// unmirrored copy of that face (added regardless, before any welding decision) is
    /// already exactly where it needs to be.
    /// </summary>
    public sealed class MirrorModifier : Modifier
    {
        public override string Name => "Mirror";

        public MirrorAxis Axis { get; set; } = MirrorAxis.X;

        /// <summary>How close (in the mesh's own local units) a vertex's own
        /// mirror-axis coordinate must be to zero to be treated as "on the plane" and
        /// welded rather than duplicated - see this class's own remarks. Small by
        /// default (a vertex has to be deliberately modeled at, or extremely near,
        /// exactly the axis for this to trigger at all) so an ordinary asymmetric mesh
        /// mirrors with no welding surprises; raise it for a mesh whose seam vertices
        /// are close to, but not exactly at, zero (the usual case after a few edits or
        /// an imported file's own floating-point rounding).</summary>
        public float WeldThreshold { get; set; } = 0.0001f;

        public override Mesh Apply(Mesh input)
        {
            ArgumentNullException.ThrowIfNull(input);

            var output = new Mesh(input.Name) { Material = input.Material };

            // The original geometry, copied through unchanged.
            foreach (var vertex in input.Vertices) output.AddVertex(vertex);
            foreach (var face in input.Faces) output.AddFace(face);
            foreach (var polygon in input.Polygons) output.AddPolygon(new Polygon(polygon.Indices));

            // Decide, per ORIGINAL vertex, whether the mirrored half welds to it (an
            // on-plane vertex - mirroredIndex[i] == i, no new vertex added at all) or
            // gets its own new, reflected vertex (mirroredIndex[i] == that new vertex's
            // own index). Normals are recalculated fresh at the end (see below) rather
            // than hand-reflected here, so a placeholder (zero) normal is fine for now.
            var mirroredIndex = new int[input.Vertices.Count];
            for (var i = 0; i < input.Vertices.Count; i++)
            {
                var position = input.Vertices[i].Position;
                if (MathF.Abs(AxisComponent(position, Axis)) <= WeldThreshold)
                {
                    mirroredIndex[i] = i;
                }
                else
                {
                    var mirroredPosition = Reflect(position, Axis);
                    mirroredIndex[i] = output.AddVertex(input.Vertices[i].WithPosition(mirroredPosition).WithNormal(Vector3.Zero));
                }
            }

            foreach (var face in input.Faces)
            {
                var a = mirroredIndex[face.A];
                var b = mirroredIndex[face.B];
                var c = mirroredIndex[face.C];
                if (HasDuplicateIndex(a, b, c)) continue; // the whole face welded to itself - see this class's own remarks

                output.AddTriangle(c, b, a); // reversed order - see this class's own remarks on winding
            }

            foreach (var polygon in input.Polygons)
            {
                var reversed = new int[polygon.Indices.Count];
                for (var i = 0; i < polygon.Indices.Count; i++)
                    reversed[i] = mirroredIndex[polygon.Indices[polygon.Indices.Count - 1 - i]];

                if (HasDuplicateIndex(reversed)) continue;
                output.AddPolygon(new Polygon(reversed));
            }

            output.RecalculateNormals();
            return output;
        }

        private static float AxisComponent(Vector3 position, MirrorAxis axis) => axis switch
        {
            MirrorAxis.X => position.X,
            MirrorAxis.Y => position.Y,
            MirrorAxis.Z => position.Z,
            _ => throw new ArgumentOutOfRangeException(nameof(axis)),
        };

        private static Vector3 Reflect(Vector3 position, MirrorAxis axis) => axis switch
        {
            MirrorAxis.X => new Vector3(-position.X, position.Y, position.Z),
            MirrorAxis.Y => new Vector3(position.X, -position.Y, position.Z),
            MirrorAxis.Z => new Vector3(position.X, position.Y, -position.Z),
            _ => throw new ArgumentOutOfRangeException(nameof(axis)),
        };

        private static bool HasDuplicateIndex(int a, int b, int c) => a == b || b == c || a == c;

        private static bool HasDuplicateIndex(IReadOnlyList<int> indices)
        {
            for (var i = 0; i < indices.Count; i++)
                for (var j = i + 1; j < indices.Count; j++)
                    if (indices[i] == indices[j]) return true;
            return false;
        }
    }
}
