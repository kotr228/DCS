// Vortice annotates many D3D parameters as non-nullable, but D3D11 explicitly accepts
// null to unbind a resource/shader/state slot - so null is passed on purpose here.
#pragma warning disable CS8604, CS8625

using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Modifiers;
using JolieCat3D.Core.Skinning;
using Vortice.Direct3D11;
using CoreMaterial = JolieCat3D.Core.Materials.Material;
using CoreNode = JolieCat3D.Core.Scene.Node;

namespace JolieCat3D.Engine.Rendering.Dx
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuVertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 Uv;
        public Vector4 Color;
    }

    /// <summary>One material's index range inside a <see cref="GpuMesh"/>.</summary>
    internal readonly record struct DrawGroup(CoreMaterial? Material, int StartIndex, int IndexCount);

    /// <summary>
    /// A node's fully evaluated mesh (modifiers + skinning applied) on the GPU, plus the
    /// CPU copy used for picking. Rebuilt only when <see cref="Signature"/> changes.
    /// </summary>
    internal sealed class GpuMesh : IDisposable
    {
        public ulong Signature { get; init; }
        public Mesh Evaluated { get; init; } = null!;
        public Face[] Triangles { get; init; } = Array.Empty<Face>();
        public ID3D11Buffer? VertexBuffer { get; init; }
        public ID3D11Buffer? IndexBuffer { get; init; }
        public ID3D11Buffer? EdgeIndexBuffer { get; init; }
        public int EdgeIndexCount { get; init; }
        public IReadOnlyList<DrawGroup> Groups { get; init; } = Array.Empty<DrawGroup>();
        public bool HasVertexColors { get; init; }
        public bool IsHeatmap { get; init; }
        public Vector3 LocalMin { get; init; }
        public Vector3 LocalMax { get; init; }
        public int TotalIndices { get; init; }

        public void Dispose()
        {
            VertexBuffer?.Dispose();
            IndexBuffer?.Dispose();
            EdgeIndexBuffer?.Dispose();
        }
    }

    /// <summary>
    /// Evaluates and uploads each node's mesh, keyed by a cheap signature of everything
    /// the result depends on (base mesh content, modifier settings, skinning bone poses,
    /// weight-paint overlay). A gizmo drag that only moves a node therefore re-uploads
    /// nothing - only its world matrix changes.
    /// </summary>
    internal sealed class SceneMeshCache : IDisposable
    {
        private readonly DxGraphics _gfx;
        private readonly Dictionary<CoreNode, GpuMesh> _entries = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<CoreNode> _seenThisFrame = new(ReferenceEqualityComparer.Instance);

        public SceneMeshCache(DxGraphics gfx) => _gfx = gfx;

        public IReadOnlyDictionary<CoreNode, GpuMesh> Entries => _entries;

        public void BeginFrame() => _seenThisFrame.Clear();

        /// <summary>Drops entries for nodes that were not drawn this frame (deleted nodes).</summary>
        public void EndFrame()
        {
            if (_entries.Count == _seenThisFrame.Count) return;
            foreach (var node in _entries.Keys.Where(n => !_seenThisFrame.Contains(n)).ToList())
            {
                _entries[node].Dispose();
                _entries.Remove(node);
            }
        }

        public GpuMesh? Get(CoreNode node, int heatmapBoneIndex = -1, bool applyShapeKeys = true)
        {
            if (node.Mesh is not { } mesh) return null;
            _seenThisFrame.Add(node);

            var signature = ComputeSignature(node, mesh, heatmapBoneIndex) ^ (applyShapeKeys ? 0UL : 0x5DEECE66DUL);
            if (_entries.TryGetValue(node, out var existing) && existing.Signature == signature)
                return existing;

            Mesh evaluated;
            try
            {
                var shaped = applyShapeKeys ? ShapeKeys.Evaluate(mesh) : mesh;
                evaluated = ModifierStack.Evaluate(shaped, node.Modifiers, node);
                if (node.SkinBinding is { } binding)
                    evaluated = SkinningEvaluator.Deform(evaluated, node.GetWorldTransform(), binding);
                if (heatmapBoneIndex >= 0)
                    evaluated = WeightVisualization.BuildHeatmapMesh(evaluated, heatmapBoneIndex);
            }
            catch (Exception)
            {
                // A modifier that throws (e.g. a degenerate Boolean) must not take the whole viewport down.
                evaluated = mesh;
            }

            var built = Upload(evaluated, signature, heatmapBoneIndex >= 0);
            existing?.Dispose();
            _entries[node] = built;
            return built;
        }

        private GpuMesh Upload(Mesh mesh, ulong signature, bool heatmap)
        {
            var vertexCount = mesh.Vertices.Count;
            var vertices = new GpuVertex[vertexCount];
            var hasColors = false;
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            for (var i = 0; i < vertexCount; i++)
            {
                var v = mesh.Vertices[i];
                var c = new Vector4(v.Color.R, v.Color.G, v.Color.B, v.Color.A);
                if (c != Vector4.One && c != Vector4.Zero) hasColors = true;
                vertices[i] = new GpuVertex { Position = v.Position, Normal = v.Normal, Uv = v.UV, Color = c };
                min = Vector3.Min(min, v.Position);
                max = Vector3.Max(max, v.Position);
            }

            if (hasColors)
            {
                // Vertices that never got a colour (all zero) render white, not black.
                for (var i = 0; i < vertexCount; i++)
                    if (vertices[i].Color == Vector4.Zero) vertices[i].Color = Vector4.One;
            }

            // Group triangles by effective material.
            var groups = new List<(CoreMaterial? Material, List<int> Indices)>();
            List<int> GroupFor(CoreMaterial? material)
            {
                foreach (var g in groups) if (ReferenceEquals(g.Material, material)) return g.Indices;
                var list = new List<int>();
                groups.Add((material, list));
                return list;
            }

            var triangles = new List<Face>(mesh.Faces.Count + mesh.Polygons.Count * 2);
            foreach (var face in mesh.Faces)
            {
                if (!Valid(face, vertexCount)) continue;
                var list = GroupFor(mesh.Material);
                list.Add(face.A); list.Add(face.B); list.Add(face.C);
                triangles.Add(face);
            }

            foreach (var polygon in mesh.Polygons)
            {
                var list = GroupFor(mesh.GetEffectiveMaterial(polygon));
                foreach (var tri in polygon.Triangulate())
                {
                    if (!Valid(tri, vertexCount)) continue;
                    list.Add(tri.A); list.Add(tri.B); list.Add(tri.C);
                    triangles.Add(tri);
                }
            }

            var indices = new List<uint>(triangles.Count * 3);
            var drawGroups = new List<DrawGroup>();
            foreach (var (material, list) in groups)
            {
                if (list.Count == 0) continue;
                drawGroups.Add(new DrawGroup(material, indices.Count, list.Count));
                foreach (var i in list) indices.Add((uint)i);
            }

            // Unique edges for wireframe / edit overlays.
            var edgeIndices = new List<uint>();
            foreach (var (a, b) in SafeEdges(mesh))
            {
                if (a >= vertexCount || b >= vertexCount) continue;
                edgeIndices.Add((uint)a); edgeIndices.Add((uint)b);
            }

            ID3D11Buffer? vb = null, ib = null, eb = null;
            if (vertexCount > 0)
                vb = _gfx.Device.CreateBuffer(vertices.AsSpan(), BindFlags.VertexBuffer, ResourceUsage.Immutable, CpuAccessFlags.None, ResourceOptionFlags.None, 0, 0);
            if (indices.Count > 0)
                ib = _gfx.Device.CreateBuffer(CollectionsMarshal.AsSpan(indices), BindFlags.IndexBuffer, ResourceUsage.Immutable, CpuAccessFlags.None, ResourceOptionFlags.None, 0, 0);
            if (edgeIndices.Count > 0)
                eb = _gfx.Device.CreateBuffer(CollectionsMarshal.AsSpan(edgeIndices), BindFlags.IndexBuffer, ResourceUsage.Immutable, CpuAccessFlags.None, ResourceOptionFlags.None, 0, 0);

            if (vertexCount == 0) { min = Vector3.Zero; max = Vector3.Zero; }

            return new GpuMesh
            {
                Signature = signature,
                Evaluated = mesh,
                Triangles = triangles.ToArray(),
                VertexBuffer = vb,
                IndexBuffer = ib,
                EdgeIndexBuffer = eb,
                EdgeIndexCount = edgeIndices.Count,
                Groups = drawGroups,
                HasVertexColors = hasColors,
                IsHeatmap = heatmap,
                LocalMin = min,
                LocalMax = max,
                TotalIndices = indices.Count,
            };

            static bool Valid(Face f, int count) =>
                (uint)f.A < (uint)count && (uint)f.B < (uint)count && (uint)f.C < (uint)count;
        }

        private static IEnumerable<(int A, int B)> SafeEdges(Mesh mesh)
        {
            try { return mesh.GetEdges().ToList(); }
            catch (Exception) { return Array.Empty<(int, int)>(); }
        }

        // ---------------- signature ----------------

        private static readonly Dictionary<Type, PropertyInfo[]> ModifierProperties = new();

        private static ulong ComputeSignature(CoreNode node, Mesh mesh, int heatmapBoneIndex)
        {
            var h = MeshFingerprint.Compute(mesh);
            var dependsOnWorld = node.SkinBinding is not null;

            foreach (var modifier in node.Modifiers)
            {
                var type = modifier.GetType();
                h = Mix(h, (ulong)type.FullName!.GetHashCode());
                h = Mix(h, modifier.IsEnabled ? 1UL : 0UL);
                if (!modifier.IsEnabled) continue;

                foreach (var prop in PropertiesOf(type))
                {
                    object? value;
                    try { value = prop.GetValue(modifier); }
                    catch (Exception) { continue; }

                    switch (value)
                    {
                        case null: h = Mix(h, 0); break;
                        case CoreNode other:
                            dependsOnWorld = true;
                            h = Mix(h, (ulong)RuntimeHelpers.GetHashCode(other));
                            h = MixMatrix(h, other.GetWorldTransform());
                            if (other.Mesh is { } otherMesh) h = Mix(h, MeshFingerprint.Compute(otherMesh));
                            break;
                        case float f: h = Mix(h, BitConverter.SingleToUInt32Bits(f)); break;
                        case double d: h = Mix(h, (ulong)BitConverter.DoubleToInt64Bits(d)); break;
                        case Vector3 v: h = Mix(h, BitConverter.SingleToUInt32Bits(v.X)); h = Mix(h, BitConverter.SingleToUInt32Bits(v.Y)); h = Mix(h, BitConverter.SingleToUInt32Bits(v.Z)); break;
                        default: h = Mix(h, (ulong)value.GetHashCode()); break;
                    }
                }
            }

            if (node.SkinBinding is { } binding)
            {
                foreach (var bone in binding.Bones) h = MixMatrix(h, bone.GetWorldTransform());
            }

            if (dependsOnWorld) h = MixMatrix(h, node.GetWorldTransform());
            h = Mix(h, (ulong)(heatmapBoneIndex + 1));
            return h;
        }

        private static PropertyInfo[] PropertiesOf(Type type)
        {
            if (ModifierProperties.TryGetValue(type, out var props)) return props;
            props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && p.Name is not ("Name" or "IsEnabled"))
                .ToArray();
            ModifierProperties[type] = props;
            return props;
        }

        private static ulong MixMatrix(ulong h, Matrix4x4 m)
        {
            h = Mix(h, BitConverter.SingleToUInt32Bits(m.M11)); h = Mix(h, BitConverter.SingleToUInt32Bits(m.M12)); h = Mix(h, BitConverter.SingleToUInt32Bits(m.M13));
            h = Mix(h, BitConverter.SingleToUInt32Bits(m.M21)); h = Mix(h, BitConverter.SingleToUInt32Bits(m.M22)); h = Mix(h, BitConverter.SingleToUInt32Bits(m.M23));
            h = Mix(h, BitConverter.SingleToUInt32Bits(m.M31)); h = Mix(h, BitConverter.SingleToUInt32Bits(m.M32)); h = Mix(h, BitConverter.SingleToUInt32Bits(m.M33));
            h = Mix(h, BitConverter.SingleToUInt32Bits(m.M41)); h = Mix(h, BitConverter.SingleToUInt32Bits(m.M42)); h = Mix(h, BitConverter.SingleToUInt32Bits(m.M43));
            return h;
        }

        private static ulong Mix(ulong h, ulong v)
        {
            h ^= v + 0x9E3779B97F4A7C15UL + (h << 6) + (h >> 2);
            return h * 1099511628211UL;
        }

        public void Dispose()
        {
            foreach (var entry in _entries.Values) entry.Dispose();
            _entries.Clear();
        }
    }
}
