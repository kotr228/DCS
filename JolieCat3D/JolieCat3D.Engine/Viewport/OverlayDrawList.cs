using System.Numerics;
using System.Runtime.InteropServices;
using JolieCat3D.Core.Numerics;

namespace JolieCat3D.Engine.Viewport
{
    /// <summary>Which depth behaviour an overlay primitive uses.</summary>
    public enum OverlayLayer
    {
        /// <summary>Depth-tested against the scene (edit-mode edges, camera frustums, ...).</summary>
        Scene,

        /// <summary>Always drawn on top of everything (gizmos).</summary>
        OnTop,
    }

    /// <summary>
    /// Per-frame list of screen-space-thick lines, round points and coloured triangles
    /// in world space. Gizmos, edit-mode markers and helper shapes fill one of these;
    /// the renderer draws it after the scene. Colours are sRGB.
    /// </summary>
    public sealed class OverlayDrawList
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct LineInstance
        {
            public Vector3 A;
            public Vector3 B;
            public Vector4 Color;
            public float Width;
            public float DepthBias;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PointInstance
        {
            public Vector3 Position;
            public Vector4 Color;
            public float Size;
            public float DepthBias;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct TriVertex
        {
            public Vector3 Position;
            public Vector3 Normal;
            public Vector4 Color;
        }

        internal readonly List<LineInstance>[] Lines = { new(), new() };
        internal readonly List<PointInstance>[] Points = { new(), new() };
        internal readonly List<TriVertex>[] Triangles = { new(), new() };

        public void Clear()
        {
            foreach (var l in Lines) l.Clear();
            foreach (var p in Points) p.Clear();
            foreach (var t in Triangles) t.Clear();
        }

        public bool IsEmpty => Lines.All(l => l.Count == 0) && Points.All(p => p.Count == 0) && Triangles.All(t => t.Count == 0);

        public void AddLine(Vector3 a, Vector3 b, Color4 color, float widthPixels = 1.5f, OverlayLayer layer = OverlayLayer.Scene, float depthBias = 0f) =>
            Lines[(int)layer].Add(new LineInstance { A = a, B = b, Color = color.Value, Width = widthPixels, DepthBias = depthBias });

        public void AddPolyline(IReadOnlyList<Vector3> points, Color4 color, float widthPixels = 1.5f, bool closed = false, OverlayLayer layer = OverlayLayer.Scene)
        {
            for (var i = 0; i + 1 < points.Count; i++) AddLine(points[i], points[i + 1], color, widthPixels, layer);
            if (closed && points.Count > 2) AddLine(points[^1], points[0], color, widthPixels, layer);
        }

        public void AddPoint(Vector3 position, Color4 color, float sizePixels = 6f, OverlayLayer layer = OverlayLayer.Scene, float depthBias = 0f) =>
            Points[(int)layer].Add(new PointInstance { Position = position, Color = color.Value, Size = sizePixels, DepthBias = depthBias });

        /// <summary>A triangle; pass a non-zero normal to get simple shading.</summary>
        public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Color4 color, OverlayLayer layer = OverlayLayer.Scene, Vector3 normal = default)
        {
            var list = Triangles[(int)layer];
            list.Add(new TriVertex { Position = a, Normal = normal, Color = color.Value });
            list.Add(new TriVertex { Position = b, Normal = normal, Color = color.Value });
            list.Add(new TriVertex { Position = c, Normal = normal, Color = color.Value });
        }

        /// <summary>Circle of <paramref name="radius"/> around <paramref name="center"/> in the plane with <paramref name="normal"/>.</summary>
        public void AddCircle(Vector3 center, Vector3 normal, float radius, Color4 color, float widthPixels = 2f, OverlayLayer layer = OverlayLayer.Scene, int segments = 64)
        {
            var (u, v) = Basis(normal);
            var previous = center + u * radius;
            for (var i = 1; i <= segments; i++)
            {
                var a = i * MathF.Tau / segments;
                var p = center + (u * MathF.Cos(a) + v * MathF.Sin(a)) * radius;
                AddLine(previous, p, color, widthPixels, layer);
                previous = p;
            }
        }

        /// <summary>Solid cone from <paramref name="baseCenter"/> to <paramref name="tip"/>.</summary>
        public void AddCone(Vector3 baseCenter, Vector3 tip, float radius, Color4 color, OverlayLayer layer = OverlayLayer.OnTop, int segments = 16)
        {
            var axis = tip - baseCenter;
            if (axis.LengthSquared() < 1e-12f) return;
            var (u, v) = Basis(Vector3.Normalize(axis));
            for (var i = 0; i < segments; i++)
            {
                var a0 = i * MathF.Tau / segments;
                var a1 = (i + 1) * MathF.Tau / segments;
                var p0 = baseCenter + (u * MathF.Cos(a0) + v * MathF.Sin(a0)) * radius;
                var p1 = baseCenter + (u * MathF.Cos(a1) + v * MathF.Sin(a1)) * radius;
                var n = Vector3.Normalize(Vector3.Cross(p1 - tip, p0 - tip));
                AddTriangle(tip, p0, p1, color, layer, n);
                AddTriangle(baseCenter, p1, p0, color, layer, -Vector3.Normalize(axis));
            }
        }

        /// <summary>Axis-aligned (in the given basis) solid box.</summary>
        public void AddBox(Vector3 center, Vector3 axisX, Vector3 axisY, Vector3 axisZ, float halfSize, Color4 color, OverlayLayer layer = OverlayLayer.OnTop)
        {
            var x = Vector3.Normalize(axisX) * halfSize;
            var y = Vector3.Normalize(axisY) * halfSize;
            var z = Vector3.Normalize(axisZ) * halfSize;
            void Quad(Vector3 n, Vector3 a, Vector3 b)
            {
                var c = center + n;
                var p0 = c - a - b; var p1 = c + a - b; var p2 = c + a + b; var p3 = c - a + b;
                var nn = Vector3.Normalize(n);
                AddTriangle(p0, p1, p2, color, layer, nn);
                AddTriangle(p0, p2, p3, color, layer, nn);
            }
            Quad(x, y, z); Quad(-x, z, y); Quad(y, z, x); Quad(-y, x, z); Quad(z, x, y); Quad(-z, y, x);
        }

        internal static (Vector3 U, Vector3 V) Basis(Vector3 normal)
        {
            normal = Vector3.Normalize(normal);
            var helper = MathF.Abs(normal.Y) < 0.95f ? Vector3.UnitY : Vector3.UnitX;
            var u = Vector3.Normalize(Vector3.Cross(helper, normal));
            var v = Vector3.Cross(normal, u);
            return (u, v);
        }
    }
}
