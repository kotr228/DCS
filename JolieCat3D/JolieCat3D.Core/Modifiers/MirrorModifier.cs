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
    /// Does not weld vertices that land exactly on the mirror plane (a real, disclosed
    /// simplification, matching this project's own established pattern of shipping the
    /// achievable common case rather than blocking on a full feature - most Mirror
    /// modifiers offer this as an optional, separate "Merge" setting anyway, not
    /// something a basic Mirror does unconditionally) - a vertex already sitting on the
    /// plane simply gets a coincident, functionally harmless duplicate in the mirrored
    /// half.
    /// </summary>
    public sealed class MirrorModifier : Modifier
    {
        public override string Name => "Mirror";

        public MirrorAxis Axis { get; set; } = MirrorAxis.X;

        public override Mesh Apply(Mesh input)
        {
            ArgumentNullException.ThrowIfNull(input);

            var output = new Mesh(input.Name) { Material = input.Material };

            // The original geometry, copied through unchanged.
            foreach (var vertex in input.Vertices) output.AddVertex(vertex);
            foreach (var face in input.Faces) output.AddFace(face);
            foreach (var polygon in input.Polygons) output.AddPolygon(new Polygon(polygon.Indices));

            // The mirrored copy - reflected positions, REVERSED winding (see this
            // class's own remarks on why), appended after an index offset. Normals are
            // recalculated fresh at the end (see below) rather than hand-reflected here,
            // so a placeholder (zero) normal is fine for now.
            var offset = input.Vertices.Count;
            foreach (var vertex in input.Vertices)
                output.AddVertex(vertex.WithPosition(Reflect(vertex.Position, Axis)).WithNormal(Vector3.Zero));

            foreach (var face in input.Faces)
                output.AddTriangle(face.C + offset, face.B + offset, face.A + offset);

            foreach (var polygon in input.Polygons)
            {
                var reversed = new int[polygon.Indices.Count];
                for (var i = 0; i < polygon.Indices.Count; i++)
                    reversed[i] = polygon.Indices[polygon.Indices.Count - 1 - i] + offset;
                output.AddPolygon(new Polygon(reversed));
            }

            output.RecalculateNormals();
            return output;
        }

        private static Vector3 Reflect(Vector3 position, MirrorAxis axis) => axis switch
        {
            MirrorAxis.X => new Vector3(-position.X, position.Y, position.Z),
            MirrorAxis.Y => new Vector3(position.X, -position.Y, position.Z),
            MirrorAxis.Z => new Vector3(position.X, position.Y, -position.Z),
            _ => throw new ArgumentOutOfRangeException(nameof(axis)),
        };
    }
}
