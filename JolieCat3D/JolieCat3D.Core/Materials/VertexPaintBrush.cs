using System.Numerics;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Numerics;

namespace JolieCat3D.Core.Materials
{
    /// <summary>
    /// Vertex Paint mode's own brush backend - the same "world hit point + radius, one
    /// tick per mouse-move sample" shape <see cref="Skinning.WeightPaintBrush"/> already
    /// established, considerably simpler here since <see cref="Vertex.Color"/> is a
    /// plain independent RGBA value (unlike bone weights, nothing here needs to sum to
    /// 1 across several slots, so there's no renormalization/eviction logic to write at
    /// all - just a direct per-vertex color blend).
    /// </summary>
    public static class VertexPaintBrush
    {
        /// <summary>Blends every vertex of <paramref name="mesh"/> within
        /// <paramref name="radius"/> of <paramref name="worldHitPoint"/> toward
        /// <paramref name="targetColor"/> by <paramref name="strength"/>, falling off
        /// linearly to nothing at the brush's own edge - the same falloff shape
        /// <see cref="Skinning.WeightPaintBrush.Apply"/> already uses. Vertices outside
        /// <paramref name="radius"/> are left completely untouched.</summary>
        public static void Apply(Mesh mesh, Matrix4x4 meshWorldTransform, Vector3 worldHitPoint, float radius, Color4 targetColor, float strength)
        {
            ArgumentNullException.ThrowIfNull(mesh);
            if (radius <= 0f || strength <= 0f) return;

            strength = Math.Clamp(strength, 0f, 1f);

            for (var i = 0; i < mesh.Vertices.Count; i++)
            {
                var vertex = mesh.Vertices[i];
                var worldPosition = Vector3.Transform(vertex.Position, meshWorldTransform);
                var distance = Vector3.Distance(worldPosition, worldHitPoint);
                if (distance > radius) continue;

                var falloff = 1f - distance / radius;
                var blend = Math.Clamp(strength * falloff, 0f, 1f);
                if (blend <= 0f) continue;

                mesh.SetVertexColor(i, Color4.Lerp(vertex.Color, targetColor, blend));
            }
        }
    }
}
