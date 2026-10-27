using System;
using SkiaSharp;

namespace JolieCat.Core.Filters
{
    /// <summary>
    /// Per-pixel and convolution filters beyond blur/sharpen/noise: Invert, Desaturate,
    /// Posterize, Threshold, Pixelate, Emboss, Find Edges and Vignette. Every one
    /// returns a new bitmap and leaves the source untouched, like the other filters,
    /// so they plug straight into the canvas's live-preview/commit flow.
    /// </summary>
    public static class StylizeFilters
    {
        /// <summary>Inverts RGB, keeping alpha.</summary>
        public static SKBitmap Invert(SKBitmap source) =>
            MapPixels(source, c => new SKColor((byte)(255 - c.Red), (byte)(255 - c.Green), (byte)(255 - c.Blue), c.Alpha));

        /// <summary>Converts to grayscale using perceptual (Rec. 709) luminance.</summary>
        public static SKBitmap Desaturate(SKBitmap source) => MapPixels(source, c =>
        {
            var y = Luma(c);
            return new SKColor(y, y, y, c.Alpha);
        });

        /// <summary>Reduces each channel to <paramref name="levels"/> evenly spaced values (2-64).</summary>
        public static SKBitmap Posterize(SKBitmap source, int levels)
        {
            levels = Math.Clamp(levels, 2, 64);
            var lut = new byte[256];
            for (var i = 0; i < 256; i++)
            {
                var step = Math.Round(i / 255.0 * (levels - 1));
                lut[i] = (byte)Math.Round(step * 255.0 / (levels - 1));
            }

            return MapPixels(source, c => new SKColor(lut[c.Red], lut[c.Green], lut[c.Blue], c.Alpha));
        }

        /// <summary>Pure black/white: luminance at or above <paramref name="level"/> becomes white.</summary>
        public static SKBitmap Threshold(SKBitmap source, int level)
        {
            level = Math.Clamp(level, 0, 255);
            return MapPixels(source, c =>
            {
                var v = Luma(c) >= level ? (byte)255 : (byte)0;
                return new SKColor(v, v, v, c.Alpha);
            });
        }

        /// <summary>Averages square blocks of <paramref name="cellSize"/> pixels (alpha-weighted,
        /// so transparent pixels don't darken edge blocks).</summary>
        public static SKBitmap Pixelate(SKBitmap source, int cellSize)
        {
            ArgumentNullException.ThrowIfNull(source);
            cellSize = Math.Clamp(cellSize, 1, 512);

            var width = source.Width;
            var height = source.Height;
            var src = source.Pixels;
            var dst = new SKColor[src.Length];

            for (var by = 0; by < height; by += cellSize)
            {
                var bh = Math.Min(cellSize, height - by);
                for (var bx = 0; bx < width; bx += cellSize)
                {
                    var bw = Math.Min(cellSize, width - bx);
                    long r = 0, g = 0, b = 0, a = 0;

                    for (var y = 0; y < bh; y++)
                    {
                        var row = (by + y) * width + bx;
                        for (var x = 0; x < bw; x++)
                        {
                            var c = src[row + x];
                            r += c.Red * c.Alpha;
                            g += c.Green * c.Alpha;
                            b += c.Blue * c.Alpha;
                            a += c.Alpha;
                        }
                    }

                    var count = bw * bh;
                    var avg = a == 0
                        ? SKColors.Transparent
                        : new SKColor((byte)(r / a), (byte)(g / a), (byte)(b / a), (byte)(a / count));

                    for (var y = 0; y < bh; y++)
                        Array.Fill(dst, avg, (by + y) * width + bx, bw);
                }
            }

            return FromPixels(source, dst);
        }

        /// <summary>Raised-relief look: a directional edge kernel over mid-gray.</summary>
        public static SKBitmap Emboss(SKBitmap source, float strength)
        {
            strength = Math.Clamp(strength, 0.1f, 5f);
            var kernel = new[]
            {
                -strength, -strength, 0f,
                -strength, 0f, strength, // sums to 0, so flat areas land exactly on the mid-gray bias
                0f, strength, strength,
            };

            using var gray = Desaturate(source);
            return Convolve(gray, kernel, bias: 0.5f);
        }

        /// <summary>Highlights edges (Laplacian), leaving flat areas black.</summary>
        public static SKBitmap FindEdges(SKBitmap source)
        {
            var kernel = new[] { -1f, -1f, -1f, -1f, 8f, -1f, -1f, -1f, -1f };
            return Convolve(source, kernel, bias: 0f);
        }

        /// <summary>Darkens toward the corners. <paramref name="amount"/> 0-100.</summary>
        public static SKBitmap Vignette(SKBitmap source, float amount)
        {
            ArgumentNullException.ThrowIfNull(source);
            amount = Math.Clamp(amount, 0f, 100f) / 100f;

            var result = new SKBitmap(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(result);
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(source, 0, 0);

            var center = new SKPoint(source.Width / 2f, source.Height / 2f);
            var radius = MathF.Sqrt(center.X * center.X + center.Y * center.Y);
            using var shader = SKShader.CreateRadialGradient(center, radius,
                new[] { SKColors.Black.WithAlpha(0), SKColors.Black.WithAlpha(0), SKColors.Black.WithAlpha((byte)(255 * amount)) },
                new[] { 0f, 0.45f, 1f }, SKShaderTileMode.Clamp);

            // SrcATop darkens only where the layer already has pixels.
            using var paint = new SKPaint { Shader = shader, BlendMode = SKBlendMode.SrcATop };
            canvas.DrawRect(0, 0, source.Width, source.Height, paint);
            return result;
        }

        private static byte Luma(SKColor c) =>
            (byte)Math.Clamp(0.2126 * c.Red + 0.7152 * c.Green + 0.0722 * c.Blue + 0.5, 0, 255);

        /// <summary>3×3 convolution on RGB (alpha kept), edges clamped. <paramref name="bias"/>
        /// is a 0-1 fraction of full brightness added after the kernel.</summary>
        private static SKBitmap Convolve(SKBitmap source, float[] kernel, float bias)
        {
            ArgumentNullException.ThrowIfNull(source);
            var width = source.Width;
            var height = source.Height;
            var src = source.Pixels;
            var dst = new SKColor[src.Length];
            var offset = bias * 255f;

            System.Threading.Tasks.Parallel.For(0, height, y =>
            {
                for (var x = 0; x < width; x++)
                {
                    float r = 0, g = 0, b = 0;
                    var k = 0;
                    for (var ky = -1; ky <= 1; ky++)
                    {
                        var sy = Math.Clamp(y + ky, 0, height - 1) * width;
                        for (var kx = -1; kx <= 1; kx++, k++)
                        {
                            var c = src[sy + Math.Clamp(x + kx, 0, width - 1)];
                            var weight = kernel[k];
                            r += c.Red * weight;
                            g += c.Green * weight;
                            b += c.Blue * weight;
                        }
                    }

                    var i = y * width + x;
                    dst[i] = new SKColor(Clamp(r + offset), Clamp(g + offset), Clamp(b + offset), src[i].Alpha);
                }
            });

            return FromPixels(source, dst);
        }

        private static byte Clamp(float v) => (byte)Math.Clamp((int)MathF.Round(v), 0, 255);

        private static SKBitmap MapPixels(SKBitmap source, Func<SKColor, SKColor> map) =>
            EnhanceFilters.MapPixels(source, map);

        private static SKBitmap FromPixels(SKBitmap source, SKColor[] pixels) =>
            EnhanceFilters.FromPixels(source, pixels);
    }
}
