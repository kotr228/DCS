using System.Numerics;
using JolieCat3D.Core.Geometry;

namespace JolieCat3D.Core.Modifiers
{
    /// <summary>
    /// One Catmull-Clark subdivision step (any polygons in, all quads out).
    ///
    /// Geometry works on CORNERS - vertices welded by position - so meshes whose vertices
    /// are split for UV seams or hard normals (e.g. the cube primitive) still subdivide as
    /// one closed surface, like Blender. Attributes stay on the split vertices: a new
    /// edge/face point is shared by the faces that shared the original vertex indices, and
    /// split again where they were split; UVs/colours are averaged accordingly.
    ///
    /// Open boundaries and creased edges (<see cref="Mesh.CreaseEdges"/>) follow the
    /// B-spline curve rule (sharp); a vertex with 3+ such edges stays put (corner); a
    /// single crease ending at a vertex (dart) leaves it smooth. Creases carry over to
    /// both halves of each split edge so multi-level subdivision stays sharp.
    /// </summary>
    public static class CatmullClarkSubdivider
    {
        public static Mesh Subdivide(Mesh input)
        {
            ArgumentNullException.ThrowIfNull(input);

            var faces = new List<IReadOnlyList<int>>();
            var faceSlots = new List<int>();
            foreach (var face in input.Faces) { faces.Add(new[] { face.A, face.B, face.C }); faceSlots.Add(-1); }
            foreach (var polygon in input.Polygons) { faces.Add(polygon.Indices); faceSlots.Add(polygon.MaterialSlotIndex); }

            var output = new Mesh(input.Name) { Material = input.Material };
            foreach (var slot in input.MaterialSlots) output.AddMaterialSlot(slot);

            if (faces.Count == 0)
            {
                foreach (var vertex in input.Vertices) output.AddVertex(vertex);
                return output;
            }

            // ---- corners: vertices welded by position ----
            var cornerOf = new int[input.Vertices.Count];
            var cornerIds = new Dictionary<(int, int, int), int>();
            var positions = new List<Vector3>();
            for (var i = 0; i < input.Vertices.Count; i++)
            {
                var key = MeshOperators.Key(input.Vertices[i].Position);
                if (!cornerIds.TryGetValue(key, out var id)) { id = positions.Count; cornerIds[key] = id; positions.Add(input.Vertices[i].Position); }
                cornerOf[i] = id;
            }
            var cornerCount = positions.Count;

            static (int A, int B) EdgeKey(int a, int b) => a < b ? (a, b) : (b, a);

            var creased = new HashSet<(int, int)>();
            foreach (var (a, b) in input.CreaseEdges)
                if ((uint)a < (uint)cornerOf.Length && (uint)b < (uint)cornerOf.Length) creased.Add(EdgeKey(cornerOf[a], cornerOf[b]));

            // ---- face points ----
            var facePoints = new Vector3[faces.Count];
            for (var f = 0; f < faces.Count; f++)
            {
                var sum = Vector3.Zero;
                foreach (var index in faces[f]) sum += positions[cornerOf[index]];
                facePoints[f] = sum / faces[f].Count;
            }

            // ---- edge adjacency (by corner) ----
            var edgeFaces = new Dictionary<(int A, int B), List<int>>();
            for (var f = 0; f < faces.Count; f++)
            {
                var face = faces[f];
                for (var i = 0; i < face.Count; i++)
                {
                    var key = EdgeKey(cornerOf[face[i]], cornerOf[face[(i + 1) % face.Count]]);
                    if (key.A == key.B) continue;
                    if (!edgeFaces.TryGetValue(key, out var list)) edgeFaces[key] = list = new List<int>();
                    list.Add(f);
                }
            }

            bool IsSharp((int A, int B) e) => edgeFaces[e].Count != 2 || creased.Contains(e);

            var edgeMidpoints = new Dictionary<(int A, int B), Vector3>();
            var edgePoints = new Dictionary<(int A, int B), Vector3>();
            foreach (var (key, touching) in edgeFaces)
            {
                var midpoint = (positions[key.A] + positions[key.B]) / 2f;
                edgeMidpoints[key] = midpoint;
                edgePoints[key] = IsSharp(key)
                    ? midpoint
                    : (positions[key.A] + positions[key.B] + facePoints[touching[0]] + facePoints[touching[1]]) / 4f;
            }

            // ---- corner rules ----
            var cornerFaces = new List<int>[cornerCount];
            var cornerEdges = new List<(int A, int B)>[cornerCount];
            for (var c = 0; c < cornerCount; c++) { cornerFaces[c] = new List<int>(); cornerEdges[c] = new List<(int, int)>(); }
            for (var f = 0; f < faces.Count; f++)
                foreach (var c in faces[f].Select(i => cornerOf[i]).Distinct()) cornerFaces[c].Add(f);
            foreach (var key in edgeFaces.Keys) { cornerEdges[key.A].Add(key); cornerEdges[key.B].Add(key); }

            var newPositions = new Vector3[cornerCount];
            for (var c = 0; c < cornerCount; c++)
            {
                var incident = cornerEdges[c];
                if (incident.Count == 0) { newPositions[c] = positions[c]; continue; }
                var sharp = incident.Where(IsSharp).ToList();
                var isDart = sharp.Count == 1 && edgeFaces[sharp[0]].Count == 2;
                if (sharp.Count >= 2 && !isDart)
                {
                    newPositions[c] = sharp.Count == 2
                        ? positions[c] * 0.75f + (edgeMidpoints[sharp[0]] + edgeMidpoints[sharp[1]]) * 0.125f
                        : positions[c]; // corner
                    continue;
                }
                if (sharp.Count == 1 && !isDart) { newPositions[c] = positions[c]; continue; }

                var faceAverage = Vector3.Zero;
                foreach (var f in cornerFaces[c]) faceAverage += facePoints[f];
                faceAverage /= cornerFaces[c].Count;
                var midAverage = Vector3.Zero;
                foreach (var e in incident) midAverage += edgeMidpoints[e];
                midAverage /= incident.Count;
                var n = incident.Count;
                newPositions[c] = (faceAverage + midAverage * 2f + positions[c] * (n - 3)) / n;
            }

            // ---- output vertices (attributes follow the original split vertices) ----
            var cornerVertex = new Dictionary<int, int>(); // original vertex -> output
            int CornerOut(int v)
            {
                if (!cornerVertex.TryGetValue(v, out var o))
                    cornerVertex[v] = o = output.AddVertex(input.Vertices[v].WithPosition(newPositions[cornerOf[v]]));
                return o;
            }

            var edgeVertex = new Dictionary<(int, int), int>(); // (original vertex pair) -> output
            int EdgeOut(int va, int vb)
            {
                var key = EdgeKey(va, vb);
                if (edgeVertex.TryGetValue(key, out var o)) return o;
                var a = input.Vertices[va];
                var b = input.Vertices[vb];
                var point = edgePoints[EdgeKey(cornerOf[va], cornerOf[vb])];
                o = output.AddVertex(new Vertex(point, Vector3.Normalize(a.Normal + b.Normal + new Vector3(1e-9f)), (a.UV + b.UV) * 0.5f,
                    new Numerics.Color4((a.Color.R + b.Color.R) / 2, (a.Color.G + b.Color.G) / 2, (a.Color.B + b.Color.B) / 2, (a.Color.A + b.Color.A) / 2),
                    a.BoneIndices, a.BoneWeights));
                edgeVertex[key] = o;
                return o;
            }

            for (var f = 0; f < faces.Count; f++)
            {
                var face = faces[f];
                var n = face.Count;
                var uv = Vector2.Zero;
                foreach (var i in face) uv += input.Vertices[i].UV;
                var first = input.Vertices[face[0]];
                var faceVertex = output.AddVertex(new Vertex(facePoints[f], first.Normal, uv / n, first.Color, first.BoneIndices, first.BoneWeights));
                for (var i = 0; i < n; i++)
                {
                    var current = face[i];
                    var next = face[(i + 1) % n];
                    var previous = face[(i - 1 + n) % n];
                    if (cornerOf[current] == cornerOf[next] || cornerOf[current] == cornerOf[previous]) continue; // degenerate edge
                    output.AddPolygon(new Polygon(new[] { CornerOut(current), EdgeOut(current, next), faceVertex, EdgeOut(previous, current) })
                    {
                        MaterialSlotIndex = faceSlots[f],
                    });
                }
            }

            // ---- creases carry over to both halves of every split crease edge ----
            foreach (var (a, b) in input.CreaseEdges)
            {
                if (!edgeVertex.TryGetValue(EdgeKey(a, b), out var mid)) continue;
                if (cornerVertex.TryGetValue(a, out var oa)) output.MarkCrease(oa, mid);
                if (cornerVertex.TryGetValue(b, out var ob)) output.MarkCrease(mid, ob);
            }
            // Creases marked on one side of a split edge apply to every split copy of it.
            foreach (var (va, vb) in edgeVertex.Keys)
            {
                if (!creased.Contains(EdgeKey(cornerOf[va], cornerOf[vb])) || input.IsCrease(va, vb)) continue;
                var mid = edgeVertex[(va, vb)];
                if (cornerVertex.TryGetValue(va, out var oa)) output.MarkCrease(oa, mid);
                if (cornerVertex.TryGetValue(vb, out var ob)) output.MarkCrease(mid, ob);
            }

            output.RecalculateNormals();
            return output;
        }
    }
}
