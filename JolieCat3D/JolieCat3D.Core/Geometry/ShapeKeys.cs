using System.Numerics;

namespace JolieCat3D.Core.Geometry
{
    /// <summary>
    /// A stored shape of a mesh (Blender "Shape Key" / morph target): one absolute position
    /// per vertex. Its <see cref="Value"/> (0..1) blends it over the basis shape.
    /// </summary>
    public sealed class ShapeKey
    {
        public ShapeKey(string name, Vector3[] positions)
        {
            Name = name;
            Positions = positions ?? throw new ArgumentNullException(nameof(positions));
        }

        public string Name { get; set; }

        /// <summary>Never mutated in place - replaced as a whole, so snapshots can share it.</summary>
        public Vector3[] Positions { get; }

        public float Value
        {
            get => _value;
            set => _value = Math.Clamp(value, 0f, 1f);
        }
        private float _value;

        public ShapeKey Clone() => new(Name, Positions) { Value = Value };
    }

    /// <summary>Creating, saving and evaluating shape keys.</summary>
    public static class ShapeKeys
    {
        /// <summary>True when the key still matches the mesh (topology edits invalidate keys).</summary>
        public static bool IsValid(Mesh mesh, ShapeKey key) => key.Positions.Length == mesh.Vertices.Count;

        /// <summary>Captures the current shape as the basis every key is relative to.</summary>
        public static void AddBasis(Mesh mesh)
        {
            mesh.ShapeBasis = mesh.Vertices.Select(v => v.Position).ToArray();
        }

        /// <summary>
        /// "Save Shape Key": stores the mesh's current positions as a new key (value 0) and puts
        /// the vertices back to the basis. Returns null when there's no matching basis or
        /// nothing moved.
        /// </summary>
        public static ShapeKey? SaveCurrentAsShapeKey(Mesh mesh, string name)
        {
            if (mesh.ShapeBasis is not { } basis || basis.Length != mesh.Vertices.Count) return null;
            var positions = mesh.Vertices.Select(v => v.Position).ToArray();
            if (positions.SequenceEqual(basis)) return null;
            var key = new ShapeKey(name, positions);
            mesh.ShapeKeys.Add(key);
            for (var i = 0; i < basis.Length; i++) mesh.SetVertexPosition(i, basis[i]);
            return key;
        }

        /// <summary>Basis + Σ value × (key − basis). Returns the mesh itself when no key is active.</summary>
        public static Mesh Evaluate(Mesh mesh)
        {
            if (mesh.ShapeBasis is not { } basis || basis.Length != mesh.Vertices.Count) return mesh;
            var active = mesh.ShapeKeys.Where(k => k.Value > 0f && IsValid(mesh, k)).ToList();
            if (active.Count == 0) return mesh;

            var result = mesh.Clone();
            for (var i = 0; i < basis.Length; i++)
            {
                var p = mesh.Vertices[i].Position;
                foreach (var k in active) p += (k.Positions[i] - basis[i]) * k.Value;
                result.SetVertexPosition(i, p);
            }
            result.RecalculateNormals();
            return result;
        }

        public static string UniqueName(Mesh mesh, string baseName)
        {
            var name = baseName;
            var n = 1;
            while (mesh.ShapeKeys.Any(k => k.Name == name)) name = $"{baseName}.{n++:000}";
            return name;
        }
    }
}
