using System.Numerics;
using JolieCat3D.Core.Numerics;

namespace JolieCat3D.Core.Materials
{
    /// <summary>Texture Paint brush modes (Blender: Draw, Soften, Smear, Fill).</summary>
    public enum TexturePaintMode
    {
        Draw,
        Soften,
        Smear,
        Fill,
    }

    /// <summary>The non-Draw texture paint brushes. Same pixel space and falloff as
    /// <see cref="TexturePaintBrush.Apply"/> (V flipped, radius in pixels).</summary>
    public static class TexturePaintBrushExtras
    {
        private static (float X, float Y) ToPixel(TextureBuffer buffer, Vector2 uv) => (uv.X * buffer.Width, (1f - uv.Y) * buffer.Height);

        private static Color4 Sample(TextureBuffer buffer, float x, float y)
        {
            var ix = Math.Clamp((int)MathF.Floor(x), 0, buffer.Width - 1);
            var iy = Math.Clamp((int)MathF.Floor(y), 0, buffer.Height - 1);
            return buffer.GetPixel(ix, iy);
        }

        /// <summary>Calls <paramref name="pixel"/> for every pixel under the brush with its falloff.</summary>
        private static void ForEach(TextureBuffer buffer, float cx, float cy, float radius, Action<int, int, float> pixel)
        {
            var minX = Math.Max(0, (int)MathF.Floor(cx - radius));
            var maxX = Math.Min(buffer.Width - 1, (int)MathF.Ceiling(cx + radius));
            var minY = Math.Max(0, (int)MathF.Floor(cy - radius));
            var maxY = Math.Min(buffer.Height - 1, (int)MathF.Ceiling(cy + radius));
            for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
            {
                var dx = x + 0.5f - cx; var dy = y + 0.5f - cy;
                var d = MathF.Sqrt(dx * dx + dy * dy);
                if (d > radius) continue;
                pixel(x, y, 1f - d / radius);
            }
        }

        /// <summary>Soften: blends each pixel towards the average of its 3x3 neighbourhood.</summary>
        public static void Soften(TextureBuffer buffer, Vector2 uv, float radius, float strength)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            if (radius <= 0f) return;
            var (cx, cy) = ToPixel(buffer, uv);
            var source = buffer.Clone(); // read from the unmodified stroke start, so the blur is order independent
            ForEach(buffer, cx, cy, radius, (x, y, falloff) =>
            {
                var sum = Vector4.Zero;
                var n = 0;
                for (var j = -1; j <= 1; j++)
                for (var i = -1; i <= 1; i++)
                {
                    int sx = x + i, sy = y + j;
                    if (sx < 0 || sy < 0 || sx >= buffer.Width || sy >= buffer.Height) continue;
                    sum += source.GetPixel(sx, sy).Value;
                    n++;
                }
                var avg = new Color4(sum / n);
                buffer.SetPixel(x, y, Color4.Lerp(source.GetPixel(x, y), avg, Math.Clamp(falloff * strength, 0f, 1f)));
            });
        }

        /// <summary>Smear: drags colour along the stroke from <paramref name="previousUv"/> to <paramref name="uv"/>.</summary>
        public static void Smear(TextureBuffer buffer, Vector2 uv, Vector2 previousUv, float radius, float strength)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            if (radius <= 0f) return;
            var (cx, cy) = ToPixel(buffer, uv);
            var (px, py) = ToPixel(buffer, previousUv);
            float dx = cx - px, dy = cy - py;
            if (dx * dx + dy * dy < 1e-6f) return;
            var source = buffer.Clone();
            ForEach(buffer, cx, cy, radius, (x, y, falloff) =>
            {
                var from = Sample(source, x + 0.5f - dx, y + 0.5f - dy);
                buffer.SetPixel(x, y, Color4.Lerp(source.GetPixel(x, y), from, Math.Clamp(falloff * strength, 0f, 1f)));
            });
        }

        /// <summary>Fill: blends the whole texture towards <paramref name="color"/>.</summary>
        public static void Fill(TextureBuffer buffer, Color4 color, float strength)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            var t = Math.Clamp(strength, 0f, 1f);
            for (var y = 0; y < buffer.Height; y++)
            for (var x = 0; x < buffer.Width; x++)
                buffer.SetPixel(x, y, Color4.Lerp(buffer.GetPixel(x, y), color, t));
        }
    }
}
