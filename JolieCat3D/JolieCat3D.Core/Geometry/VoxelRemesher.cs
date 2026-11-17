using System.Numerics;

namespace JolieCat3D.Core.Geometry
{
    /// <summary>
    /// Voxel Remesh (Blender's Ctrl+R in Sculpt mode): rebuilds a closed mesh as an even,
    /// all-quad surface at a chosen voxel size. The mesh's signed distance field is sampled
    /// on a grid (closest point via BVH; inside/outside by winding number) and the surface is
    /// extracted with Naive Surface Nets (one vertex per cell crossing the surface, one
    /// quad per grid edge crossing it). Self-intersections and uneven triangles disappear;
    /// UVs, weights and shape keys do not survive (like Blender).
    /// </summary>
    public static class VoxelRemesher
    {
        private static readonly Vector3 ParityDirection = Vector3.Normalize(new Vector3(0.97f, 0.173f, 0.131f));

        /// <param name="voxelSize">Grid spacing in mesh units (smaller = more detail).</param>
        /// <param name="maxCells">Safety cap on cells per axis.</param>
        public static Mesh? Remesh(Mesh mesh, float voxelSize, int maxCells = 256)
        {
            var tris = mesh.GetRenderFaces().Select(f => (mesh.Vertices[f.A].Position, mesh.Vertices[f.B].Position, mesh.Vertices[f.C].Position)).ToList();
            if (tris.Count == 0) return null;
            var bvh = new TriangleBvh(tris);

            var min = mesh.Vertices.Select(v => v.Position).Aggregate(Vector3.Min);
            var max = mesh.Vertices.Select(v => v.Position).Aggregate(Vector3.Max);
            var extent = max - min;
            var longest = MathF.Max(extent.X, MathF.Max(extent.Y, extent.Z));
            if (longest <= 0) return null;
            voxelSize = MathF.Max(voxelSize, longest / maxCells);
            // Odd fractional offset: grid corners never land exactly on axis-aligned faces.
            min -= new Vector3(voxelSize * 1.5f) + new Vector3(0.1237f, 0.2311f, 0.0719f) * voxelSize;
            var nx = (int)MathF.Ceiling((extent.X + voxelSize * 3) / voxelSize) + 1;
            var ny = (int)MathF.Ceiling((extent.Y + voxelSize * 3) / voxelSize) + 1;
            var nz = (int)MathF.Ceiling((extent.Z + voxelSize * 3) / voxelSize) + 1;

            Vector3 Corner(int i, int j, int k) => min + new Vector3(i, j, k) * voxelSize;
            int Index(int i, int j, int k) => (k * ny + j) * nx + i;

            // Signed distance at every grid corner (negative inside).
            var sdf = new float[nx * ny * nz];
            Parallel.For(0, nz, k =>
            {
                for (var j = 0; j < ny; j++)
                for (var i = 0; i < nx; i++)
                {
                    var p = Corner(i, j, k);
                    var distance = bvh.ClosestPoint(p) is { } c ? Vector3.Distance(p, c.Point) : float.MaxValue;
                    sdf[Index(i, j, k)] = Inside(bvh, p) ? -distance : distance;
                }
            });

            // One vertex per cell that the surface crosses: the mean of its edge crossings.
            var cellVertex = new int[(nx - 1) * (ny - 1) * (nz - 1)];
            Array.Fill(cellVertex, -1);
            int CellIndex(int i, int j, int k) => (k * (ny - 1) + j) * (nx - 1) + i;
            var output = new Mesh("Remeshed");
            var cornerOffsets = new (int, int, int)[] { (0, 0, 0), (1, 0, 0), (0, 1, 0), (1, 1, 0), (0, 0, 1), (1, 0, 1), (0, 1, 1), (1, 1, 1) };
            var cubeEdges = new (int A, int B)[] { (0, 1), (2, 3), (4, 5), (6, 7), (0, 2), (1, 3), (4, 6), (5, 7), (0, 4), (1, 5), (2, 6), (3, 7) };
            for (var k = 0; k < nz - 1; k++)
            for (var j = 0; j < ny - 1; j++)
            for (var i = 0; i < nx - 1; i++)
            {
                var values = new float[8];
                var positions = new Vector3[8];
                var inside = 0;
                for (var c = 0; c < 8; c++)
                {
                    var (di, dj, dk) = cornerOffsets[c];
                    values[c] = sdf[Index(i + di, j + dj, k + dk)];
                    positions[c] = Corner(i + di, j + dj, k + dk);
                    if (values[c] < 0) inside++;
                }
                if (inside == 0 || inside == 8) continue;
                var sum = Vector3.Zero;
                var count = 0;
                foreach (var (a, b) in cubeEdges)
                {
                    if ((values[a] < 0) == (values[b] < 0)) continue;
                    var t = values[a] / (values[a] - values[b]);
                    sum += Vector3.Lerp(positions[a], positions[b], t);
                    count++;
                }
                cellVertex[CellIndex(i, j, k)] = output.AddVertex(new Vertex(sum / count, Vector3.UnitY, Vector2.Zero));
            }

            // One quad per grid edge with a sign change, joining the 4 cells around it.
            void Quad(int c0, int c1, int c2, int c3, bool flip)
            {
                if (c0 < 0 || c1 < 0 || c2 < 0 || c3 < 0) return;
                output.AddPolygon(new Polygon(flip ? new[] { c0, c3, c2, c1 } : new[] { c0, c1, c2, c3 }));
            }
            for (var k = 1; k < nz - 1; k++)
            for (var j = 1; j < ny - 1; j++)
            for (var i = 0; i < nx - 1; i++)
            {
                // Edge along X from (i,j,k) to (i+1,j,k): cells (i, j-1..j, k-1..k).
                var a = sdf[Index(i, j, k)] < 0;
                if (a == sdf[Index(i + 1, j, k)] < 0) continue;
                Quad(cellVertex[CellIndex(i, j - 1, k - 1)], cellVertex[CellIndex(i, j, k - 1)], cellVertex[CellIndex(i, j, k)], cellVertex[CellIndex(i, j - 1, k)], !a);
            }
            for (var k = 1; k < nz - 1; k++)
            for (var j = 0; j < ny - 1; j++)
            for (var i = 1; i < nx - 1; i++)
            {
                var a = sdf[Index(i, j, k)] < 0;
                if (a == sdf[Index(i, j + 1, k)] < 0) continue;
                Quad(cellVertex[CellIndex(i - 1, j, k - 1)], cellVertex[CellIndex(i - 1, j, k)], cellVertex[CellIndex(i, j, k)], cellVertex[CellIndex(i, j, k - 1)], !a);
            }
            for (var k = 0; k < nz - 1; k++)
            for (var j = 1; j < ny - 1; j++)
            for (var i = 1; i < nx - 1; i++)
            {
                var a = sdf[Index(i, j, k)] < 0;
                if (a == sdf[Index(i, j, k + 1)] < 0) continue;
                Quad(cellVertex[CellIndex(i - 1, j - 1, k)], cellVertex[CellIndex(i, j - 1, k)], cellVertex[CellIndex(i, j, k)], cellVertex[CellIndex(i - 1, j, k)], !a);
            }

            if (output.Polygons.Count == 0) return null;
            output.RemoveUnusedVertices();
            output.RecalculateNormals();
            // Make sure faces point outward (the quad winding convention above is per axis).
            var outward = 0;
            foreach (var p in output.Polygons)
            {
                var v0 = output.Vertices[p.Indices[0]].Position;
                var n = Vector3.Cross(output.Vertices[p.Indices[1]].Position - v0, output.Vertices[p.Indices[2]].Position - v0);
                var centroid = p.Indices.Aggregate(Vector3.Zero, (s, idx) => s + output.Vertices[idx].Position) / p.Indices.Count;
                outward += Inside(bvh, centroid + Vector3.Normalize(n + new Vector3(1e-12f)) * voxelSize * 0.5f) ? -1 : 1;
                if (Math.Abs(outward) > 64) break;
            }
            if (outward < 0) output.FlipWinding();
            output.Material = mesh.Material;
            return output;
        }

        /// <summary>Winding number along one ray (exits minus entries): non-zero means inside.
        /// Unlike plain parity this treats overlapping parts (a union) as solid.</summary>
        private static bool Inside(TriangleBvh bvh, Vector3 p)
        {
            var winding = 0;
            var origin = p;
            for (var guard = 0; guard < 256; guard++)
            {
                if (bvh.Intersect(origin, ParityDirection, 1e-6f) is not { } hit) break;
                winding += Vector3.Dot(bvh.TriangleNormal(hit.Triangle), ParityDirection) > 0 ? 1 : -1;
                origin += ParityDirection * (hit.T + 1e-5f);
            }
            return winding != 0;
        }
    }
}
