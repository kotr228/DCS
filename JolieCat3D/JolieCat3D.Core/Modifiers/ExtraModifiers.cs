using System.Numerics;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Materials.Nodes;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.Core.Modifiers
{
    /// <summary>A modifier whose settings are a flat list of named values - the Inspector
    /// shows these generically (numbers as text boxes, choices as drop-downs).</summary>
    public interface IParameterizedModifier
    {
        IReadOnlyList<ModifierParameter> Parameters { get; }
    }

    /// <summary>One editable setting. <see cref="Choices"/> non-null = a drop-down; the value
    /// is then the choice index.</summary>
    public sealed record ModifierParameter(string Label, Func<float> Get, Action<float> Set, IReadOnlyList<string>? Choices = null, bool IsInteger = false);

    /// <summary>Type names (for saving) of the parameterized modifiers.</summary>
    public static class ParameterizedModifiers
    {
        public static readonly IReadOnlyDictionary<string, Func<Modifier>> Factories = new Dictionary<string, Func<Modifier>>
        {
            ["Displace"] = () => new DisplaceModifier(),
            ["Smooth"] = () => new SmoothModifier(),
            ["Triangulate"] = () => new TriangulateModifier(),
            ["Weld"] = () => new WeldModifier(),
            ["SimpleDeform"] = () => new SimpleDeformModifier(),
            ["Cast"] = () => new CastModifier(),
            ["Remesh"] = () => new RemeshModifier(),
        };

        public static string? TypeName(Modifier modifier) => modifier switch
        {
            DisplaceModifier => "Displace",
            SmoothModifier => "Smooth",
            TriangulateModifier => "Triangulate",
            WeldModifier => "Weld",
            SimpleDeformModifier => "SimpleDeform",
            CastModifier => "Cast",
            RemeshModifier => "Remesh",
            _ => null,
        };
    }

    /// <summary>Cast (Blender): pulls the shape towards a sphere, cylinder (around the
    /// chosen axis) or cuboid around its own bounds centre. Factor 1 = fully cast, negative pushes away.</summary>
    public sealed class CastModifier : Modifier, IParameterizedModifier
    {
        public static readonly string[] Shapes = { "Sphere", "Cylinder", "Cuboid" };

        public override string Name => "Cast";
        public int Shape { get; set; }
        public float Factor { get; set; } = 0.5f;
        /// <summary>Cylinder axis (0 X, 1 Y, 2 Z).</summary>
        public int Axis { get; set; } = 1;

        public IReadOnlyList<ModifierParameter> Parameters => new ModifierParameter[]
        {
            new("Shape", () => Shape, v => Shape = Math.Clamp((int)v, 0, 2), Shapes),
            new("Factor", () => Factor, v => Factor = Math.Clamp(v, -5f, 5f)),
            new("Axis", () => Axis, v => Axis = Math.Clamp((int)v, 0, 2), SimpleDeformModifier.Axes),
        };

        public override Mesh Apply(Mesh input, Node? owner = null)
        {
            ArgumentNullException.ThrowIfNull(input);
            if (input.Vertices.Count == 0) return input.Clone();
            var (min, max) = input.GetBounds();
            var centre = (min + max) * 0.5f;
            var half = (max - min) * 0.5f;
            // Target size: the average distance from the centre (sphere/cylinder), the box itself (cuboid).
            var radius = input.Vertices.Average(v => (v.Position - centre).Length());
            var axis = Axis switch { 0 => Vector3.UnitX, 2 => Vector3.UnitZ, _ => Vector3.UnitY };
            var radial = input.Vertices.Average(v =>
            {
                var d = v.Position - centre;
                return (d - axis * Vector3.Dot(d, axis)).Length();
            });
            return ModifierMath.WithPositions(input, (_, p) =>
            {
                var d = p - centre;
                Vector3 target;
                switch (Shape)
                {
                    case 1:
                    {
                        var along = axis * Vector3.Dot(d, axis);
                        var across = d - along;
                        target = across.LengthSquared() > 1e-20f ? centre + along + Vector3.Normalize(across) * radial : p;
                        break;
                    }
                    case 2:
                    {
                        // Project onto the box surface along the direction from the centre.
                        var scale = MathF.Max(MathF.Max(MathF.Abs(d.X) / MathF.Max(half.X, 1e-6f), MathF.Abs(d.Y) / MathF.Max(half.Y, 1e-6f)), MathF.Abs(d.Z) / MathF.Max(half.Z, 1e-6f));
                        target = scale > 1e-6f ? centre + d / scale : p;
                        break;
                    }
                    default:
                        target = d.LengthSquared() > 1e-20f ? centre + Vector3.Normalize(d) * radius : p;
                        break;
                }
                return p + (target - p) * Factor;
            });
        }

        public override Modifier Clone() => new CastModifier { IsEnabled = IsEnabled, Shape = Shape, Factor = Factor, Axis = Axis };
    }

    /// <summary>Remesh (Blender, Voxel mode) as a non-destructive modifier: rebuilds the
    /// surface as an even, watertight mesh with cells of <see cref="VoxelSize"/>.</summary>
    public sealed class RemeshModifier : Modifier, IParameterizedModifier
    {
        public override string Name => "Remesh";
        public float VoxelSize { get; set; } = 0.1f;

        public IReadOnlyList<ModifierParameter> Parameters => new ModifierParameter[]
        {
            new("Voxel Size", () => VoxelSize, v => VoxelSize = MathF.Max(0.001f, v)),
        };

        public override Mesh Apply(Mesh input, Node? owner = null)
        {
            ArgumentNullException.ThrowIfNull(input);
            var result = VoxelRemesher.Remesh(input, VoxelSize, maxCells: 160);
            if (result is null) return input.Clone();
            result.Material = input.Material;
            return result;
        }

        public override Modifier Clone() => new RemeshModifier { IsEnabled = IsEnabled, VoxelSize = VoxelSize };
    }

    internal static class ModifierMath
    {
        /// <summary>Smooth normals averaged over every vertex entry sharing a position, so
        /// split vertices (UV seams, hard edges) move together and the surface doesn't tear.</summary>
        public static Dictionary<(int, int, int), Vector3> WeldedNormals(Mesh mesh)
        {
            var sums = new Dictionary<(int, int, int), Vector3>();
            foreach (var f in mesh.GetRenderFaces())
            {
                var a = mesh.Vertices[f.A].Position; var b = mesh.Vertices[f.B].Position; var c = mesh.Vertices[f.C].Position;
                var n = Vector3.Cross(b - a, c - a); // area weighted
                foreach (var p in new[] { a, b, c })
                {
                    var key = MeshOperators.Key(p);
                    sums[key] = sums.TryGetValue(key, out var s) ? s + n : n;
                }
            }
            foreach (var key in sums.Keys.ToList())
                sums[key] = sums[key].LengthSquared() > 1e-20f ? Vector3.Normalize(sums[key]) : Vector3.UnitY;
            return sums;
        }

        /// <summary>Moves vertices and re-derives normals, keeping hard edges hard (each split
        /// vertex keeps averaging only over its own faces).</summary>
        public static Mesh WithPositions(Mesh input, Func<int, Vector3, Vector3> move)
        {
            var output = input.Clone();
            for (var i = 0; i < output.Vertices.Count; i++) output.SetVertexPosition(i, move(i, input.Vertices[i].Position));
            output.RecalculateNormals();
            return output;
        }
    }

    /// <summary>
    /// Displace (Blender): pushes vertices along their normal (or an axis) by a fractal
    /// noise texture - rocks, terrain, organic breakup. Noise is read in object space, so
    /// coincident vertices move identically and the surface stays closed.
    /// </summary>
    public sealed class DisplaceModifier : Modifier, IParameterizedModifier
    {
        public static readonly string[] Directions = { "Normal", "X", "Y", "Z" };

        public override string Name => "Displace";
        public float Strength { get; set; } = 0.2f;
        public float Scale { get; set; } = 2f;
        public float Detail { get; set; } = 2f;
        public float Midlevel { get; set; } = 0.5f;
        public int Direction { get; set; }
        public int Seed { get; set; }

        public IReadOnlyList<ModifierParameter> Parameters => new ModifierParameter[]
        {
            new("Strength", () => Strength, v => Strength = v),
            new("Noise Scale", () => Scale, v => Scale = MathF.Max(0.001f, v)),
            new("Detail", () => Detail, v => Detail = Math.Clamp(v, 0, 15)),
            new("Midlevel", () => Midlevel, v => Midlevel = v),
            new("Direction", () => Direction, v => Direction = Math.Clamp((int)v, 0, 3), Directions),
            new("Seed", () => Seed, v => Seed = (int)v, IsInteger: true),
        };

        public override Mesh Apply(Mesh input, Node? owner = null)
        {
            ArgumentNullException.ThrowIfNull(input);
            var normals = ModifierMath.WeldedNormals(input);
            var offset = new Vector3(Seed * 17.31f, Seed * 5.17f, Seed * 11.73f);
            return ModifierMath.WithPositions(input, (_, p) =>
            {
                var h = Noise.Fbm(p * Scale + offset, Detail, 0.5f, 0) - Midlevel;
                var dir = Direction switch
                {
                    1 => Vector3.UnitX,
                    2 => Vector3.UnitY,
                    3 => Vector3.UnitZ,
                    _ => normals.TryGetValue(MeshOperators.Key(p), out var n) ? n : Vector3.UnitY,
                };
                return p + dir * (h * Strength);
            });
        }

        public override Modifier Clone() => new DisplaceModifier { IsEnabled = IsEnabled, Strength = Strength, Scale = Scale, Detail = Detail, Midlevel = Midlevel, Direction = Direction, Seed = Seed };
    }

    /// <summary>Smooth (Blender): Laplacian relaxation over the welded edge graph.</summary>
    public sealed class SmoothModifier : Modifier, IParameterizedModifier
    {
        public override string Name => "Smooth";
        public float Factor { get; set; } = 0.5f;
        public int Repeat { get; set; } = 5;

        public IReadOnlyList<ModifierParameter> Parameters => new ModifierParameter[]
        {
            new("Factor", () => Factor, v => Factor = Math.Clamp(v, -2f, 2f)),
            new("Repeat", () => Repeat, v => Repeat = Math.Clamp((int)v, 0, 200), IsInteger: true),
        };

        public override Mesh Apply(Mesh input, Node? owner = null)
        {
            ArgumentNullException.ThrowIfNull(input);
            // Welded vertex ids and their neighbours.
            var ids = new Dictionary<(int, int, int), int>();
            var vertexId = new int[input.Vertices.Count];
            var positions = new List<Vector3>();
            for (var i = 0; i < input.Vertices.Count; i++)
            {
                var key = MeshOperators.Key(input.Vertices[i].Position);
                if (!ids.TryGetValue(key, out var id)) { id = ids[key] = positions.Count; positions.Add(input.Vertices[i].Position); }
                vertexId[i] = id;
            }
            var neighbours = new HashSet<int>[positions.Count];
            for (var i = 0; i < neighbours.Length; i++) neighbours[i] = new HashSet<int>();
            foreach (var (a, b) in input.GetEdges())
            {
                int ia = vertexId[a], ib = vertexId[b];
                if (ia == ib) continue;
                neighbours[ia].Add(ib);
                neighbours[ib].Add(ia);
            }

            var current = positions.ToArray();
            var next = new Vector3[current.Length];
            for (var r = 0; r < Repeat; r++)
            {
                for (var i = 0; i < current.Length; i++)
                {
                    if (neighbours[i].Count == 0) { next[i] = current[i]; continue; }
                    var avg = Vector3.Zero;
                    foreach (var n in neighbours[i]) avg += current[n];
                    avg /= neighbours[i].Count;
                    next[i] = current[i] + (avg - current[i]) * Factor;
                }
                (current, next) = (next, current);
            }
            return ModifierMath.WithPositions(input, (i, _) => current[vertexId[i]]);
        }

        public override Modifier Clone() => new SmoothModifier { IsEnabled = IsEnabled, Factor = Factor, Repeat = Repeat };
    }

    /// <summary>Triangulate (Blender): every polygon becomes triangles (quads split along
    /// the shorter diagonal, like Blender's Beauty) - what a game engine ends up with,
    /// so you can preview and export the exact triangulation.</summary>
    public sealed class TriangulateModifier : Modifier, IParameterizedModifier
    {
        public static readonly string[] QuadMethods = { "Beauty", "Fixed" };

        public override string Name => "Triangulate";
        public int QuadMethod { get; set; }

        public IReadOnlyList<ModifierParameter> Parameters => new ModifierParameter[]
        {
            new("Quad Method", () => QuadMethod, v => QuadMethod = Math.Clamp((int)v, 0, 1), QuadMethods),
        };

        public override Mesh Apply(Mesh input, Node? owner = null)
        {
            ArgumentNullException.ThrowIfNull(input);
            var output = input.Clone();
            var polygons = new List<Polygon>();
            foreach (var polygon in input.Polygons)
            {
                var idx = polygon.Indices;
                if (idx.Count < 3) continue;
                if (idx.Count == 3) { polygons.Add(new Polygon(idx) { MaterialSlotIndex = polygon.MaterialSlotIndex }); continue; }
                if (idx.Count == 4 && QuadMethod == 0)
                {
                    var d02 = Vector3.DistanceSquared(input.Vertices[idx[0]].Position, input.Vertices[idx[2]].Position);
                    var d13 = Vector3.DistanceSquared(input.Vertices[idx[1]].Position, input.Vertices[idx[3]].Position);
                    if (d13 < d02)
                    {
                        polygons.Add(new Polygon(new[] { idx[0], idx[1], idx[3] }) { MaterialSlotIndex = polygon.MaterialSlotIndex });
                        polygons.Add(new Polygon(new[] { idx[1], idx[2], idx[3] }) { MaterialSlotIndex = polygon.MaterialSlotIndex });
                        continue;
                    }
                }
                for (var i = 1; i + 1 < idx.Count; i++)
                    polygons.Add(new Polygon(new[] { idx[0], idx[i], idx[i + 1] }) { MaterialSlotIndex = polygon.MaterialSlotIndex });
            }
            output.SetTopology(input.Faces, polygons);
            return output;
        }

        public override Modifier Clone() => new TriangulateModifier { IsEnabled = IsEnabled, QuadMethod = QuadMethod };
    }

    /// <summary>Weld / Merge by Distance (Blender): merges vertices closer than
    /// <see cref="Distance"/>. "Keep UV Seams" only merges vertices whose UVs match too, so
    /// texture seams survive (what you usually want for game meshes).</summary>
    public sealed class WeldModifier : Modifier, IParameterizedModifier
    {
        public static readonly string[] Modes = { "All", "Keep UV Seams" };

        public override string Name => "Weld";
        public float Distance { get; set; } = 0.001f;
        public int Mode { get; set; }

        public IReadOnlyList<ModifierParameter> Parameters => new ModifierParameter[]
        {
            new("Distance", () => Distance, v => Distance = MathF.Max(0, v)),
            new("Mode", () => Mode, v => Mode = Math.Clamp((int)v, 0, 1), Modes),
        };

        public override Mesh Apply(Mesh input, Node? owner = null)
        {
            ArgumentNullException.ThrowIfNull(input);
            var output = input.Clone();
            var n = input.Vertices.Count;
            var target = new int[n];
            var cell = MathF.Max(Distance, 1e-6f);
            var grid = new Dictionary<(long, long, long), List<int>>();
            (long, long, long) Cell(Vector3 p) => ((long)MathF.Floor(p.X / cell), (long)MathF.Floor(p.Y / cell), (long)MathF.Floor(p.Z / cell));
            var d2 = Distance * Distance;
            for (var i = 0; i < n; i++)
            {
                var v = input.Vertices[i];
                target[i] = i;
                var (cx, cy, cz) = Cell(v.Position);
                var found = false;
                for (var dx = -1; dx <= 1 && !found; dx++)
                for (var dy = -1; dy <= 1 && !found; dy++)
                for (var dz = -1; dz <= 1 && !found; dz++)
                {
                    if (!grid.TryGetValue((cx + dx, cy + dy, cz + dz), out var list)) continue;
                    foreach (var j in list)
                    {
                        var w = input.Vertices[j];
                        if (Vector3.DistanceSquared(v.Position, w.Position) > d2) continue;
                        if (Mode == 1 && Vector2.DistanceSquared(v.UV, w.UV) > 1e-10f) continue;
                        target[i] = j;
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    var key = (cx, cy, cz);
                    if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
                    list.Add(i);
                }
            }

            bool Degenerate(IReadOnlyList<int> idx) => idx.Distinct().Count() < 3;
            var faces = input.Faces.Select(f => new Face(target[f.A], target[f.B], target[f.C])).Where(f => f.A != f.B && f.B != f.C && f.A != f.C).ToList();
            var polygons = new List<Polygon>();
            foreach (var p in input.Polygons)
            {
                // Drop consecutive duplicates (a collapsed edge) but keep the polygon.
                var idx = new List<int>();
                foreach (var i in p.Indices.Select(i => target[i]))
                    if (idx.Count == 0 || idx[^1] != i) idx.Add(i);
                while (idx.Count > 1 && idx[0] == idx[^1]) idx.RemoveAt(idx.Count - 1);
                if (!Degenerate(idx)) polygons.Add(new Polygon(idx) { MaterialSlotIndex = p.MaterialSlotIndex });
            }
            output.SetTopology(faces, polygons);
            output.RemoveUnusedVertices();
            return output;
        }

        public override Modifier Clone() => new WeldModifier { IsEnabled = IsEnabled, Distance = Distance, Mode = Mode };
    }

    /// <summary>Simple Deform (Blender): Twist, Bend, Taper or Stretch along a local axis.
    /// Angles are per unit of length along the axis, like Blender.</summary>
    public sealed class SimpleDeformModifier : Modifier, IParameterizedModifier
    {
        public static readonly string[] Methods = { "Twist", "Bend", "Taper", "Stretch" };
        public static readonly string[] Axes = { "X", "Y", "Z" };

        public override string Name => "Simple Deform";
        public int Method { get; set; }
        /// <summary>Degrees for Twist/Bend, factor for Taper/Stretch.</summary>
        public float Amount { get; set; } = 45f;
        public int Axis { get; set; } = 1;

        public IReadOnlyList<ModifierParameter> Parameters => new ModifierParameter[]
        {
            new("Method", () => Method, v => Method = Math.Clamp((int)v, 0, 3), Methods),
            new("Angle / Factor", () => Amount, v => Amount = v),
            new("Axis", () => Axis, v => Axis = Math.Clamp((int)v, 0, 2), Axes),
        };

        public override Mesh Apply(Mesh input, Node? owner = null)
        {
            ArgumentNullException.ThrowIfNull(input);
            // Work in a frame where the deform axis is local "z" and (x, y) the other two.
            Vector3 ToLocal(Vector3 p) => Axis switch { 0 => new Vector3(p.Y, p.Z, p.X), 1 => new Vector3(p.Z, p.X, p.Y), _ => p };
            Vector3 ToWorld(Vector3 q) => Axis switch { 0 => new Vector3(q.Z, q.X, q.Y), 1 => new Vector3(q.Y, q.Z, q.X), _ => q };
            var radians = Amount * MathF.PI / 180f;
            return ModifierMath.WithPositions(input, (_, p) =>
            {
                var q = ToLocal(p);
                switch (Method)
                {
                    case 0: // twist about the axis
                    {
                        var (s, c) = MathF.SinCos(q.Z * radians);
                        q = new Vector3(q.X * c - q.Y * s, q.X * s + q.Y * c, q.Z);
                        break;
                    }
                    case 1: // bend around the axis: x becomes an arc of radius 1/k
                    {
                        if (MathF.Abs(radians) < 1e-6f) break;
                        var theta = q.X * radians;
                        var r = 1f / radians;
                        var (s, c) = MathF.SinCos(theta);
                        q = new Vector3(-(q.Y - r) * s, (q.Y - r) * c + r, q.Z);
                        break;
                    }
                    case 2: // taper: scale across the axis grows along it
                    {
                        var k = 1f + q.Z * Amount;
                        q = new Vector3(q.X * k, q.Y * k, q.Z);
                        break;
                    }
                    default: // stretch along the axis, volume preserving across it
                    {
                        var k = MathF.Max(1e-3f, 1f + Amount);
                        var side = 1f / MathF.Sqrt(k);
                        q = new Vector3(q.X * side, q.Y * side, q.Z * k);
                        break;
                    }
                }
                return ToWorld(q);
            });
        }

        public override Modifier Clone() => new SimpleDeformModifier { IsEnabled = IsEnabled, Method = Method, Amount = Amount, Axis = Axis };
    }
}
