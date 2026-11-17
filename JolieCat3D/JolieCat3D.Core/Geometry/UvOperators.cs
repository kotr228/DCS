using System.Numerics;

namespace JolieCat3D.Core.Geometry
{
    /// <summary>
    /// UV editing helpers for the UV Editor: islands (UV-connected face groups), island
    /// packing into the 0..1 square, and transforming a set of UVs. UV seams are vertices
    /// split by index, so an island is a set of faces connected through shared indices.
    /// </summary>
    public static class UvOperators
    {
        /// <summary>Island id per vertex (-1 for vertices no face uses).</summary>
        public static int[] Islands(Mesh mesh)
        {
            var parent = Enumerable.Range(0, mesh.Vertices.Count).ToArray();
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }

            var used = new bool[mesh.Vertices.Count];
            foreach (var f in mesh.GetRenderFaces())
            {
                Union(f.A, f.B); Union(f.A, f.C);
                used[f.A] = used[f.B] = used[f.C] = true;
            }

            var ids = new Dictionary<int, int>();
            var result = new int[mesh.Vertices.Count];
            for (var i = 0; i < result.Length; i++)
            {
                if (!used[i]) { result[i] = -1; continue; }
                var root = Find(i);
                if (!ids.TryGetValue(root, out var id)) ids[root] = id = ids.Count;
                result[i] = id;
            }
            return result;
        }

        /// <summary>Every vertex in the same island as any of <paramref name="seed"/>.</summary>
        public static HashSet<int> SelectIslands(Mesh mesh, IEnumerable<int> seed)
        {
            var islands = Islands(mesh);
            var wanted = seed.Where(i => (uint)i < (uint)islands.Length && islands[i] >= 0).Select(i => islands[i]).ToHashSet();
            return Enumerable.Range(0, islands.Length).Where(i => islands[i] >= 0 && wanted.Contains(islands[i])).ToHashSet();
        }

        /// <summary>Applies <paramref name="transform"/> (in UV space) to the given vertices' UVs.</summary>
        public static void Transform(Mesh mesh, IEnumerable<int> vertices, Func<Vector2, Vector2> transform)
        {
            foreach (var i in vertices)
                if ((uint)i < (uint)mesh.Vertices.Count) mesh.SetVertexUV(i, transform(mesh.Vertices[i].UV));
        }

        /// <summary>
        /// Packs all islands into the 0..1 square without overlap (shelf packing of their
        /// bounding boxes, keeping their relative sizes), with <paramref name="margin"/>
        /// between islands. Returns the number of islands packed.
        /// </summary>
        public static int PackIslands(Mesh mesh, float margin = 0.01f)
        {
            var islands = Islands(mesh);
            var groups = Enumerable.Range(0, islands.Length).Where(i => islands[i] >= 0).GroupBy(i => islands[i]).Select(g => g.ToList()).ToList();
            return PackGroups(mesh, groups, margin);
        }

        /// <summary>Packs the given vertex groups (each moved as one rigid island).</summary>
        public static int PackGroups(Mesh mesh, IReadOnlyList<List<int>> groups, float margin = 0.01f)
        {
            if (groups.Count == 0) return 0;

            var boxes = groups.Select(g =>
            {
                var min = new Vector2(float.MaxValue);
                var max = new Vector2(float.MinValue);
                foreach (var i in g) { min = Vector2.Min(min, mesh.Vertices[i].UV); max = Vector2.Max(max, mesh.Vertices[i].UV); }
                var size = Vector2.Max(max - min, new Vector2(1e-4f));
                return (Vertices: g, Min: min, Size: size);
            }).OrderByDescending(b => b.Size.Y).ToList();

            // Try a range of shelf widths and keep the layout that allows the largest scale.
            var maxBoxWidth = boxes.Max(b => b.Size.X + margin);
            var totalWidth = boxes.Sum(b => b.Size.X + margin);
            List<(List<int> Vertices, Vector2 Min, Vector2 Offset)>? placements = null;
            float usedWidth = 0, usedHeight = 0, bestScale = 0;
            for (var step = 0; step <= 24; step++)
            {
                var shelfWidth = maxBoxWidth + (totalWidth - maxBoxWidth) * step / 24f;
                var candidate = new List<(List<int>, Vector2, Vector2)>();
                float x = 0, y = 0, rowHeight = 0, width = 0;
                foreach (var box in boxes)
                {
                    if (x > 0 && x + box.Size.X > shelfWidth + 1e-6f)
                    {
                        x = 0;
                        y += rowHeight + margin;
                        rowHeight = 0;
                    }
                    candidate.Add((box.Vertices, box.Min, new Vector2(x, y)));
                    x += box.Size.X + margin;
                    width = MathF.Max(width, x - margin);
                    rowHeight = MathF.Max(rowHeight, box.Size.Y);
                }
                var height = y + rowHeight;
                var candidateScale = (1f - 2 * margin) / MathF.Max(width, height);
                if (candidateScale > bestScale)
                {
                    bestScale = candidateScale;
                    placements = candidate;
                    usedWidth = width;
                    usedHeight = height;
                }
            }

            // Uniform scale so the packed block fits 0..1 with a margin border.
            var scale = bestScale;
            foreach (var (vertices, min, offset) in placements!)
                foreach (var i in vertices)
                    mesh.SetVertexUV(i, new Vector2(margin, margin) + (offset + (mesh.Vertices[i].UV - min)) * scale);

            return groups.Count;
        }

        /// <summary>True when two UV triangles of different islands overlap (for tests/diagnostics).</summary>
        public static bool IslandsOverlap(Mesh mesh)
        {
            var islands = Islands(mesh);
            var faces = mesh.GetRenderFaces().ToList();
            for (var i = 0; i < faces.Count; i++)
            for (var j = i + 1; j < faces.Count; j++)
            {
                if (islands[faces[i].A] == islands[faces[j].A]) continue;
                if (TrianglesOverlap(Uv(mesh, faces[i]), Uv(mesh, faces[j]))) return true;
            }
            return false;

            static Vector2[] Uv(Mesh m, Face f) => new[] { m.Vertices[f.A].UV, m.Vertices[f.B].UV, m.Vertices[f.C].UV };
        }

        private static bool TrianglesOverlap(Vector2[] a, Vector2[] b)
        {
            // Separating axis test on the 6 edge normals (strict, so touching edges don't count).
            foreach (var tri in new[] { a, b })
            for (var e = 0; e < 3; e++)
            {
                var edge = tri[(e + 1) % 3] - tri[e];
                var axis = new Vector2(-edge.Y, edge.X);
                float aMin = float.MaxValue, aMax = float.MinValue, bMin = float.MaxValue, bMax = float.MinValue;
                foreach (var p in a) { var d = Vector2.Dot(p, axis); aMin = MathF.Min(aMin, d); aMax = MathF.Max(aMax, d); }
                foreach (var p in b) { var d = Vector2.Dot(p, axis); bMin = MathF.Min(bMin, d); bMax = MathF.Max(bMax, d); }
                if (aMax <= bMin + 1e-6f || bMax <= aMin + 1e-6f) return false;
            }
            return true;
        }
    }
}
