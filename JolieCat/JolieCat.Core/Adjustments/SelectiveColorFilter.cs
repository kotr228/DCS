using System;
using SkiaSharp;

namespace JolieCat.Core.Adjustments
{
    /// <summary>
    /// Selective Color as a GPU/CPU color filter: each pixel belongs to the color ranges
    /// (reds, yellows, ... whites, neutrals, blacks) by how strongly it shows them, and
    /// each range adds or removes cyan / magenta / yellow / black ink. Relative changes
    /// scale with the ink already there (so pure white stays white); absolute ones don't.
    /// </summary>
    public static class SelectiveColorFilter
    {
        private const string Source = @"
uniform float4 amounts[9];
uniform float absoluteMode;

float3 shift(float3 rgb, float4 a, float w) {
    if (w <= 0.0) return float3(0);
    float3 ink = 1.0 - rgb;
    float3 change = a.xyz + a.w;
    float3 scale = absoluteMode > 0.5 ? float3(1.0) : ink;
    return -w * change * scale;
}

half4 main(half4 color) {
    float alpha = color.a;
    if (alpha <= 0.0) return color;
    float3 rgb = clamp(float3(color.rgb) / alpha, 0.0, 1.0);
    float mx = max(rgb.r, max(rgb.g, rgb.b));
    float mn = min(rgb.r, min(rgb.g, rgb.b));
    float md = rgb.r + rgb.g + rgb.b - mx - mn;

    float3 d = float3(0);
    if (rgb.r == mx) d += shift(rgb, amounts[0], mx - md);
    if (rgb.b == mn) d += shift(rgb, amounts[1], md - mn);
    if (rgb.g == mx) d += shift(rgb, amounts[2], mx - md);
    if (rgb.r == mn) d += shift(rgb, amounts[3], md - mn);
    if (rgb.b == mx) d += shift(rgb, amounts[4], mx - md);
    if (rgb.g == mn) d += shift(rgb, amounts[5], md - mn);
    d += shift(rgb, amounts[6], clamp((mn - 0.5) * 2.0, 0.0, 1.0));
    d += shift(rgb, amounts[7], clamp(1.0 - (abs(mx - 0.5) + abs(mn - 0.5)), 0.0, 1.0));
    d += shift(rgb, amounts[8], clamp((0.5 - mx) * 2.0, 0.0, 1.0));

    rgb = clamp(rgb + d, 0.0, 1.0);
    return half4(half3(rgb * alpha), color.a);
}";

        private static SKRuntimeEffect? _effect;
        private static readonly object Gate = new();

        private static SKRuntimeEffect Effect
        {
            get
            {
                lock (Gate)
                {
                    if (_effect is null)
                    {
                        _effect = SKRuntimeEffect.CreateColorFilter(Source, out var errors);
                        if (_effect is null) throw new InvalidOperationException("Selective Color shader: " + errors);
                    }
                    return _effect;
                }
            }
        }

        /// <param name="amounts">Cyan, magenta, yellow, black for each range, -1..1.</param>
        public static SKColorFilter Create(float[] amounts, bool absolute)
        {
            var effect = Effect;
            var uniforms = new SKRuntimeEffectUniforms(effect)
            {
                ["amounts"] = amounts,
                ["absoluteMode"] = absolute ? 1f : 0f,
            };
            return effect.ToColorFilter(uniforms);
        }
    }
}
