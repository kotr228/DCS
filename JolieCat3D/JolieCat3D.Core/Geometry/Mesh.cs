using System.Numerics;
using JolieCat3D.Core.Materials;
using JolieCat3D.Core.Numerics;

namespace JolieCat3D.Core.Geometry
{
    /// <summary>
    /// A named collection of <see cref="Vertex"/> plus the <see cref="Face"/> triangles
    /// (and/or authoring-time <see cref="Polygon"/> n-gons) connecting them, with an
    /// optional <see cref="Material"/> reference for how it should be shaded. The
    /// platform-agnostic 3D geometry type this whole project is built around -
    /// <c>JolieCat3D.Engine</c>'s renderer reads a Mesh and produces a WPF
    /// <c>MeshGeometry3D</c> from it; nothing in here references WPF, Windows, or any
    /// rendering API at all, so a Mesh can be built, loaded, or edited (in
    /// <c>JolieCat3D.Core</c> or <c>JolieCat3D.Service</c>) with no GPU or UI framework
    /// in the process at all.
    /// </summary>
    public sealed class Mesh
    {
        private readonly List<Vertex> _vertices = new();
        private readonly List<Face> _faces = new();
        private readonly List<Polygon> _polygons = new();

        public string Name { get; set; }

        /// <summary>Optional - a mesh with no material renders with <see cref="Material.CreateDefault"/> instead.</summary>
        public Material? Material { get; set; }

        public IReadOnlyList<Vertex> Vertices => _vertices;

        /// <summary>Ready-to-render triangles, referencing <see cref="Vertices"/> by index.</summary>
        public IReadOnlyList<Face> Faces => _faces;

        /// <summary>Authoring-time n-sided faces (see <see cref="Polygon"/>'s own remarks)
        /// - not rendered directly; <see cref="GetRenderFaces"/> triangulates them
        /// alongside <see cref="Faces"/> for anything that actually needs triangles.</summary>
        public IReadOnlyList<Polygon> Polygons => _polygons;

        public Mesh(string name = "Mesh") => Name = name;

        public int AddVertex(Vertex vertex)
        {
            _vertices.Add(vertex);
            return _vertices.Count - 1;
        }

        public void AddFace(Face face) => _faces.Add(face);

        public void AddTriangle(int a, int b, int c) => AddFace(new Face(a, b, c));

        /// <summary>Adds a quad as a <see cref="Polygon"/> (not two immediately-split
        /// triangles) so it round-trips as one 4-sided face for anything downstream that
        /// wants to reason about the quad itself, not its arbitrary triangulation -
        /// <see cref="GetRenderFaces"/> still triangulates it for actual rendering.</summary>
        public void AddQuad(int a, int b, int c, int d) => _polygons.Add(new Polygon(a, b, c, d));

        public void AddPolygon(Polygon polygon)
        {
            ArgumentNullException.ThrowIfNull(polygon);
            _polygons.Add(polygon);
        }

        /// <summary>Replaces the position of the vertex at <paramref name="index"/> in
        /// place, keeping its existing normal/UV/color - the one way to move an
        /// existing vertex without re-adding it (see <see cref="Vertex"/>'s own remarks
        /// on why it's otherwise an immutable struct). Used by
        /// <c>JolieCat3D.Engine.Editing.MeshEditSession</c> to drag selected vertices in
        /// Edit Mode. Deliberately does not recalculate normals itself - a caller moving
        /// several vertices in one drag should call <see cref="RecalculateNormals"/>
        /// once afterward, not once per vertex.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a valid vertex index.</exception>
        public void SetVertexPosition(int index, Vector3 position)
        {
            if (index < 0 || index >= _vertices.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            _vertices[index] = _vertices[index].WithPosition(position);
        }

        /// <summary>Every distinct edge in this mesh: a deduplicated (normalized so
        /// A &lt; B - the same edge shared by two adjacent faces is reported once, not
        /// twice) unordered pair of vertex indices, derived from <see cref="Faces"/> and
        /// <see cref="Polygons"/> (each face/polygon contributes the edge between every
        /// pair of consecutive corners, wrapping back to its first). This mesh has no
        /// separate "edge" data structure of its own - only faces/polygons that imply
        /// them - so <c>JolieCat3D.Engine.Editing.ComponentHitTester</c> (Edge-mode
        /// picking) and the edge-overlay marker visual both call this rather than
        /// walking <see cref="Faces"/>/<see cref="Polygons"/> themselves.</summary>
        public IEnumerable<(int A, int B)> GetEdges()
        {
            var seen = new HashSet<(int, int)>();

            static IEnumerable<(int, int)> EdgesOf(IReadOnlyList<int> indices)
            {
                for (var i = 0; i < indices.Count; i++)
                {
                    var a = indices[i];
                    var b = indices[(i + 1) % indices.Count];
                    yield return a < b ? (a, b) : (b, a);
                }
            }

            foreach (var face in _faces)
                foreach (var edge in EdgesOf(new[] { face.A, face.B, face.C }))
                    if (seen.Add(edge)) yield return edge;

            foreach (var polygon in _polygons)
                foreach (var edge in EdgesOf(polygon.Indices))
                    if (seen.Add(edge)) yield return edge;
        }

        /// <summary>Every triangle this mesh should render as: <see cref="Faces"/>
        /// verbatim, plus every <see cref="Polygon"/> in <see cref="Polygons"/>
        /// fan-triangulated (see <see cref="Polygon.Triangulate"/>). The one method
        /// <c>JolieCat3D.Engine</c>'s geometry adapter actually calls - it never needs to
        /// know Face/Polygon are two different representations at all.</summary>
        public IEnumerable<Face> GetRenderFaces()
        {
            foreach (var face in _faces) yield return face;
            foreach (var polygon in _polygons)
                foreach (var triangle in polygon.Triangulate())
                    yield return triangle;
        }

        /// <summary>The axis-aligned bounding box of every vertex position, in this
        /// mesh's own local space - <c>(Vector3.Zero, Vector3.Zero)</c> for an empty mesh.
        /// Used by <c>JolieCat3D.Engine</c>'s camera helper to frame a scene automatically.</summary>
        public (Vector3 Min, Vector3 Max) GetBounds()
        {
            if (_vertices.Count == 0) return (Vector3.Zero, Vector3.Zero);

            var min = _vertices[0].Position;
            var max = min;

            foreach (var vertex in _vertices)
            {
                min = Vector3.Min(min, vertex.Position);
                max = Vector3.Max(max, vertex.Position);
            }

            return (min, max);
        }

        /// <summary>
        /// Extrudes <paramref name="face"/> (which must already be one of this mesh's own
        /// <see cref="Polygons"/>) outward along its own face normal by
        /// <paramref name="distance"/>: duplicates its vertices into a new ring offset
        /// along that normal, connects the original ring to the new one with a side quad
        /// per edge, replaces the original polygon with the new offset one as the cap, and
        /// recalculates normals for the whole mesh afterward. Returns the new cap polygon,
        /// so a caller can chain a further operation onto the freshly-extruded face (an
        /// extrude-then-extrude "tower", for instance) the same way a modeling tool's own
        /// "Extrude" leaves the new face selected. The original face's own vertices are
        /// left untouched and still exactly where they were - anything else in the mesh
        /// that shares them (a neighboring face on the rest of the object) is unaffected;
        /// only the new side quads and cap are new geometry. Verified (vertex/polygon
        /// counts, and that every resulting face still winds outward) against a
        /// hand-built cube in a throwaway console script before being written here.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="face"/> is not one of
        /// this mesh's own <see cref="Polygons"/>.</exception>
        public Polygon ExtrudeFace(Polygon face, float distance)
        {
            ArgumentNullException.ThrowIfNull(face);
            if (!_polygons.Remove(face))
                throw new ArgumentException("The given polygon is not part of this mesh.", nameof(face));

            var indices = face.Indices;
            var normal = ComputeFaceNormal(indices);
            var offset = normal * distance;

            var newIndices = new int[indices.Count];
            for (var i = 0; i < indices.Count; i++)
            {
                var original = _vertices[indices[i]];
                newIndices[i] = AddVertex(original.WithPosition(original.Position + offset).WithNormal(normal));
            }

            for (var i = 0; i < indices.Count; i++)
            {
                var next = (i + 1) % indices.Count;
                // (old[i], old[next], new[next], new[i]) - this exact order, verified
                // numerically (every resulting side quad winds outward on a hand-built
                // cube) before being written here; the seemingly-equivalent
                // (old[i], new[i], new[next], old[next]) is actually its reverse and
                // winds every side quad inward instead.
                AddQuad(indices[i], indices[next], newIndices[next], newIndices[i]);
            }

            var capPolygon = new Polygon(newIndices);
            AddPolygon(capPolygon);

            RecalculateNormals();
            return capPolygon;
        }

        /// <summary>
        /// Replaces every triangle in <see cref="Faces"/> with 4 smaller ones (split at
        /// its own 3 edge midpoints) and every n-gon in <see cref="Polygons"/> with N
        /// quads (one per edge, meeting at a new center vertex) - the standard "linear"
        /// mesh subdivision (no Catmull-Clark-style smoothing/re-positioning of existing
        /// vertices, just adding new geometry along existing edges/faces). Operates on
        /// the whole mesh, not a per-face selection - there's no concept of a partial
        /// selection in <c>JolieCat3D.Core</c> itself (that's a viewport/UI concern), so
        /// "Subdivide" here means "subdivide everything", the same way a modeling tool's
        /// own Subdivide button behaves with nothing specific selected. An edge shared by
        /// two faces gets exactly one shared midpoint vertex (not two, one per face) -
        /// verified against a hand-built shared-vertex cube in a throwaway console script
        /// (26 vertices - 8 original + 12 shared edge midpoints + 6 face centers - not 38,
        /// which is what an unshared/duplicated version would have produced) before being
        /// written here, so the subdivided mesh has no seams or cracks between faces that
        /// used to share an edge. Recalculates normals afterward.
        /// </summary>
        public void Subdivide()
        {
            var edgeMidpoints = new Dictionary<(int, int), int>();

            int GetOrCreateMidpoint(int a, int b)
            {
                var key = a < b ? (a, b) : (b, a);
                if (edgeMidpoints.TryGetValue(key, out var existing)) return existing;

                var va = _vertices[a];
                var vb = _vertices[b];
                var midpointNormal = va.Normal + vb.Normal;
                var midpoint = new Vertex(
                    (va.Position + vb.Position) / 2f,
                    midpointNormal.LengthSquared() > float.Epsilon ? Vector3.Normalize(midpointNormal) : va.Normal,
                    (va.UV + vb.UV) / 2f,
                    Color4.Lerp(va.Color, vb.Color, 0.5f));

                var index = AddVertex(midpoint);
                edgeMidpoints[key] = index;
                return index;
            }

            var oldFaces = _faces.ToList();
            var oldPolygons = _polygons.ToList();
            _faces.Clear();
            _polygons.Clear();

            foreach (var face in oldFaces)
            {
                var m01 = GetOrCreateMidpoint(face.A, face.B);
                var m12 = GetOrCreateMidpoint(face.B, face.C);
                var m20 = GetOrCreateMidpoint(face.C, face.A);

                AddTriangle(face.A, m01, m20);
                AddTriangle(m01, face.B, m12);
                AddTriangle(m20, m12, face.C);
                AddTriangle(m01, m12, m20);
            }

            foreach (var polygon in oldPolygons)
            {
                var indices = polygon.Indices;
                var n = indices.Count;

                var midpoints = new int[n];
                for (var i = 0; i < n; i++)
                    midpoints[i] = GetOrCreateMidpoint(indices[i], indices[(i + 1) % n]);

                var centerPosition = Vector3.Zero;
                var centerNormal = Vector3.Zero;
                var centerUV = Vector2.Zero;
                foreach (var index in indices)
                {
                    centerPosition += _vertices[index].Position;
                    centerNormal += _vertices[index].Normal;
                    centerUV += _vertices[index].UV;
                }
                centerPosition /= n;
                centerNormal = centerNormal.LengthSquared() > float.Epsilon ? Vector3.Normalize(centerNormal) : Vector3.UnitY;
                centerUV /= n;

                var centerIndex = AddVertex(new Vertex(centerPosition, centerNormal, centerUV));

                for (var i = 0; i < n; i++)
                {
                    var previous = (i - 1 + n) % n;
                    // New quad: original corner -> edge-midpoint after it -> face center
                    // -> edge-midpoint before it - preserves the original polygon's own
                    // winding direction.
                    AddQuad(indices[i], midpoints[i], centerIndex, midpoints[previous]);
                }
            }

            RecalculateNormals();
        }

        /// <summary>Newell's method - the face normal of an arbitrary (possibly
        /// non-planar or non-triangular) polygon, robust where a plain 3-point cross
        /// product isn't. Matches this codebase's existing winding convention
        /// (<see cref="RecalculateNormals"/>'s own <c>Cross(b-a, c-a)</c> per triangle) -
        /// verified against it (a simple CCW-from-+Z square) before being relied on here.</summary>
        private Vector3 ComputeFaceNormal(IReadOnlyList<int> indices)
        {
            var normal = Vector3.Zero;
            for (var i = 0; i < indices.Count; i++)
            {
                var current = _vertices[indices[i]].Position;
                var next = _vertices[indices[(i + 1) % indices.Count]].Position;
                normal.X += (current.Y - next.Y) * (current.Z + next.Z);
                normal.Y += (current.Z - next.Z) * (current.X + next.X);
                normal.Z += (current.X - next.X) * (current.Y + next.Y);
            }

            return normal.LengthSquared() > float.Epsilon ? Vector3.Normalize(normal) : Vector3.UnitY;
        }

        /// <summary>
        /// Recomputes every vertex's <see cref="Vertex.Normal"/> as the normalized
        /// average of the face normals of every triangle (from <see cref="GetRenderFaces"/>)
        /// it participates in - the standard smooth-shading normal, for a mesh built
        /// (like <c>Primitives</c>'s helpers) or edited without normals of its own.
        /// Replaces <see cref="Vertices"/> in place.
        /// </summary>
        public void RecalculateNormals()
        {
            var accumulated = new Vector3[_vertices.Count];

            foreach (var face in GetRenderFaces())
            {
                var a = _vertices[face.A].Position;
                var b = _vertices[face.B].Position;
                var c = _vertices[face.C].Position;

                var faceNormal = Vector3.Cross(b - a, c - a);
                // A degenerate (zero-area) triangle contributes nothing rather than
                // polluting its vertices' normals with a NaN from normalizing a zero vector.
                if (faceNormal.LengthSquared() < float.Epsilon) continue;

                accumulated[face.A] += faceNormal;
                accumulated[face.B] += faceNormal;
                accumulated[face.C] += faceNormal;
            }

            for (var i = 0; i < _vertices.Count; i++)
            {
                var normal = accumulated[i].LengthSquared() > float.Epsilon
                    ? Vector3.Normalize(accumulated[i])
                    : Vector3.UnitY;
                _vertices[i] = _vertices[i].WithNormal(normal);
            }
        }
    }
}
