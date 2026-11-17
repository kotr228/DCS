using System.Numerics;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.Core.Modifiers
{
    public enum ShrinkwrapMode
    {
        /// <summary>Each vertex moves to the closest point of the target surface.</summary>
        NearestSurface,

        /// <summary>Each vertex moves along its own normal (both ways) until it hits the target.</summary>
        Project,
    }

    /// <summary>
    /// Shrinkwrap (Blender): snaps the mesh onto <see cref="Target"/>'s surface, then pushes
    /// it <see cref="Offset"/> along the surface normal - for retopology, clothing and decals
    /// that must sit on another object. Works in world space so both objects can be
    /// anywhere; the target is read as its raw mesh (no modifiers), like Boolean.
    /// </summary>
    public sealed class ShrinkwrapModifier : Modifier
    {
        public override string Name => "Shrinkwrap";

        public Node? Target { get; set; }

        public ShrinkwrapMode Mode { get; set; } = ShrinkwrapMode.NearestSurface;

        /// <summary>Distance kept from the surface (along its normal).</summary>
        public float Offset { get; set; }

        public override Mesh Apply(Mesh input, Node? owner = null)
        {
            ArgumentNullException.ThrowIfNull(input);
            if (Target?.Mesh is not { } targetMesh || owner is null || ReferenceEquals(Target, owner)) return input;
            var world = owner.GetWorldTransform();
            if (!Matrix4x4.Invert(world, out var toLocal)) return input;

            var targetWorld = Target.GetWorldTransform();
            var tris = targetMesh.GetRenderFaces().Select(f => (
                Vector3.Transform(targetMesh.Vertices[f.A].Position, targetWorld),
                Vector3.Transform(targetMesh.Vertices[f.B].Position, targetWorld),
                Vector3.Transform(targetMesh.Vertices[f.C].Position, targetWorld))).ToList();
            if (tris.Count == 0) return input;
            var bvh = new TriangleBvh(tris);
            Matrix4x4.Invert(world, out var inverse);
            var normalMatrix = Matrix4x4.Transpose(inverse);

            var output = input.Clone();
            for (var i = 0; i < input.Vertices.Count; i++)
            {
                var p = Vector3.Transform(input.Vertices[i].Position, world);
                Vector3? hitPoint = null;
                var surfaceNormal = Vector3.Zero;

                if (Mode == ShrinkwrapMode.Project)
                {
                    var n = Vector3.TransformNormal(input.Vertices[i].Normal, normalMatrix);
                    if (n.LengthSquared() > 1e-12f)
                    {
                        n = Vector3.Normalize(n);
                        var forward = bvh.Intersect(p, n, 0f);
                        var backward = bvh.Intersect(p, -n, 0f);
                        var best = forward is { } f && (backward is not { } b0 || f.T <= b0.T) ? (forward, n) : (backward, -n);
                        if (best.Item1 is { } hit)
                        {
                            hitPoint = p + best.Item2 * hit.T;
                            surfaceNormal = bvh.TriangleNormal(hit.Triangle);
                        }
                    }
                }
                else if (bvh.ClosestPoint(p) is { } closest)
                {
                    hitPoint = closest.Point;
                    surfaceNormal = bvh.TriangleNormal(closest.Triangle);
                    // Offset outward from the side the vertex came from.
                    if (Vector3.Dot(p - closest.Point, surfaceNormal) < 0) surfaceNormal = -surfaceNormal;
                }

                if (hitPoint is not { } q) continue;
                if (Mode == ShrinkwrapMode.Project && Vector3.Dot(p - q, surfaceNormal) < 0) surfaceNormal = -surfaceNormal;
                var moved = q + surfaceNormal * Offset;
                output.SetVertexPosition(i, Vector3.Transform(moved, toLocal));
            }
            output.RecalculateNormals();
            return output;
        }

        public override Modifier Clone() => new ShrinkwrapModifier { IsEnabled = IsEnabled, Target = Target, Mode = Mode, Offset = Offset };
    }
}
