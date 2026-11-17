using System.Numerics;

namespace JolieCat3D.Core.Geometry
{
    /// <summary>
    /// Blender-style "Unwrap" (angle-preserving): cuts the mesh into UV islands along its
    /// seams, flattens each island with Least Squares Conformal Maps (Lévy et al. 2002),
    /// scales every island to the same texel density, then packs them into 0..1.
    ///
    /// Islands are found by position (split-normal vertices sharing a position count as
    /// one corner, like the other operators); seams are matched by position too. A vertex
    /// index used by several islands is duplicated so each island owns its UVs. Seams are
    /// kept (remapped onto the new indices) so the mesh can be unwrapped again.
    /// </summary>
    public static class LscmUnwrapper
    {
        /// <summary>Unwraps <paramref name="mesh"/> in place. Returns the number of islands.
        /// A closed island (no boundary to flatten from, e.g. a seamless sphere) is split
        /// Smart-UV-Project style first.</summary>
        public static int Unwrap(Mesh mesh, float margin = 0.01f) => Run(mesh, margin, smartAngleDegrees: null);

        /// <summary>
        /// Smart UV Project: ignores seams and groups faces whose normals stay within
        /// <paramref name="angleLimitDegrees"/> of their island's first face, then flattens
        /// and packs like <see cref="Unwrap"/>. Works on any mesh without marking seams.
        /// </summary>
        public static int SmartProject(Mesh mesh, float angleLimitDegrees = 66f, float margin = 0.01f) =>
            Run(mesh, margin, Math.Clamp(angleLimitDegrees, 1f, 89f));

        private static int Run(Mesh mesh, float margin, float? smartAngleDegrees)
        {
            ArgumentNullException.ThrowIfNull(mesh);

            // ---- faces as corner lists ----
            var faces = new List<int[]>();
            var faceSlot = new List<int>();
            var faceIsTriangle = new List<bool>();
            foreach (var f in mesh.Faces) { faces.Add(new[] { f.A, f.B, f.C }); faceSlot.Add(-1); faceIsTriangle.Add(true); }
            foreach (var p in mesh.Polygons) { faces.Add(p.Indices.ToArray()); faceSlot.Add(p.MaterialSlotIndex); faceIsTriangle.Add(false); }
            if (faces.Count == 0) return 0;

            var key = new (int, int, int)[mesh.Vertices.Count];
            for (var i = 0; i < key.Length; i++) key[i] = MeshOperators.Key(mesh.Vertices[i].Position);

            static ((int, int, int), (int, int, int)) EdgeKey((int, int, int) a, (int, int, int) b) =>
                Comparer<(int, int, int)>.Default.Compare(a, b) <= 0 ? (a, b) : (b, a);

            var seamKeys = smartAngleDegrees is not null ? new HashSet<((int, int, int), (int, int, int))>() : mesh.SeamEdges
                .Where(e => (uint)e.A < (uint)key.Length && (uint)e.B < (uint)key.Length)
                .Select(e => EdgeKey(key[e.A], key[e.B])).ToHashSet();

            // ---- islands: faces connected through non-seam edges ----
            var edgeFaces = new Dictionary<((int, int, int), (int, int, int)), List<int>>();
            for (var fi = 0; fi < faces.Count; fi++)
            {
                var c = faces[fi];
                for (var i = 0; i < c.Length; i++)
                {
                    var ek = EdgeKey(key[c[i]], key[c[(i + 1) % c.Length]]);
                    (edgeFaces.TryGetValue(ek, out var l) ? l : edgeFaces[ek] = new List<int>()).Add(fi);
                }
            }

            var faceNormal = faces.Select(c => Newell(c.Select(i => mesh.Vertices[i].Position).ToList())).ToArray();

            var islandOf = Enumerable.Repeat(-1, faces.Count).ToArray();
            var islandCount = 0;

            // Flood fill over `subset` (faces with islandOf == -1). With an angle limit, a
            // face joins only if its normal is close to the island's seed face normal.
            void Flood(IEnumerable<int> subset, float? angleDegrees)
            {
                var cosLimit = angleDegrees is { } deg ? MathF.Cos(deg * MathF.PI / 180f) : -2f;
                foreach (var start in subset)
                {
                    if (islandOf[start] >= 0) continue;
                    var seed = faceNormal[start];
                    var queue = new Queue<int>();
                    queue.Enqueue(start);
                    islandOf[start] = islandCount;
                    while (queue.Count > 0)
                    {
                        var fi = queue.Dequeue();
                        var c = faces[fi];
                        for (var i = 0; i < c.Length; i++)
                        {
                            var ek = EdgeKey(key[c[i]], key[c[(i + 1) % c.Length]]);
                            if (seamKeys.Contains(ek)) continue;
                            foreach (var other in edgeFaces[ek])
                            {
                                if (islandOf[other] >= 0) continue;
                                if (Vector3.Dot(faceNormal[other], seed) < cosLimit) continue;
                                islandOf[other] = islandCount;
                                queue.Enqueue(other);
                            }
                        }
                    }
                    islandCount++;
                }
            }

            Flood(Enumerable.Range(0, faces.Count), smartAngleDegrees);

            // Closed islands can't be flattened: re-split them by normal direction.
            if (smartAngleDegrees is null)
            {
                var closed = new List<int>();
                for (var island = 0; island < islandCount; island++)
                {
                    var members = Enumerable.Range(0, faces.Count).Where(fi => islandOf[fi] == island).ToList();
                    var hasBoundary = members.Any(fi =>
                    {
                        var c = faces[fi];
                        for (var i = 0; i < c.Length; i++)
                        {
                            var ek = EdgeKey(key[c[i]], key[c[(i + 1) % c.Length]]);
                            if (seamKeys.Contains(ek) || edgeFaces[ek].Count(o => islandOf[o] == island) < 2) return true;
                        }
                        return false;
                    });
                    if (!hasBoundary) closed.AddRange(members);
                }
                if (closed.Count > 0)
                {
                    foreach (var fi in closed) islandOf[fi] = -1;
                    Flood(closed, 66f);
                    // Renumber to a dense 0..n-1 range.
                    var dense = islandOf.Distinct().OrderBy(i => i).Select((old, i) => (old, i)).ToDictionary(t => t.old, t => t.i);
                    for (var fi = 0; fi < faces.Count; fi++) islandOf[fi] = dense[islandOf[fi]];
                    islandCount = dense.Count;
                }
            }

            // ---- give every island its own vertex copies ----
            var original = mesh.Vertices.ToList();
            var copyOf = new Dictionary<(int Vertex, int Island), int>();
            var sourceOfNew = new List<int>();
            for (var i = 0; i < original.Count; i++) sourceOfNew.Add(i);
            var firstIsland = Enumerable.Repeat(-1, original.Count).ToArray();
            var remapped = new List<int[]>(faces.Count);
            for (var fi = 0; fi < faces.Count; fi++)
            {
                var island = islandOf[fi];
                var c = faces[fi];
                var r = new int[c.Length];
                for (var i = 0; i < c.Length; i++)
                {
                    var v = c[i];
                    if (firstIsland[v] < 0 || firstIsland[v] == island) { firstIsland[v] = island; r[i] = v; continue; }
                    if (!copyOf.TryGetValue((v, island), out var copy))
                    {
                        copy = mesh.AddVertex(original[v]);
                        sourceOfNew.Add(v);
                        copyOf[(v, island)] = copy;
                    }
                    r[i] = copy;
                }
                remapped.Add(r);
            }

            // Seams stay on the new indices (both sides of each cut).
            var newSeams = new HashSet<(int, int)>();
            foreach (var r in remapped)
                for (var i = 0; i < r.Length; i++)
                {
                    var a = r[i];
                    var b = r[(i + 1) % r.Length];
                    if (seamKeys.Contains(EdgeKey(key[sourceOfNew[a]], key[sourceOfNew[b]]))) newSeams.Add(a < b ? (a, b) : (b, a));
                }

            mesh.SetTopology(
                Enumerable.Range(0, faces.Count).Where(fi => faceIsTriangle[fi]).Select(fi => new Face(remapped[fi][0], remapped[fi][1], remapped[fi][2])),
                Enumerable.Range(0, faces.Count).Where(fi => !faceIsTriangle[fi]).Select(fi => new Polygon(remapped[fi]) { MaterialSlotIndex = faceSlot[fi] }));
            foreach (var s in mesh.SeamEdges.ToList()) mesh.ClearSeam(s.A, s.B);
            foreach (var (a, b) in newSeams) mesh.MarkSeam(a, b);
            RemapShapeKeys(mesh, sourceOfNew);

            // ---- flatten each island ----
            var islandFaces = new List<int>[islandCount];
            for (var i = 0; i < islandCount; i++) islandFaces[i] = new List<int>();
            for (var fi = 0; fi < faces.Count; fi++) islandFaces[islandOf[fi]].Add(fi);

            for (var island = 0; island < islandCount; island++)
                FlattenIsland(mesh, islandFaces[island].Select(fi => remapped[fi]).ToList(), key, sourceOfNew);

            var groups = Enumerable.Range(0, islandCount)
                .Select(i => islandFaces[i].SelectMany(fi => remapped[fi]).Distinct().ToList())
                .Where(g => g.Count > 0).ToList();
            UvOperators.PackGroups(mesh, groups, margin);
            return islandCount;
        }

        /// <summary>Rotates the points so their axis-aligned bounding box is smallest
        /// (one of the convex hull's edges is then axis-aligned), wider than tall.</summary>
        private static void RotateToMinimalBounds(Vector2[] points)
        {
            if (points.Length < 3) return;
            var hull = ConvexHull(points);
            if (hull.Count < 3) return;

            var bestAngle = 0f;
            var bestArea = float.MaxValue;
            for (var i = 0; i < hull.Count; i++)
            {
                var edge = hull[(i + 1) % hull.Count] - hull[i];
                if (edge.LengthSquared() < 1e-20f) continue;
                var angle = -MathF.Atan2(edge.Y, edge.X);
                var (cos, sin) = (MathF.Cos(angle), MathF.Sin(angle));
                float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
                foreach (var p in hull)
                {
                    var x = p.X * cos - p.Y * sin;
                    var y = p.X * sin + p.Y * cos;
                    minX = MathF.Min(minX, x); maxX = MathF.Max(maxX, x);
                    minY = MathF.Min(minY, y); maxY = MathF.Max(maxY, y);
                }
                var area = (maxX - minX) * (maxY - minY);
                if (area < bestArea - 1e-9f) { bestArea = area; bestAngle = (maxY - minY) > (maxX - minX) ? angle + MathF.PI / 2 : angle; }
            }

            var (c, s2) = (MathF.Cos(bestAngle), MathF.Sin(bestAngle));
            for (var i = 0; i < points.Length; i++)
                points[i] = new Vector2(points[i].X * c - points[i].Y * s2, points[i].X * s2 + points[i].Y * c);
        }

        private static List<Vector2> ConvexHull(Vector2[] points)
        {
            var sorted = points.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
            if (sorted.Count < 3) return sorted;
            static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
            var hull = new List<Vector2>();
            foreach (var pass in new[] { sorted, Enumerable.Reverse(sorted).ToList() })
            {
                var start = hull.Count;
                foreach (var p in pass)
                {
                    while (hull.Count >= start + 2 && Cross(hull[^2], hull[^1], p) <= 0) hull.RemoveAt(hull.Count - 1);
                    hull.Add(p);
                }
                hull.RemoveAt(hull.Count - 1);
            }
            return hull;
        }

        private static Vector3 Newell(IReadOnlyList<Vector3> pts)
        {
            var nrm = Vector3.Zero;
            for (var i = 0; i < pts.Count; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Count];
                nrm += new Vector3((a.Y - b.Y) * (a.Z + b.Z), (a.Z - b.Z) * (a.X + b.X), (a.X - b.X) * (a.Y + b.Y));
            }
            return nrm.LengthSquared() > 1e-20f ? Vector3.Normalize(nrm) : Vector3.UnitY;
        }

        private static void RemapShapeKeys(Mesh mesh, List<int> sourceOfNew)
        {
            var originalCount = sourceOfNew.Where((s, i) => s == i).Count(); // copies always point at a lower index
            if (mesh.ShapeBasis is { } basis && basis.Length == originalCount && sourceOfNew.Count > originalCount)
            {
                mesh.ShapeBasis = sourceOfNew.Select(s => basis[s]).ToArray();
                for (var k = 0; k < mesh.ShapeKeys.Count; k++)
                {
                    var old = mesh.ShapeKeys[k];
                    if (old.Positions.Length != basis.Length) continue;
                    mesh.ShapeKeys[k] = new ShapeKey(old.Name, sourceOfNew.Select(s => old.Positions[s]).ToArray()) { Value = old.Value };
                }
            }
        }

        /// <summary>LSCM for one island, then scaled so UV area equals 3D area (uniform
        /// texel density across islands; packing normalises the whole layout later).</summary>
        private static void FlattenIsland(Mesh mesh, List<int[]> islandFaces, (int, int, int)[] key, List<int> sourceOfNew)
        {
            // Unknowns: one per distinct position in the island.
            var cornerId = new Dictionary<(int, int, int), int>();
            var cornerPos = new List<Vector3>();
            var vertexCorner = new Dictionary<int, int>();
            foreach (var f in islandFaces)
                foreach (var v in f)
                {
                    if (vertexCorner.ContainsKey(v)) continue;
                    var k = key[sourceOfNew[v]];
                    if (!cornerId.TryGetValue(k, out var id)) { id = cornerPos.Count; cornerId[k] = id; cornerPos.Add(mesh.Vertices[v].Position); }
                    vertexCorner[v] = id;
                }
            var n = cornerPos.Count;
            if (n < 3) return;

            // Triangles (fan) in corner ids.
            var tris = new List<(int A, int B, int C)>();
            foreach (var f in islandFaces)
                for (var i = 1; i + 1 < f.Length; i++)
                {
                    var t = (vertexCorner[f[0]], vertexCorner[f[i]], vertexCorner[f[i + 1]]);
                    if (t.Item1 != t.Item2 && t.Item2 != t.Item3 && t.Item1 != t.Item3) tris.Add(t);
                }
            if (tris.Count == 0) return;

            // Pins: the two corners farthest apart along the island's longest axis.
            var min = cornerPos.Aggregate(Vector3.Min);
            var max = cornerPos.Aggregate(Vector3.Max);
            var extent = max - min;
            var axis = extent.X >= extent.Y && extent.X >= extent.Z ? Vector3.UnitX : extent.Y >= extent.Z ? Vector3.UnitY : Vector3.UnitZ;
            var pinA = 0;
            var pinB = 0;
            for (var i = 1; i < n; i++)
            {
                if (Vector3.Dot(cornerPos[i], axis) < Vector3.Dot(cornerPos[pinA], axis)) pinA = i;
                if (Vector3.Dot(cornerPos[i], axis) > Vector3.Dot(cornerPos[pinB], axis)) pinB = i;
            }
            if (pinA == pinB) pinB = (pinA + 1) % n;
            var pinDistance = Math.Max(1e-6, Vector3.Distance(cornerPos[pinA], cornerPos[pinB]));

            // Variable layout: u_i at 2i, v_i at 2i+1; pinned ones are constants.
            var fixedValue = new Dictionary<int, double> { [2 * pinA] = 0, [2 * pinA + 1] = 0, [2 * pinB] = pinDistance, [2 * pinB + 1] = 0 };
            var column = new int[2 * n];
            var free = 0;
            for (var i = 0; i < 2 * n; i++) column[i] = fixedValue.ContainsKey(i) ? -1 : free++;

            // Rows: per triangle, real and imaginary parts of sum_j W_j (u_j + i v_j) = 0.
            var rows = new List<(int[] Cols, double[] Vals, double Rhs)>();
            foreach (var (a, b, c) in tris)
            {
                var p0 = cornerPos[a];
                var e1 = cornerPos[b] - p0;
                var e2 = cornerPos[c] - p0;
                var normal = Vector3.Cross(e1, e2);
                var doubleArea = normal.Length();
                if (doubleArea < 1e-12f || e1.LengthSquared() < 1e-14f) continue;
                var ex = Vector3.Normalize(e1);
                var ey = Vector3.Normalize(Vector3.Cross(normal, ex));
                var x = new double[] { 0, e1.Length(), Vector3.Dot(e2, ex) };
                var y = new double[] { 0, 0, Vector3.Dot(e2, ey) };
                var ids = new[] { a, b, c };
                var scale = 1.0 / Math.Sqrt(doubleArea);

                var realCols = new List<int>(); var realVals = new List<double>(); var realRhs = 0.0;
                var imagCols = new List<int>(); var imagVals = new List<double>(); var imagRhs = 0.0;
                for (var j = 0; j < 3; j++)
                {
                    var k = (j + 1) % 3;
                    var l = (j + 2) % 3;
                    var wr = (x[l] - x[k]) * scale;
                    var wi = (y[l] - y[k]) * scale;
                    var uVar = 2 * ids[j];
                    var vVar = uVar + 1;
                    // real: wr*u - wi*v ; imag: wi*u + wr*v
                    Accumulate(uVar, wr, realCols, realVals, ref realRhs);
                    Accumulate(vVar, -wi, realCols, realVals, ref realRhs);
                    Accumulate(uVar, wi, imagCols, imagVals, ref imagRhs);
                    Accumulate(vVar, wr, imagCols, imagVals, ref imagRhs);
                }
                rows.Add((realCols.ToArray(), realVals.ToArray(), realRhs));
                rows.Add((imagCols.ToArray(), imagVals.ToArray(), imagRhs));
            }
            if (rows.Count == 0) return;

            void Accumulate(int variable, double coefficient, List<int> cols, List<double> vals, ref double rhs)
            {
                if (column[variable] < 0) { rhs -= coefficient * fixedValue[variable]; return; }
                cols.Add(column[variable]);
                vals.Add(coefficient);
            }

            // Initial guess: projection onto the island's average plane (helps CG converge).
            var guess = new double[free];
            var avgNormal = Vector3.Zero;
            foreach (var (a, b, c) in tris) avgNormal += Vector3.Cross(cornerPos[b] - cornerPos[a], cornerPos[c] - cornerPos[a]);
            if (avgNormal.LengthSquared() > 1e-20f)
            {
                avgNormal = Vector3.Normalize(avgNormal);
                var pinDir = cornerPos[pinB] - cornerPos[pinA];
                pinDir -= avgNormal * Vector3.Dot(pinDir, avgNormal);
                if (pinDir.LengthSquared() > 1e-12f)
                {
                    pinDir = Vector3.Normalize(pinDir);
                    var side = Vector3.Cross(avgNormal, pinDir);
                    for (var i = 0; i < n; i++)
                    {
                        var d = cornerPos[i] - cornerPos[pinA];
                        if (column[2 * i] >= 0) guess[column[2 * i]] = Vector3.Dot(d, pinDir);
                        if (column[2 * i + 1] >= 0) guess[column[2 * i + 1]] = Vector3.Dot(d, side);
                    }
                }
            }

            var solution = SolveLeastSquares(rows, free, guess);

            var uv = new Vector2[n];
            for (var i = 0; i < n; i++)
            {
                double U(int variable) => column[variable] < 0 ? fixedValue[variable] : solution[column[variable]];
                uv[i] = new Vector2((float)U(2 * i), (float)U(2 * i + 1));
            }

            // Mirror if the map came out flipped (keep front faces counter-clockwise).
            var signedArea = 0.0;
            var area3D = 0.0;
            foreach (var (a, b, c) in tris)
            {
                var d1 = uv[b] - uv[a];
                var d2 = uv[c] - uv[a];
                signedArea += 0.5 * (d1.X * d2.Y - d1.Y * d2.X);
                area3D += 0.5 * Vector3.Cross(cornerPos[b] - cornerPos[a], cornerPos[c] - cornerPos[a]).Length();
            }
            // D3D convention: V grows down, so a front face is clockwise in (u, v).
            if (signedArea > 0) for (var i = 0; i < n; i++) uv[i].Y = -uv[i].Y;
            var uvScale = Math.Abs(signedArea) > 1e-20 ? (float)Math.Sqrt(area3D / Math.Abs(signedArea)) : 1f;
            for (var i = 0; i < n; i++) uv[i] *= uvScale;
            RotateToMinimalBounds(uv);

            foreach (var (vertex, corner) in vertexCorner) mesh.SetVertexUV(vertex, uv[corner]);
        }

        /// <summary>min |A x - b|² by conjugate gradient on the normal equations.</summary>
        private static double[] SolveLeastSquares(List<(int[] Cols, double[] Vals, double Rhs)> rows, int size, double[] x)
        {
            if (size == 0) return x;
            var atb = new double[size];
            foreach (var (cols, vals, rhs) in rows)
                for (var i = 0; i < cols.Length; i++) atb[cols[i]] += vals[i] * rhs;

            double[] Multiply(double[] v)
            {
                var result = new double[size];
                foreach (var (cols, vals, _) in rows)
                {
                    var dot = 0.0;
                    for (var i = 0; i < cols.Length; i++) dot += vals[i] * v[cols[i]];
                    for (var i = 0; i < cols.Length; i++) result[cols[i]] += vals[i] * dot;
                }
                return result;
            }

            var ax = Multiply(x);
            var r = new double[size];
            for (var i = 0; i < size; i++) r[i] = atb[i] - ax[i];
            var p = (double[])r.Clone();
            var rr = Dot(r, r);
            var tolerance = Math.Max(1e-20, 1e-14 * Math.Max(1.0, Dot(atb, atb)));
            var maxIterations = Math.Max(200, size * 4);
            for (var iteration = 0; iteration < maxIterations && rr > tolerance; iteration++)
            {
                var ap = Multiply(p);
                var pap = Dot(p, ap);
                if (Math.Abs(pap) < 1e-300) break;
                var alpha = rr / pap;
                for (var i = 0; i < size; i++) { x[i] += alpha * p[i]; r[i] -= alpha * ap[i]; }
                var rrNew = Dot(r, r);
                var beta = rrNew / rr;
                for (var i = 0; i < size; i++) p[i] = r[i] + beta * p[i];
                rr = rrNew;
            }
            return x;

            static double Dot(double[] a, double[] b)
            {
                var s = 0.0;
                for (var i = 0; i < a.Length; i++) s += a[i] * b[i];
                return s;
            }
        }
    }
}
