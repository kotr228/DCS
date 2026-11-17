using System.Numerics;

namespace JolieCat3D.Core.Geometry
{
    /// <summary>Operators that sweep the selected faces: Spin, Screw and Extrude Along Normals.</summary>
    public static class MeshSweepOperators
    {
        /// <summary>
        /// Spin: extrudes the selected faces <paramref name="steps"/> times, rotating each
        /// copy by <paramref name="angleDegrees"/>/<paramref name="steps"/> around the axis
        /// through <paramref name="center"/> (mesh-local space). With
        /// <paramref name="advancePerStep"/> each step also moves along the axis (Screw).
        /// Returns the last cap's vertices.
        /// </summary>
        public static IReadOnlySet<int>? Spin(Mesh mesh, IReadOnlySet<int> selection, Vector3 center, Vector3 axis, float angleDegrees, int steps, float advancePerStep = 0f)
        {
            if (axis.LengthSquared() < 1e-12f || steps < 1) return null;
            axis = Vector3.Normalize(axis);
            var stepRotation = Quaternion.CreateFromAxisAngle(axis, angleDegrees * MathF.PI / 180f / steps);
            var fullTurn = MathF.Abs(angleDegrees) >= 359.99f && MathF.Abs(advancePerStep) < 1e-7f;

            // Extruding removes the original faces; a partial spin keeps them as a start cap
            // (reversed, facing away from the sweep) so the result is a closed solid.
            var startFaces = mesh.Polygons.Where(f => f.Indices.All(selection.Contains)).Select(f => (f.Indices.ToArray(), f.MaterialSlotIndex))
                .Concat(mesh.Faces.Where(f => selection.Contains(f.A) && selection.Contains(f.B) && selection.Contains(f.C)).Select(f => (new[] { f.A, f.B, f.C }, -1)))
                .ToList();
            if (startFaces.Count == 0) return null;
            var startCaps = startFaces.Select(f => (Positions: f.Item1.Select(i => mesh.Vertices[i]).ToArray(), Slot: f.Item2)).ToList();

            IReadOnlySet<int>? current = selection;
            for (var i = 0; i < steps; i++)
            {
                current = MeshOperators.ExtrudeRegion(mesh, current!, 0f);
                if (current is null) return null;
                foreach (var v in current)
                {
                    var p = mesh.Vertices[v].Position;
                    var rotated = center + Vector3.Transform(p - center, stepRotation) + axis * advancePerStep;
                    mesh.SetVertexPosition(v, rotated);
                }
            }
            if (fullTurn)
            {
                // The last cap lands exactly on the start: drop it, the walls already close the ring.
                var cap = current!;
                mesh.SetTopology(
                    mesh.Faces.Where(f => !(cap.Contains(f.A) && cap.Contains(f.B) && cap.Contains(f.C))),
                    mesh.Polygons.Where(p => !p.Indices.All(cap.Contains)));
            }
            else
            {
                foreach (var (vertices, slot) in startCaps)
                {
                    var indices = vertices.Reverse().Select(v => mesh.AddVertex(v)).ToArray();
                    mesh.AddPolygon(new Polygon(indices) { MaterialSlotIndex = slot });
                }
            }

            mesh.RecalculateNormals();
            return current;
        }

        /// <summary>Alt+E: extrudes the selected faces and pushes the new cap out along each
        /// corner's own normal (keeps curved surfaces evenly thick, unlike a plain extrude).</summary>
        public static IReadOnlySet<int>? ExtrudeAlongNormals(Mesh mesh, IReadOnlySet<int> selection, float distance)
        {
            // Corner normals from the selected faces before extruding.
            var cornerNormals = new Dictionary<(int, int, int), Vector3>();
            IEnumerable<IReadOnlyList<int>> faces = mesh.Polygons.Select(p => p.Indices)
                .Concat(mesh.Faces.Select(f => (IReadOnlyList<int>)new[] { f.A, f.B, f.C }));
            foreach (var f in faces)
            {
                if (!f.All(selection.Contains)) continue;
                var n = Vector3.Zero;
                for (var i = 0; i < f.Count; i++)
                {
                    var a = mesh.Vertices[f[i]].Position;
                    var b = mesh.Vertices[f[(i + 1) % f.Count]].Position;
                    n += new Vector3((a.Y - b.Y) * (a.Z + b.Z), (a.Z - b.Z) * (a.X + b.X), (a.X - b.X) * (a.Y + b.Y));
                }
                if (n.LengthSquared() < 1e-20f) continue;
                n = Vector3.Normalize(n);
                foreach (var i in f)
                {
                    var k = MeshOperators.Key(mesh.Vertices[i].Position);
                    cornerNormals[k] = cornerNormals.GetValueOrDefault(k) + n;
                }
            }
            if (cornerNormals.Count == 0) return null;

            var cap = MeshOperators.ExtrudeRegion(mesh, selection, 0f);
            if (cap is null) return null;
            foreach (var v in cap)
            {
                var p = mesh.Vertices[v].Position;
                if (!cornerNormals.TryGetValue(MeshOperators.Key(p), out var n) || n.LengthSquared() < 1e-12f) continue;
                mesh.SetVertexPosition(v, p + Vector3.Normalize(n) * distance);
            }
            mesh.RecalculateNormals();
            return cap;
        }
    }
}
