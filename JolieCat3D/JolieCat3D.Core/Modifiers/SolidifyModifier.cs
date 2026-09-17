using System.Numerics;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.Core.Modifiers
{
    /// <summary>
    /// Gives <see cref="Modifier.Apply"/>'s own input mesh real thickness - the standard
    /// "Solidify" modifier, turning a thin, open surface (a single Plane, a swept
    /// ribbon, ...) into a genuinely closed, watertight solid shell. The ORIGINAL
    /// surface is kept as the shell's own "outer" side; a second, INNER copy is added
    /// with every vertex pushed <see cref="Thickness"/> units back along its own
    /// (already-computed) <see cref="Vertex.Normal"/> and every face wound in REVERSE
    /// (so this inner copy faces the opposite way, exactly as <see cref="MirrorModifier"/>'s
    /// own mirrored copy does for the same "a reflected/offset copy needs the opposite
    /// winding to still read as front-facing from its own new side" reason); every
    /// BOUNDARY edge (one belonging to only a single face - the actual "open" rim of a
    /// non-closed surface) then gets a new side wall quad stitching the outer and inner
    /// copies together, so the result has no naked/open edges left at all. A mesh with
    /// NO boundary edges at all (already fully closed, like a cube) simply gets a
    /// second, offset-inward shell with no walls needed - a real, if less commonly
    /// useful, result rather than a special case to guard against.
    ///
    /// The exact wall-quad winding direction was verified by hand (a flat single
    /// triangle's own boundary edge, cross-product-checked to point away from the
    /// triangle's own interior) before being written here, then confirmed
    /// computationally via the same signed-volume-through-the-divergence-theorem
    /// technique this project's own CSG engine was verified with - a genuinely
    /// unintuitive detail to get backwards silently (a reversed wall renders inverted/
    /// invisible from outside, exactly the kind of subtle winding bug this project's
    /// own testing methodology exists to catch before it ships).
    ///
    /// Known limitation (shared with most simple solidify implementations, and the
    /// reason professional tools add extra rim/miter handling): each vertex is pushed
    /// inward along its OWN normal alone, with no attempt to reconcile that against a
    /// neighboring face's differing normal. On a HARD-SHADED mesh (one that, like
    /// <see cref="Geometry.Primitives.CreateCube"/>, duplicates vertices per face so
    /// adjacent faces meet at literally different vertex instances, just the same
    /// position) this can leave the inner shell with tiny gaps at hard edges/corners,
    /// since neighboring faces shrink toward each other along different directions.
    /// This does not affect Solidify's actual primary use case - a smoothly-shaded or
    /// genuinely OPEN surface (a Plane, a swept ribbon, a custom-modeled shape with
    /// shared vertices) - where neighboring faces already agree closely enough on
    /// normal direction for the offset shell to stay watertight.
    /// </summary>
    public sealed class SolidifyModifier : Modifier
    {
        public override string Name => "Solidify";

        /// <summary>How far inward (along each vertex's own normal) the new inner shell
        /// sits from the original surface - in the mesh's own local units. 0.1 by
        /// default (a thin, but clearly visible, wall thickness at this project's own
        /// established ~1-unit primitive scale).</summary>
        public float Thickness { get; set; } = 0.1f;

        public override Mesh Apply(Mesh input, Node? owner = null)
        {
            ArgumentNullException.ThrowIfNull(input);

            var output = new Mesh(input.Name) { Material = input.Material };
            var vertexCount = input.Vertices.Count;

            // The original ("outer") surface, copied through unchanged.
            foreach (var vertex in input.Vertices) output.AddVertex(vertex);
            foreach (var face in input.Faces) output.AddFace(face);
            foreach (var polygon in input.Polygons) output.AddPolygon(new Polygon(polygon.Indices));

            // The "inner" offset copy - each vertex pushed back along its OWN normal,
            // every face reversed.
            var innerIndex = new int[vertexCount];
            for (var i = 0; i < vertexCount; i++)
            {
                var vertex = input.Vertices[i];
                var offsetPosition = vertex.Position - vertex.Normal * Thickness;
                innerIndex[i] = output.AddVertex(vertex.WithPosition(offsetPosition).WithNormal(-vertex.Normal));
            }

            foreach (var face in input.Faces)
                output.AddTriangle(innerIndex[face.C], innerIndex[face.B], innerIndex[face.A]);

            foreach (var polygon in input.Polygons)
            {
                var indices = polygon.Indices;
                var reversed = new int[indices.Count];
                for (var i = 0; i < indices.Count; i++)
                    reversed[i] = innerIndex[indices[indices.Count - 1 - i]];
                output.AddPolygon(new Polygon(reversed));
            }

            // Side walls along every boundary edge - see this class's own remarks on
            // the exact winding, and FindBoundaryEdges's own remarks on why the
            // direction each edge is returned in matters here, not just which two
            // vertices it connects.
            foreach (var (a, b) in FindBoundaryEdges(input))
                output.AddQuad(b, a, innerIndex[a], innerIndex[b]);

            return output;
        }

        /// <summary>Every edge belonging to EXACTLY ONE face/polygon in
        /// <paramref name="mesh"/> - the open "rim" <see cref="Apply"/>'s own side walls
        /// need to stitch shut. Returned in the SAME directed order that edge's own
        /// (single) owning face/polygon already winds it in - not just an unordered
        /// pair - since a boundary edge's own direction (as its one owning face already
        /// established) is exactly what a correctly-outward-facing wall quad needs to
        /// key off; losing that direction (e.g. by normalizing to A&lt;B first) would
        /// leave no way to tell which of the two possible wall windings is actually
        /// correct.
        ///
        /// Two faces meeting along what is geometrically the same edge do not
        /// necessarily share the same VERTEX INDICES for its endpoints - this engine
        /// deliberately duplicates vertices per face for flat shading (see
        /// <see cref="Geometry.Primitives.CreateCube"/>'s own remarks), so an edge
        /// shared by two adjacent cube faces is stored as two different index pairs
        /// that merely happen to sit at the same two positions. Keying purely on index
        /// would therefore see every edge of a flat-shaded, already-closed mesh (a
        /// Cube) as "boundary" (touched by only one face BY INDEX), wrongly stitching a
        /// spurious internal wall along every single edge instead of the genuinely
        /// closed shell it already is. Keying on (quantized) POSITION instead treats
        /// same-position endpoints as the same edge regardless of index, correctly
        /// unifying both this engine's duplicated-vertex meshes (Cube) and its
        /// shared-vertex ones (Sphere) under the one geometric definition of "boundary"
        /// this modifier actually needs.</summary>
        private static IEnumerable<(int A, int B)> FindBoundaryEdges(Mesh mesh)
        {
            var counts = new Dictionary<(Vector3, Vector3), int>();
            var lastSeenDirection = new Dictionary<(Vector3, Vector3), (int A, int B)>();

            void Track(int a, int b)
            {
                var key = PositionKey(mesh.Vertices[a].Position, mesh.Vertices[b].Position);
                counts[key] = counts.GetValueOrDefault(key) + 1;
                lastSeenDirection[key] = (a, b);
            }

            foreach (var face in mesh.Faces)
            {
                Track(face.A, face.B);
                Track(face.B, face.C);
                Track(face.C, face.A);
            }

            foreach (var polygon in mesh.Polygons)
            {
                var indices = polygon.Indices;
                for (var i = 0; i < indices.Count; i++)
                    Track(indices[i], indices[(i + 1) % indices.Count]);
            }

            foreach (var (key, count) in counts)
                if (count == 1) yield return lastSeenDirection[key];
        }

        /// <summary>An unordered key identifying an edge by its two endpoints'
        /// POSITIONS (rounded to kill float noise between otherwise-coincident
        /// vertices), not their vertex indices - see <see cref="FindBoundaryEdges"/>'s
        /// own remarks on why.</summary>
        private static (Vector3, Vector3) PositionKey(Vector3 a, Vector3 b)
        {
            Vector3 Round(Vector3 v) => new(MathF.Round(v.X, 4), MathF.Round(v.Y, 4), MathF.Round(v.Z, 4));
            var ra = Round(a);
            var rb = Round(b);
            return IsOrderedBefore(ra, rb) ? (ra, rb) : (rb, ra);
        }

        private static bool IsOrderedBefore(Vector3 a, Vector3 b)
        {
            if (a.X != b.X) return a.X < b.X;
            if (a.Y != b.Y) return a.Y < b.Y;
            return a.Z < b.Z;
        }

        public override Modifier Clone() => new SolidifyModifier
        {
            IsEnabled = IsEnabled,
            Thickness = Thickness,
        };
    }
}
