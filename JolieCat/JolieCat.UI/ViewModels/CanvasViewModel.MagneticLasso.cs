using System;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    public partial class CanvasViewModel
    {
        // ================= Magnetic Lasso =================
        //
        // Freehand lasso whose points stick to nearby edges: each point is moved to the
        // spot with the strongest brightness edge (Sobel gradient) within SnapRadius
        // pixels. The layer's brightness is read once when the stroke starts, and the
        // drag is sampled every couple of pixels so the outline follows the edge closely.

        private const int MagneticSnapRadius = 8;
        private const float MagneticSampleStep = 3f;

        private byte[]? _magneticLuma;
        private int _magneticWidth;
        private int _magneticHeight;
        private SKPoint _magneticLastRaw;

        private void BeginMagneticLasso(Core.Documents.Layer layer, SKPoint doc)
        {
            var pixels = layer.Bitmap.Pixels;
            _magneticWidth = layer.Bitmap.Width;
            _magneticHeight = layer.Bitmap.Height;
            _magneticLuma = new byte[pixels.Length];
            for (var i = 0; i < pixels.Length; i++)
            {
                var c = pixels[i];
                // Composited over white, so transparent areas read as a flat background.
                var a = c.Alpha / 255f;
                var y = 0.2126f * c.Red + 0.7152f * c.Green + 0.0722f * c.Blue;
                _magneticLuma[i] = (byte)Math.Clamp(y * a + 255 * (1 - a), 0, 255);
            }

            _magneticLastRaw = doc;
        }

        /// <summary>Adds the drag from the previous sample to <paramref name="doc"/> to
        /// the lasso path, each sample snapped to the nearest strong edge.</summary>
        private void ExtendMagneticLasso(SKPath path, SKPoint doc)
        {
            var dx = doc.X - _magneticLastRaw.X;
            var dy = doc.Y - _magneticLastRaw.Y;
            var distance = MathF.Sqrt(dx * dx + dy * dy);
            if (distance < MagneticSampleStep) return;

            for (var t = MagneticSampleStep; t <= distance; t += MagneticSampleStep)
            {
                var raw = new SKPoint(_magneticLastRaw.X + dx * t / distance, _magneticLastRaw.Y + dy * t / distance);
                path.LineTo(SnapToEdge(raw));
            }

            _magneticLastRaw = doc;
        }

        private void EndMagneticLasso() => _magneticLuma = null;

        private SKPoint SnapToEdge(SKPoint point)
        {
            if (_magneticLuma is null) return point;

            var cx = (int)point.X;
            var cy = (int)point.Y;
            var best = point;
            var bestScore = 0f;

            for (var y = cy - MagneticSnapRadius; y <= cy + MagneticSnapRadius; y++)
            {
                if (y < 1 || y >= _magneticHeight - 1) continue;
                for (var x = cx - MagneticSnapRadius; x <= cx + MagneticSnapRadius; x++)
                {
                    if (x < 1 || x >= _magneticWidth - 1) continue;

                    var ddx = x - point.X;
                    var ddy = y - point.Y;
                    var d2 = ddx * ddx + ddy * ddy;
                    if (d2 > MagneticSnapRadius * MagneticSnapRadius) continue;

                    // Edge strength, slightly favouring points near the pointer so the
                    // outline doesn't jump to a distant edge when two are equally strong.
                    var score = Sobel(x, y) * (1f - 0.5f * d2 / (MagneticSnapRadius * MagneticSnapRadius));
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = new SKPoint(x + 0.5f, y + 0.5f);
                    }
                }
            }

            // On flat areas (no real edge nearby) follow the pointer as is.
            return bestScore >= 40f ? best : point;
        }

        private float Sobel(int x, int y)
        {
            var l = _magneticLuma!;
            var w = _magneticWidth;
            int P(int px, int py) => l[py * w + px];

            var gx = -P(x - 1, y - 1) - 2 * P(x - 1, y) - P(x - 1, y + 1) + P(x + 1, y - 1) + 2 * P(x + 1, y) + P(x + 1, y + 1);
            var gy = -P(x - 1, y - 1) - 2 * P(x, y - 1) - P(x + 1, y - 1) + P(x - 1, y + 1) + 2 * P(x, y + 1) + P(x + 1, y + 1);
            return MathF.Sqrt(gx * gx + gy * gy);
        }
    }
}
