using System.Numerics;
using JolieCat3D.Core.Geometry;

namespace JolieCat3D.Core.Modifiers
{
    /// <summary>
    /// One level of true Catmull-Clark subdivision - unlike <see cref="Mesh.Subdivide"/>
    /// (which only ever adds geometry along existing edges/faces with no repositioning
    /// at all - see its own remarks), this actually SMOOTHS the surface: every new
    /// vertex position is a weighted blend of the surrounding geometry (the classic
    /// "cube becomes rounder" result), the whole point of a modeling tool's own
    /// "Subdivision Surface" modifier. Deliberately a separate algorithm from
    /// <see cref="Mesh.Subdivide"/>, not an option added to it - that method is Edit
    /// Mode's own destructive, permanent "Subdivide" action (already relied upon and
    /// verified elsewhere), while this one only ever runs non-destructively through
    /// <see cref="SubdivisionSurfaceModifier"/>.
    ///
    /// Implements the standard algorithm (every <see cref="Mesh.Faces"/> triangle and
    /// <see cref="Mesh.Polygons"/> n-gon treated uniformly as just "a face"):
    /// <list type="number">
    /// <item><description>Face point: the average of each face's own vertices.</description></item>
    /// <item><description>Edge point: for an INTERIOR edge (shared by 2 faces), the
    /// average of its own 2 endpoints and its 2 neighboring face points (4-point
    /// average); for a BOUNDARY edge (only 1 face touches it - e.g. a Plane primitive's
    /// outer border), just the plain midpoint of its 2 endpoints.</description></item>
    /// <item><description>New vertex point: for an interior vertex, the classic
    /// <c>(F + 2R + (n-3)P) / n</c> weighted blend (F = average of face points of every
    /// face touching it, R = average of the plain MIDPOINTS of every edge touching it, P
    /// = its own original position, n = its valence); for a boundary vertex with exactly
    /// 2 boundary edges, the standard 1/8-3/4-1/8 boundary curve rule; any other boundary
    /// vertex (a non-manifold junction, more/fewer than 2 boundary edges) is left in
    /// place rather than guessing.</description></item>
    /// <item><description>Topology: each original n-gon face becomes n new quads (corner,
    /// following edge point, face point, preceding edge point) - the same quad-split
    /// shape <see cref="Mesh.Subdivide"/> already uses for its own n-gons, just with
    /// smoothed rather than linear positions.</description></item>
    /// </list>
    /// Assumes a 2-manifold input (every edge touches at most 2 faces; no "bowtie"
    /// vertices) - the only shape <c>Geometry.Primitives</c> or an OBJ/STL import ever
    /// produces in practice.
    /// </summary>
    public static class CatmullClarkSubdivider
    {
        public static Mesh Subdivide(Mesh input)
        {
            ArgumentNullException.ThrowIfNull(input);

            var faces = new List<IReadOnlyList<int>>();
            foreach (var face in input.Faces) faces.Add(new[] { face.A, face.B, face.C });
            foreach (var polygon in input.Polygons) faces.Add(polygon.Indices);

            var output = new Mesh(input.Name) { Material = input.Material };

            if (faces.Count == 0)
            {
                // Nothing to subdivide (an empty mesh, or one authored with no faces at
                // all) - pass the vertices through unchanged rather than producing an
                // empty result from a non-empty input.
                foreach (var vertex in input.Vertices) output.AddVertex(vertex);
                return output;
            }

            var positions = new Vector3[input.Vertices.Count];
            for (var i = 0; i < input.Vertices.Count; i++) positions[i] = input.Vertices[i].Position;

            // ---- Face points ----
            var facePoints = new Vector3[faces.Count];
            for (var f = 0; f < faces.Count; f++)
            {
                var sum = Vector3.Zero;
                foreach (var index in faces[f]) sum += positions[index];
                facePoints[f] = sum / faces[f].Count;
            }

            // ---- Edge adjacency: which face(s) touch each edge ----
            var edgeFaces = new Dictionary<(int A, int B), List<int>>();
            for (var f = 0; f < faces.Count; f++)
            {
                var face = faces[f];
                for (var i = 0; i < face.Count; i++)
                {
                    var key = EdgeKey(face[i], face[(i + 1) % face.Count]);
                    if (!edgeFaces.TryGetValue(key, out var list)) edgeFaces[key] = list = new List<int>();
                    list.Add(f);
                }
            }

            // ---- Edge midpoints (plain) and edge points (smoothed) ----
            var edgeMidpoints = new Dictionary<(int A, int B), Vector3>();
            var edgePoints = new Dictionary<(int A, int B), Vector3>();
            foreach (var (key, touchingFaces) in edgeFaces)
            {
                var midpoint = (positions[key.A] + positions[key.B]) / 2f;
                edgeMidpoints[key] = midpoint;

                edgePoints[key] = touchingFaces.Count >= 2
                    ? (positions[key.A] + positions[key.B] + facePoints[touchingFaces[0]] + facePoints[touchingFaces[1]]) / 4f
                    : midpoint; // boundary edge - no face-point blending
            }

            // ---- Per-vertex incident faces/edges ----
            var vertexFaces = new List<int>[positions.Length];
            var vertexEdges = new List<(int A, int B)>[positions.Length];
            for (var i = 0; i < positions.Length; i++)
            {
                vertexFaces[i] = new List<int>();
                vertexEdges[i] = new List<(int, int)>();
            }

            for (var f = 0; f < faces.Count; f++)
                foreach (var index in faces[f]) vertexFaces[index].Add(f);

            foreach (var key in edgeFaces.Keys)
            {
                vertexEdges[key.A].Add(key);
                vertexEdges[key.B].Add(key);
            }

            // ---- New (smoothed) vertex positions ----
            var newPositions = new Vector3[positions.Length];
            for (var v = 0; v < positions.Length; v++)
            {
                var incidentEdges = vertexEdges[v];
                var boundaryEdges = incidentEdges.Where(e => edgeFaces[e].Count < 2).ToList();

                if (boundaryEdges.Count > 0)
                {
                    // Boundary vertex - the standard cubic-B-spline boundary curve rule
                    // needs EXACTLY 2 boundary edges to apply; anything else (a corner
                    // touched by more, a non-manifold junction) is left in place.
                    newPositions[v] = boundaryEdges.Count == 2
                        ? positions[v] * 0.75f + (edgeMidpoints[boundaryEdges[0]] + edgeMidpoints[boundaryEdges[1]]) * 0.125f
                        : positions[v];
                    continue;
                }

                if (incidentEdges.Count == 0)
                {
                    newPositions[v] = positions[v]; // an isolated vertex nothing touches
                    continue;
                }

                var faceAverage = Vector3.Zero;
                foreach (var f in vertexFaces[v]) faceAverage += facePoints[f];
                faceAverage /= vertexFaces[v].Count;

                var edgeMidpointAverage = Vector3.Zero;
                foreach (var e in incidentEdges) edgeMidpointAverage += edgeMidpoints[e];
                edgeMidpointAverage /= incidentEdges.Count;

                var n = incidentEdges.Count; // valence - equals the incident face count for an interior manifold vertex
                newPositions[v] = (faceAverage + edgeMidpointAverage * 2f + positions[v] * (n - 3)) / n;
            }

            // ---- Assemble the output mesh: smoothed original vertices, then face points, then edge points ----
            var originalOutputIndex = new int[positions.Length];
            for (var v = 0; v < positions.Length; v++)
                originalOutputIndex[v] = output.AddVertex(input.Vertices[v].WithPosition(newPositions[v]));

            var facePointOutputIndex = new int[faces.Count];
            for (var f = 0; f < faces.Count; f++)
                facePointOutputIndex[f] = output.AddVertex(new Vertex(facePoints[f]));

            var edgePointOutputIndex = new Dictionary<(int A, int B), int>();
            foreach (var (key, point) in edgePoints)
                edgePointOutputIndex[key] = output.AddVertex(new Vertex(point));

            for (var f = 0; f < faces.Count; f++)
            {
                var face = faces[f];
                var n = face.Count;
                for (var i = 0; i < n; i++)
                {
                    var current = face[i];
                    var next = face[(i + 1) % n];
                    var previous = face[(i - 1 + n) % n];

                    output.AddQuad(
                        originalOutputIndex[current],
                        edgePointOutputIndex[EdgeKey(current, next)],
                        facePointOutputIndex[f],
                        edgePointOutputIndex[EdgeKey(previous, current)]);
                }
            }

            output.RecalculateNormals();
            return output;
        }

        private static (int A, int B) EdgeKey(int a, int b) => a < b ? (a, b) : (b, a);
    }
}
