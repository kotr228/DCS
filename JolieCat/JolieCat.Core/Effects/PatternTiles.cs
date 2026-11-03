using System;
using System.Collections.Generic;
using SkiaSharp;

namespace JolieCat.Core.Effects
{
    /// <summary>
    /// The built-in patterns for Pattern Overlay: small tiles drawn in one color on
    /// transparency (so the layer's own color shows between the marks), repeated
    /// across the layer.
    /// </summary>
    public static class PatternTiles
    {
        public const int TileSize = 16;

        public static IReadOnlyList<string> BuiltInNames { get; } = new[] { "Checker", "Stripes", "Diagonal", "Dots", "Grid", "Noise" };

        private static readonly Dictionary<string, SKBitmap> Custom = new();
        private static readonly object Gate = new();

        /// <summary>The built-in patterns, then the user's own (Edit > Define Pattern).</summary>
        public static IReadOnlyList<string> Names
        {
            get
            {
                lock (Gate)
                {
                    var names = new List<string>(BuiltInNames);
                    names.AddRange(Custom.Keys);
                    return names;
                }
            }
        }

        /// <summary>Adds (or replaces) a pattern made from an image. The image is used as
        /// is - in its own colors - and repeated across the layer.</summary>
        public static void Register(string name, SKBitmap image)
        {
            ArgumentNullException.ThrowIfNull(image);
            lock (Gate)
            {
                if (Custom.TryGetValue(name, out var old)) old.Dispose();
                Custom[name] = image.Copy(SKColorType.Rgba8888);
            }
        }

        public static bool Unregister(string name)
        {
            lock (Gate)
            {
                if (!Custom.Remove(name, out var old)) return false;
                old.Dispose();
                return true;
            }
        }

        public static bool IsCustom(string? name)
        {
            lock (Gate) return name is not null && Custom.ContainsKey(name);
        }

        /// <summary>A <see cref="TileSize"/>-pixel tile of <paramref name="name"/>
        /// (unknown names give Checker).</summary>
        public static SKBitmap Create(string? name, SKColor color)
        {
            lock (Gate)
            {
                // A user pattern keeps its own colors (the color setting doesn't apply).
                if (name is not null && Custom.TryGetValue(name, out var image)) return image.Copy();
            }

            var tile = new SKBitmap(TileSize, TileSize, SKColorType.Rgba8888, SKAlphaType.Premul);
            tile.Erase(SKColors.Transparent);
            using var canvas = new SKCanvas(tile);
            using var paint = new SKPaint { Color = color, IsAntialias = true };
            const float s = TileSize;

            switch (name)
            {
                case "Stripes":
                    canvas.DrawRect(0, 0, s, s / 2, paint);
                    break;

                case "Diagonal":
                    // Two triangles per tile make continuous 45° bands when repeated.
                    using (var path = new SKPath())
                    {
                        path.MoveTo(0, 0); path.LineTo(s / 2, 0); path.LineTo(0, s / 2); path.Close();
                        path.MoveTo(s, 0); path.LineTo(s, s / 2); path.LineTo(s / 2, s); path.LineTo(0, s); path.Close();
                        canvas.DrawPath(path, paint);
                    }
                    break;

                case "Dots":
                    canvas.DrawCircle(s / 2, s / 2, s * 0.22f, paint);
                    break;

                case "Grid":
                    paint.IsAntialias = false;
                    canvas.DrawRect(0, 0, s, 1, paint);
                    canvas.DrawRect(0, 0, 1, s, paint);
                    break;

                case "Noise":
                {
                    // Fixed seed: the same "noise" every time the layer is drawn.
                    var random = new Random(1234);
                    for (var y = 0; y < TileSize; y++)
                        for (var x = 0; x < TileSize; x++)
                            tile.SetPixel(x, y, color.WithAlpha((byte)(random.Next(256) * color.Alpha / 255)));
                    break;
                }

                default: // Checker
                    canvas.DrawRect(0, 0, s / 2, s / 2, paint);
                    canvas.DrawRect(s / 2, s / 2, s / 2, s / 2, paint);
                    break;
            }

            return tile;
        }
    }
}
