using System;
using System.Collections.Generic;
using JolieCat.Shared.Enums;
using SkiaSharp;

namespace JolieCat.Core.Rendering
{
    /// <summary>
    /// Photoshop's blend modes that Skia doesn't have built in (Linear Burn, Vivid Light,
    /// Hard Mix, Divide...), as Skia runtime blenders: small shader programs that mix the
    /// layer's color (src) into what's below (dst) the way Photoshop's formulas do, with
    /// see-through parts handled like every other mode. Dissolve depends on the pixel's
    /// place, which a blender doesn't know, so it's done by <see cref="Dissolve"/> instead.
    /// </summary>
    public static class BlendModes
    {
        private static readonly Dictionary<BlendMode, SKBlender?> Blenders = new();
        private static readonly object Gate = new();

        /// <summary>True for the modes drawn with a custom blender.</summary>
        public static bool IsCustom(BlendMode mode) => FormulaOf(mode) is not null;

        /// <summary>Gives <paramref name="paint"/> the custom blender for <paramref name="mode"/>
        /// (built-in modes are left to <see cref="SKPaint.BlendMode"/>, already set).</summary>
        public static void Apply(SKPaint paint, BlendMode mode)
        {
            if (BlenderFor(mode) is { } blender) paint.Blender = blender;
        }

        public static SKBlender? BlenderFor(BlendMode mode)
        {
            if (FormulaOf(mode) is not { } formula) return null;
            lock (Gate)
            {
                if (Blenders.TryGetValue(mode, out var cached)) return cached;
                var source = Prelude + "half3 blend(half3 s, half3 d) { " + formula + " }\n" + Main;
                using var effect = SKRuntimeEffect.CreateBlender(source, out var errors);
                var blender = effect?.ToBlender();
                if (blender is null) System.Diagnostics.Debug.WriteLine($"Blend mode {mode}: {errors}");
                Blenders[mode] = blender;
                return blender;
            }
        }

        private const string Prelude = @"
half lum(half3 c) { return dot(c, half3(0.3, 0.59, 0.11)); }
half burn1(half d, half s) { return s <= 0.0 ? 0.0 : 1.0 - min(1.0, (1.0 - d) / s); }
half dodge1(half d, half s) { return s >= 1.0 ? 1.0 : min(1.0, d / (1.0 - s)); }
half vivid1(half s, half d) { return s < 0.5 ? burn1(d, 2.0 * s) : dodge1(d, 2.0 * (s - 0.5)); }
half pin1(half s, half d) { return s < 0.5 ? min(d, 2.0 * s) : max(d, 2.0 * s - 1.0); }
half divide1(half s, half d) { return s <= 0.0 ? (d > 0.0 ? 1.0 : 0.0) : min(1.0, d / s); }
";

        // The separable blend formula: where both are there, the mix; where only one is,
        // that one (premultiplied colors in and out).
        private const string Main = @"
half4 main(half4 src, half4 dst) {
    half3 s = src.rgb / max(src.a, 0.0001);
    half3 d = dst.rgb / max(dst.a, 0.0001);
    half3 b = clamp(blend(s, d), 0.0, 1.0);
    half3 rgb = src.rgb * (1.0 - dst.a) + dst.rgb * (1.0 - src.a) + src.a * dst.a * b;
    return half4(rgb, src.a + dst.a * (1.0 - src.a));
}
";

        /// <summary>The formula for a custom mode (s = the layer's color, d = below), or null.</summary>
        private static string? FormulaOf(BlendMode mode) => mode switch
        {
            BlendMode.LinearBurn => "return s + d - 1.0;",
            BlendMode.LinearDodge => "return s + d;",
            BlendMode.Subtract => "return d - s;",
            BlendMode.Divide => "return half3(divide1(s.r, d.r), divide1(s.g, d.g), divide1(s.b, d.b));",
            BlendMode.VividLight => "return half3(vivid1(s.r, d.r), vivid1(s.g, d.g), vivid1(s.b, d.b));",
            BlendMode.LinearLight => "return d + 2.0 * s - 1.0;",
            BlendMode.PinLight => "return half3(pin1(s.r, d.r), pin1(s.g, d.g), pin1(s.b, d.b));",
            BlendMode.HardMix => "return step(1.0, s + d);",
            BlendMode.DarkerColor => "return lum(s) < lum(d) ? s : d;",
            BlendMode.LighterColor => "return lum(s) > lum(d) ? s : d;",
            _ => null,
        };

        /// <summary>
        /// Dissolve: each pixel either fully shows the layer or not at all, at random, more
        /// often where it's more opaque (<paramref name="opacity"/> counts too). A copy of
        /// <paramref name="source"/> to draw normally; the pattern stays put between frames.
        /// </summary>
        public static SKBitmap Dissolve(SKBitmap source, double opacity)
        {
            int w = source.Width, h = source.Height;
            var src = source.Pixels;
            var dst = new SKColor[src.Length];
            var k = Math.Clamp(opacity, 0, 1);
            System.Threading.Tasks.Parallel.For(0, h, y =>
            {
                for (var x = 0; x < w; x++)
                {
                    var c = src[y * w + x];
                    unchecked
                    {
                        var hsh = (uint)(x * 73856093 ^ y * 19349663);
                        hsh = (hsh ^ (hsh >> 13)) * 1274126177;
                        var noise = (hsh & 0xFFFF) / 65536.0;
                        dst[y * w + x] = noise < c.Alpha / 255.0 * k ? c.WithAlpha(255) : SKColors.Transparent;
                    }
                }
            });
            var result = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
            result.Pixels = dst;
            return result;
        }
    }
}
