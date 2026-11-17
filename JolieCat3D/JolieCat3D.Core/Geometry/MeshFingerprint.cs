using System.Numerics;
using System.Runtime.CompilerServices;

namespace JolieCat3D.Core.Geometry
{
    /// <summary>A fast 64-bit content fingerprint of a mesh (FNV-1a over every vertex
    /// attribute, face, polygon, seam and material reference) used to detect whether a
    /// mesh actually changed between two snapshots.</summary>
    public static class MeshFingerprint
    {
        private const ulong Offset = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        public static ulong Compute(Mesh mesh)
        {
            ArgumentNullException.ThrowIfNull(mesh);
            var h = Offset;

            Mix(ref h, (ulong)mesh.Vertices.Count);
            foreach (var v in mesh.Vertices)
            {
                Mix(ref h, v.Position); Mix(ref h, v.Normal);
                Mix(ref h, v.UV.X); Mix(ref h, v.UV.Y);
                Mix(ref h, v.Color.R); Mix(ref h, v.Color.G); Mix(ref h, v.Color.B); Mix(ref h, v.Color.A);
                Mix(ref h, (ulong)(uint)v.BoneIndices.X); Mix(ref h, (ulong)(uint)v.BoneIndices.Y);
                Mix(ref h, (ulong)(uint)v.BoneIndices.Z); Mix(ref h, (ulong)(uint)v.BoneIndices.W);
                Mix(ref h, v.BoneWeights.X); Mix(ref h, v.BoneWeights.Y); Mix(ref h, v.BoneWeights.Z); Mix(ref h, v.BoneWeights.W);
            }

            Mix(ref h, (ulong)mesh.Faces.Count);
            foreach (var f in mesh.Faces) { Mix(ref h, (ulong)(uint)f.A); Mix(ref h, (ulong)(uint)f.B); Mix(ref h, (ulong)(uint)f.C); }

            Mix(ref h, (ulong)mesh.Polygons.Count);
            foreach (var p in mesh.Polygons)
            {
                Mix(ref h, (ulong)(uint)p.MaterialSlotIndex);
                Mix(ref h, (ulong)p.Indices.Count);
                foreach (var i in p.Indices) Mix(ref h, (ulong)(uint)i);
            }

            // Seams are a set - combine order-independently.
            ulong seams = 0;
            foreach (var (a, b) in mesh.SeamEdges) seams += ((ulong)(uint)a << 32 | (uint)b) * Prime;
            Mix(ref h, seams);
            ulong creases = 0;
            foreach (var (a, b) in mesh.CreaseEdges) creases += ((ulong)(uint)a << 32 | (uint)b) * (Prime + 2);
            Mix(ref h, creases);

            Mix(ref h, (ulong)(uint)(mesh.Material is null ? 0 : RuntimeHelpers.GetHashCode(mesh.Material)));
            foreach (var slot in mesh.MaterialSlots) Mix(ref h, (ulong)(uint)RuntimeHelpers.GetHashCode(slot));
            Mix(ref h, (ulong)mesh.Name.GetHashCode(StringComparison.Ordinal));

            // Shape keys: values and identity of the stored shapes (arrays are never mutated in place).
            Mix(ref h, (ulong)(uint)(mesh.ShapeBasis is null ? 0 : RuntimeHelpers.GetHashCode(mesh.ShapeBasis)));
            foreach (var key in mesh.ShapeKeys)
            {
                Mix(ref h, (ulong)(uint)RuntimeHelpers.GetHashCode(key.Positions));
                Mix(ref h, key.Value);
                Mix(ref h, (ulong)key.Name.GetHashCode(StringComparison.Ordinal));
            }

            return h;
        }

        private static void Mix(ref ulong h, Vector3 v) { Mix(ref h, v.X); Mix(ref h, v.Y); Mix(ref h, v.Z); }

        private static void Mix(ref ulong h, float value) => Mix(ref h, BitConverter.SingleToUInt32Bits(value));

        private static void Mix(ref ulong h, ulong value)
        {
            h ^= value;
            h *= Prime;
        }
    }
}
