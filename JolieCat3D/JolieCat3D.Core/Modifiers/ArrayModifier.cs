using System.Numerics;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.Core.Modifiers
{
    /// <summary>
    /// Replicates <see cref="Modifier.Apply"/>'s own input mesh <see cref="Count"/>
    /// times, offsetting each successive copy further along a cumulative translation -
    /// the standard modeling-tool "Array" modifier, used to (for instance) model one
    /// fence post/railing spindle/staircase step and let the modifier generate the rest.
    /// <see cref="RelativeOffset"/> is expressed as a FRACTION of the input mesh's own
    /// local-space bounding-box size per axis (Blender's own "Relative Offset" - as
    /// opposed to a "Constant Offset" in absolute world units, which this modifier does
    /// not implement) - a <see cref="RelativeOffset"/> of (1,0,0) places each successive
    /// copy exactly one mesh-width further along X, so a row of identically-sized
    /// objects tiles edge-to-edge with no gap or overlap regardless of the mesh's own
    /// actual size.
    /// </summary>
    public sealed class ArrayModifier : Modifier
    {
        public override string Name => "Array";

        /// <summary>How many total copies to produce - clamped to at least 1 (a Count
        /// of 1 is a harmless no-op: the plain input mesh, unchanged, copied through
        /// once). Not clamped on the high end - an unreasonably large value is the
        /// caller's own problem to notice (the same "no artificial ceiling" latitude
        /// <c>SubdivisionSurfaceModifier.Iterations</c> already gives), though the UI
        /// this is wired to keeps a sane default.</summary>
        public int Count { get; set; } = 3;

        /// <summary>The offset BETWEEN each successive copy, as a fraction of the input
        /// mesh's own bounding-box size per axis - see this class's own remarks. (1,0,0)
        /// by default: a row of copies laid out edge-to-edge along X, the single most
        /// common Array use (a fence, a row of steps, ...).</summary>
        public Vector3 RelativeOffset { get; set; } = new(1f, 0f, 0f);

        public override Mesh Apply(Mesh input, Node? owner = null)
        {
            ArgumentNullException.ThrowIfNull(input);

            var count = Math.Max(1, Count);
            var output = new Mesh(input.Name) { Material = input.Material };

            if (count == 1 || input.Vertices.Count == 0)
            {
                foreach (var vertex in input.Vertices) output.AddVertex(vertex);
                foreach (var face in input.Faces) output.AddFace(face);
                foreach (var polygon in input.Polygons) output.AddPolygon(new Polygon(polygon.Indices));
                return output;
            }

            var (min, max) = input.GetBounds();
            var size = max - min;
            var offsetPerCopy = new Vector3(RelativeOffset.X * size.X, RelativeOffset.Y * size.Y, RelativeOffset.Z * size.Z);

            for (var copyIndex = 0; copyIndex < count; copyIndex++)
            {
                var translation = offsetPerCopy * copyIndex;
                var vertexOffset = output.Vertices.Count;

                // A plain translation changes no direction at all - every vertex's own
                // normal carries across completely unchanged, so there is no need for a
                // RecalculateNormals() pass afterward the way Mirror's own REFLECTED
                // copy needs.
                foreach (var vertex in input.Vertices)
                    output.AddVertex(vertex.WithPosition(vertex.Position + translation));

                foreach (var face in input.Faces)
                    output.AddTriangle(face.A + vertexOffset, face.B + vertexOffset, face.C + vertexOffset);

                foreach (var polygon in input.Polygons)
                    output.AddPolygon(new Polygon(polygon.Indices.Select(index => index + vertexOffset)));
            }

            return output;
        }

        public override Modifier Clone() => new ArrayModifier
        {
            IsEnabled = IsEnabled,
            Count = Count,
            RelativeOffset = RelativeOffset,
        };
    }
}
