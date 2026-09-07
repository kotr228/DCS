using System.Numerics;
using JolieCat3D.Core.Materials;

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
