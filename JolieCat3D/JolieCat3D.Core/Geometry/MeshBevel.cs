using System.Numerics;

namespace JolieCat3D.Core.Geometry
{
    /// <summary>
    /// Ctrl+B edge bevel, Blender-style: one segment is a chamfer, more segments round the
    /// strip along a circular profile (exactly a quarter circle on a right-angle edge).
    /// With several segments, the corner patches take the profile points too (a flat n-gon
    /// at multi-edge corners rather than Blender's rounded grid).
    ///
    /// Every selected edge shared by two faces is replaced by a strip face of the given
    /// width. Each face touching a bevelled corner is rebuilt with that corner replaced by:
    /// <list type="bullet">
    /// <item>an inner point, when both of the face's edges at the corner are bevelled;</item>
    /// <item>a point slid along its non-bevelled edge, when one is;</item>
    /// <item>the slid points of its two edges (and the corner itself if one edge has no
    /// slide), when neither is.</item>
    /// </list>
    /// Any hole left around a corner (3+ distinct new points, e.g. a cube corner with all
    /// three edges bevelled) is closed with a patch polygon. Like the other operators,
    /// vertices sharing a position are one corner.
    /// </summary>
    public static class MeshBevel
    {
        private sealed class FaceInfo
        {
            public int[] Corners = Array.Empty<int>();
            public int[] Vertices = Array.Empty<int>();
            public int Slot;
            public bool IsTriangle;
        }

        private readonly record struct NewPoint(Vector3 Position, Vector2 Uv);

        /// <summary>Bevels every edge whose two faces meet at more than
        /// <paramref name="angleLimitDegrees"/> (the Bevel modifier's "Angle" limit).</summary>
        public static IReadOnlySet<int>? BevelEdgesByAngle(Mesh mesh, float angleLimitDegrees, float width, int segments = 1) =>
            BevelEdges(mesh, null, width, segments, angleLimitDegrees);

        public static IReadOnlySet<int>? BevelEdges(Mesh mesh, IReadOnlySet<int>? selection, float width, int segments = 1, float? angleLimitDegrees = null)
        {
            if (width <= 0) return null;
            segments = Math.Clamp(segments, 1, 64);

            // ---- corners (by position) ----
            var cornerOf = new int[mesh.Vertices.Count];
            var ids = new Dictionary<(int, int, int), int>();
            var cornerPos = new List<Vector3>();
            for (var i = 0; i < mesh.Vertices.Count; i++)
            {
                var k = MeshOperators.Key(mesh.Vertices[i].Position);
                if (!ids.TryGetValue(k, out var id)) { id = ids.Count; ids[k] = id; cornerPos.Add(mesh.Vertices[i].Position); }
                cornerOf[i] = id;
            }

            var faces = new List<FaceInfo>();
            foreach (var p in mesh.Polygons)
                faces.Add(new FaceInfo { Vertices = p.Indices.ToArray(), Corners = p.Indices.Select(i => cornerOf[i]).ToArray(), Slot = p.MaterialSlotIndex });
            foreach (var f in mesh.Faces)
                faces.Add(new FaceInfo { Vertices = new[] { f.A, f.B, f.C }, Corners = new[] { cornerOf[f.A], cornerOf[f.B], cornerOf[f.C] }, Slot = -1, IsTriangle = true });

            static (int, int) E(int a, int b) => a < b ? (a, b) : (b, a);
            var edgeFaces = new Dictionary<(int, int), List<int>>();
            var cornerFaces = new Dictionary<int, List<int>>();
            for (var fi = 0; fi < faces.Count; fi++)
            {
                var c = faces[fi].Corners;
                for (var i = 0; i < c.Length; i++)
                {
                    var key = E(c[i], c[(i + 1) % c.Length]);
                    (edgeFaces.TryGetValue(key, out var l) ? l : edgeFaces[key] = new List<int>()).Add(fi);
                    (cornerFaces.TryGetValue(c[i], out var cf) ? cf : cornerFaces[c[i]] = new List<int>()).Add(fi);
                }
            }

            HashSet<(int, int)> bevelled;
            if (selection is null)
            {
                // By angle: compare the two faces' normals.
                var limit = MathF.Cos(Math.Clamp(angleLimitDegrees ?? 30f, 0f, 180f) * MathF.PI / 180f);
                Vector3 FaceNormal(int fi)
                {
                    var c = faces[fi].Corners;
                    var n = Vector3.Zero;
                    for (var i = 0; i < c.Length; i++)
                    {
                        var a = cornerPos[c[i]];
                        var b = cornerPos[c[(i + 1) % c.Length]];
                        n += new Vector3((a.Y - b.Y) * (a.Z + b.Z), (a.Z - b.Z) * (a.X + b.X), (a.X - b.X) * (a.Y + b.Y));
                    }
                    return n.LengthSquared() > 1e-20f ? Vector3.Normalize(n) : Vector3.Zero;
                }
                bevelled = edgeFaces.Where(kv => kv.Value.Count == 2 && Vector3.Dot(FaceNormal(kv.Value[0]), FaceNormal(kv.Value[1])) < limit)
                    .Select(kv => kv.Key).ToHashSet();
            }
            else
            {
                var selectedCorners = selection.Where(i => (uint)i < (uint)mesh.Vertices.Count).Select(i => cornerOf[i]).ToHashSet();
                bevelled = edgeFaces.Where(kv => kv.Value.Count == 2 && selectedCorners.Contains(kv.Key.Item1) && selectedCorners.Contains(kv.Key.Item2))
                    .Select(kv => kv.Key).ToHashSet();
            }
            if (bevelled.Count == 0) return null;

            var bevelCorners = bevelled.SelectMany(e => new[] { e.Item1, e.Item2 }).ToHashSet();

            // Keep the width below half of every edge leaving a bevelled corner.
            var maxWidth = float.MaxValue;
            foreach (var key in edgeFaces.Keys)
                if (bevelCorners.Contains(key.Item1) || bevelCorners.Contains(key.Item2))
                    maxWidth = MathF.Min(maxWidth, Vector3.Distance(cornerPos[key.Item1], cornerPos[key.Item2]) * 0.45f);
            width = MathF.Min(width, maxWidth);

            Vector3 Dir(int from, int to) => Vector3.Normalize(cornerPos[to] - cornerPos[from]);
            float T(int from, int to) => width / MathF.Max(1e-6f, Vector3.Distance(cornerPos[from], cornerPos[to]));

            // A slide point exists on edge (u,x) if it isn't bevelled and a face along it bevels an edge at u.
            bool HasSlide(int u, int x)
            {
                if (bevelled.Contains(E(u, x))) return false;
                if (!edgeFaces.TryGetValue(E(u, x), out var list)) return false;
                foreach (var fi in list)
                {
                    var c = faces[fi].Corners;
                    var i = Array.IndexOf(c, u);
                    if (i < 0) continue;
                    var p = c[(i + c.Length - 1) % c.Length];
                    var n = c[(i + 1) % c.Length];
                    if (bevelled.Contains(E(p, u)) || bevelled.Contains(E(u, n))) return true;
                }
                return false;
            }

            // ---- replacement points per face corner ----
            var replacements = new List<NewPoint>[faces.Count][];
            for (var fi = 0; fi < faces.Count; fi++)
            {
                var f = faces[fi];
                var c = f.Corners;
                replacements[fi] = new List<NewPoint>[c.Length];
                for (var i = 0; i < c.Length; i++)
                {
                    var u = c[i];
                    var pi = (i + c.Length - 1) % c.Length;
                    var ni = (i + 1) % c.Length;
                    var p = c[pi];
                    var n = c[ni];
                    var uvU = mesh.Vertices[f.Vertices[i]].UV;
                    var uvP = mesh.Vertices[f.Vertices[pi]].UV;
                    var uvN = mesh.Vertices[f.Vertices[ni]].UV;
                    NewPoint Slide(int x, Vector2 uvX) => new(cornerPos[u] + Dir(u, x) * width, Vector2.Lerp(uvU, uvX, T(u, x)));

                    var list = new List<NewPoint>();
                    if (!bevelCorners.Contains(u))
                    {
                        list.Add(new NewPoint(cornerPos[u], uvU));
                    }
                    else
                    {
                        var bin = bevelled.Contains(E(p, u));
                        var bout = bevelled.Contains(E(u, n));
                        if (bin && bout)
                            list.Add(new NewPoint(cornerPos[u] + Dir(u, p) * width + Dir(u, n) * width,
                                uvU + (uvP - uvU) * T(u, p) + (uvN - uvU) * T(u, n)));
                        else if (bin) list.Add(Slide(n, uvN));
                        else if (bout) list.Add(Slide(p, uvP));
                        else
                        {
                            var sp = HasSlide(u, p);
                            var sn = HasSlide(u, n);
                            if (sp) list.Add(Slide(p, uvP));
                            if (!(sp && sn)) list.Add(new NewPoint(cornerPos[u], uvU));
                            if (sn) list.Add(Slide(n, uvN));
                        }
                    }
                    replacements[fi][i] = list;
                }
            }

            // ---- rebuild ----
            var newPolygons = new List<Polygon>();
            var newTriangles = new List<Face>();
            var result = new HashSet<int>();

            int AddPoint(NewPoint p, Vector3 normal) =>
                mesh.AddVertex(new Vertex(p.Position, normal, p.Uv, Numerics.Color4.White));

            static Vector3 Normal(IReadOnlyList<Vector3> pts)
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

            for (var fi = 0; fi < faces.Count; fi++)
            {
                var f = faces[fi];
                if (!f.Corners.Any(bevelCorners.Contains))
                {
                    if (f.IsTriangle) newTriangles.Add(new Face(f.Vertices[0], f.Vertices[1], f.Vertices[2]));
                    else newPolygons.Add(new Polygon(f.Vertices) { MaterialSlotIndex = f.Slot });
                    continue;
                }
                var pts = replacements[fi].SelectMany(l => l).ToList();
                var normal = Normal(pts.Select(p => p.Position).ToList());
                newPolygons.Add(new Polygon(pts.Select(p => AddPoint(p, normal))) { MaterialSlotIndex = f.Slot });
            }

            // Profile between the two side points A (face 1) and B (face 2) at one end of a
            // bevelled edge: P(t) = C + (A-C)(1-sin(t*pi/2)) + (B-C)(1-cos(t*pi/2)), where C is
            // the point of the original edge line nearest the midpoint of A and B.
            var cornerProfile = new Dictionary<int, List<NewPoint>>();
            var cornerArcCenters = new Dictionary<int, List<Vector3>>();
            List<NewPoint> Profile(NewPoint a, NewPoint b, int edgeFrom, int edgeTo, int corner)
            {
                var list = new List<NewPoint> { a };
                if (segments > 1)
                {
                    var origin = cornerPos[edgeFrom];
                    var dir = Dir(edgeFrom, edgeTo);
                    var mid = (a.Position + b.Position) * 0.5f;
                    var c = origin + dir * Vector3.Dot(mid - origin, dir);
                    (cornerArcCenters.TryGetValue(corner, out var centers) ? centers : cornerArcCenters[corner] = new List<Vector3>()).Add(a.Position + b.Position - c);
                    for (var s = 1; s < segments; s++)
                    {
                        var t = s / (float)segments;
                        var wa = 1f - MathF.Sin(t * MathF.PI / 2);
                        var wb = 1f - MathF.Cos(t * MathF.PI / 2);
                        var point = new NewPoint(c + (a.Position - c) * wa + (b.Position - c) * wb, Vector2.Lerp(a.Uv, b.Uv, t));
                        list.Add(point);
                        (cornerProfile.TryGetValue(corner, out var cp) ? cp : cornerProfile[corner] = new List<NewPoint>()).Add(point);
                    }
                }
                list.Add(b);
                return list;
            }

            // Strip face(s) per bevelled edge.
            foreach (var (a, b) in bevelled)
            {
                var pair = edgeFaces[(a, b)];
                NewPoint First(int fi, int corner) => replacements[fi][Array.IndexOf(faces[fi].Corners, corner)][0];
                NewPoint Last(int fi, int corner) { var l = replacements[fi][Array.IndexOf(faces[fi].Corners, corner)]; return l[^1]; }

                var f1 = pair[0];
                var c1 = faces[f1].Corners;
                var ia = Array.IndexOf(c1, a);
                var forward = c1[(ia + 1) % c1.Length] == b; // f1 runs a -> b
                var (from, to) = forward ? (a, b) : (b, a);   // f1 runs from -> to
                var f2 = pair[1];
                var from1 = Last(f1, from);
                var to1 = First(f1, to);
                var to2 = Last(f2, to);   // f2 runs to -> from
                var from2 = First(f2, from);
                var fromProfile = Profile(from1, from2, from, to, from);
                var toProfile = Profile(to1, to2, to, from, to);
                for (var s = 0; s < segments; s++)
                {
                    var quad = new[] { toProfile[s], fromProfile[s], fromProfile[s + 1], toProfile[s + 1] };
                    var normal = Normal(quad.Select(q => q.Position).ToList());
                    var idx = quad.Select(q => AddPoint(q, normal)).ToArray();
                    foreach (var i in idx) result.Add(i);
                    newPolygons.Add(new Polygon(idx) { MaterialSlotIndex = faces[f1].Slot });
                }
            }

            // Corner patches.
            foreach (var u in bevelCorners)
            {
                var around = cornerFaces[u].Distinct().ToList();
                var points = new Dictionary<(int, int, int), NewPoint>();
                var avgNormal = Vector3.Zero;
                foreach (var fi in around)
                {
                    var i = Array.IndexOf(faces[fi].Corners, u);
                    foreach (var p in replacements[fi][i]) points.TryAdd(MeshOperators.Key(p.Position), p);
                    avgNormal += Normal(faces[fi].Corners.Select(c => cornerPos[c]).ToList());
                }
                if (cornerProfile.TryGetValue(u, out var profilePoints))
                    foreach (var p in profilePoints) points.TryAdd(MeshOperators.Key(p.Position), p);
                if (points.Count < 3) continue;

                // 3+ rounded edges meeting: a spherical cap (triangle fan around an apex on
                // the sphere through the arcs) instead of a flat n-gon.
                if (cornerArcCenters.TryGetValue(u, out var arcCenters) && arcCenters.Count >= 3)
                {
                    var sphereCenter = arcCenters.Aggregate(Vector3.Zero, (acc, v) => acc + v) / arcCenters.Count;
                    var ring = points.Values.ToList();
                    var mean = ring.Aggregate(Vector3.Zero, (acc, p) => acc + p.Position) / ring.Count;
                    var radius = ring.Average(p => Vector3.Distance(p.Position, sphereCenter));
                    var capAxis = mean - sphereCenter;
                    if (capAxis.LengthSquared() > 1e-12f)
                    {
                        capAxis = Vector3.Normalize(capAxis);
                        var cHelper = MathF.Abs(capAxis.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
                        var cu = Vector3.Normalize(Vector3.Cross(cHelper, capAxis));
                        var cv = Vector3.Cross(capAxis, cu);
                        ring = ring.OrderBy(p => MathF.Atan2(Vector3.Dot(p.Position - mean, cv), Vector3.Dot(p.Position - mean, cu))).ToList();
                        var apex = new NewPoint(sphereCenter + capAxis * radius, ring.Aggregate(Vector2.Zero, (acc, p) => acc + p.Uv) / ring.Count);
                        for (var k = 0; k < ring.Count; k++)
                        {
                            var tri = new List<NewPoint> { apex, ring[k], ring[(k + 1) % ring.Count] };
                            var triNormal = Normal(tri.Select(p => p.Position).ToList());
                            if (Vector3.Dot(triNormal, capAxis) < 0) { (tri[1], tri[2]) = (tri[2], tri[1]); triNormal = -triNormal; }
                            var triIdx = tri.Select(p => AddPoint(p, triNormal)).ToArray();
                            foreach (var i in triIdx) result.Add(i);
                            newPolygons.Add(new Polygon(triIdx));
                        }
                        continue;
                    }
                }

                avgNormal = avgNormal.LengthSquared() > 1e-12f ? Vector3.Normalize(avgNormal) : Vector3.UnitY;
                var center = points.Values.Aggregate(Vector3.Zero, (s, p) => s + p.Position) / points.Count;
                var helper = MathF.Abs(avgNormal.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
                var axisU = Vector3.Normalize(Vector3.Cross(helper, avgNormal));
                var axisV = Vector3.Cross(avgNormal, axisU);
                var ordered = points.Values.OrderBy(p => MathF.Atan2(Vector3.Dot(p.Position - center, axisV), Vector3.Dot(p.Position - center, axisU))).ToList();
                var patchNormal = Normal(ordered.Select(p => p.Position).ToList());
                if (Vector3.Dot(patchNormal, avgNormal) < 0) { ordered.Reverse(); patchNormal = -patchNormal; }
                var idx = ordered.Select(p => AddPoint(p, patchNormal)).ToArray();
                foreach (var i in idx) result.Add(i);
                newPolygons.Add(new Polygon(idx));
            }

            mesh.SetTopology(newTriangles, newPolygons);
            var remap = mesh.RemoveUnusedVertices();
            return remap is null ? result : result.Where(i => remap[i] >= 0).Select(i => remap[i]).ToHashSet();
        }
    }
}
