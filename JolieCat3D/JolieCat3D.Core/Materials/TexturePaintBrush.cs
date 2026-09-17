using System.Numerics;
using JolieCat3D.Core.Numerics;

namespace JolieCat3D.Core.Materials
{
    /// <summary>
    /// Texture Paint mode's own brush algorithm - stamps <paramref name="color"/> onto
    /// a <see cref="TextureBuffer"/> in a circle around one UV hit point, the same
    /// "world/UV hit point + radius, linear falloff, one tick per mouse-move sample"
    /// shape <see cref="VertexPaintBrush"/>/<see cref="Skinning.WeightPaintBrush"/>
    /// already established for their own paint modes - just operating over a flat 2D
    /// pixel grid instead of a mesh's own vertex list, and <paramref name="radius"/>
    /// itself is a PIXEL distance (this buffer's own resolution-dependent unit).
    /// </summary>
    public static class TexturePaintBrush
    {
        /// <summary>Blends <paramref name="color"/> into every pixel within
        /// <paramref name="radius"/> pixels of <paramref name="uv"/> (converted to this
        /// buffer's own pixel space - see <see cref="TextureBuffer"/>'s own remarks on
        /// the V-flip/top-down convention), each one weighted by the same linear falloff
        /// (1 at the brush's own center, fading to 0 at its edge) every other brush in
        /// this project already uses, scaled by <paramref name="strength"/> - a 0-1 blend
        /// factor toward <paramref name="color"/>, exactly like <see cref="VertexPaintBrush.Apply"/>'s
        /// own Strength. A no-op for a non-positive <paramref name="radius"/> (nothing
        /// meaningful to stamp).</summary>
        public static void Apply(TextureBuffer buffer, Vector2 uv, float radius, Color4 color, float strength)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            if (radius <= 0f) return;

            var centerX = uv.X * buffer.Width;
            var centerY = (1f - uv.Y) * buffer.Height;

            var minX = Math.Max(0, (int)MathF.Floor(centerX - radius));
            var maxX = Math.Min(buffer.Width - 1, (int)MathF.Ceiling(centerX + radius));
            var minY = Math.Max(0, (int)MathF.Floor(centerY - radius));
            var maxY = Math.Min(buffer.Height - 1, (int)MathF.Ceiling(centerY + radius));

            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    // Sampled at each pixel's own center, not its integer corner - the
                    // same "measure from the middle of the texel" convention any
                    // resolution-independent brush needs to avoid a visibly lopsided
                    // stamp.
                    var dx = (x + 0.5f) - centerX;
                    var dy = (y + 0.5f) - centerY;
                    var distance = MathF.Sqrt(dx * dx + dy * dy);
                    if (distance > radius) continue;

                    var falloff = 1f - distance / radius;
                    var blend = Math.Clamp(falloff * strength, 0f, 1f);

                    var existing = buffer.GetPixel(x, y);
                    buffer.SetPixel(x, y, Color4.Lerp(existing, color, blend));
                }
            }
        }
    }
}
