namespace JolieCat3D.Core.Geometry
{
    /// <summary>
    /// An authoring-time, arbitrary-sided face in a <see cref="Mesh"/>: an ordered list of
    /// indices into <see cref="Mesh.Vertices"/>, wound counter-clockwise the same way
    /// <see cref="Face"/> is. Modeling operations (quad grids, n-gon extrusion, ...) work
    /// naturally in terms of these; a GPU only ever draws triangles, so
    /// <see cref="Mesh.Triangulate"/> converts a mesh's <see cref="Mesh.Polygons"/> into
    /// <see cref="Face"/> triangles before <c>JolieCat3D.Engine</c> builds a
    /// <c>MeshGeometry3D</c> from them. A triangle or quad is just a <see cref="Polygon"/>
    /// with three or four indices - there's no separate "Quad" type.
    /// </summary>
    public sealed class Polygon
    {
        private readonly List<int> _indices;

        public IReadOnlyList<int> Indices => _indices;

        public Polygon(IEnumerable<int> indices)
        {
            ArgumentNullException.ThrowIfNull(indices);
            _indices = indices.ToList();
            if (_indices.Count < 3)
                throw new ArgumentException("A polygon needs at least 3 indices.", nameof(indices));
        }

        public Polygon(params int[] indices) : this((IEnumerable<int>)indices)
        {
        }

        /// <summary>
        /// Splits this polygon into <see cref="Indices"/>.Count - 2 triangles via simple
        /// fan triangulation - every triangle shares this polygon's first index as one
        /// corner. Correct and index-cheap for the convex n-gons authoring tools mostly
        /// produce (quads especially); a concave polygon can fan-triangulate into
        /// triangles that poke outside its own outline, which a full ear-clipping
        /// triangulator would avoid - not implemented here, since nothing in this project
        /// yet authors concave polygons.
        /// </summary>
        public IEnumerable<Face> Triangulate()
        {
            for (var i = 1; i < _indices.Count - 1; i++)
                yield return new Face(_indices[0], _indices[i], _indices[i + 1]);
        }
    }
}
