using System;
using SkiaSharp;

namespace JolieCat.Core.Filters
{
    /// <summary>A classic 3x3 unsharp-mask-style convolution kernel, scaled smoothly
    /// by <c>amount</c> rather than a fixed single strength.</summary>
    public static class SharpenFilter
    {
        public static SKBitmap Apply(SKBitmap source, float amount)
        {
            ArgumentNullException.ThrowIfNull(source);

            // 0 = identity (no-op) kernel, 1 = the standard "5 center, -1 neighbors"
            // sharpen; scales past 1 for a stronger effect. The kernel's own values
            // always sum to exactly 1 regardless of amount, so overall brightness
            // never drifts - only local contrast at edges changes.
            amount = Math.Clamp(amount, 0f, 3f);

            var center = 1f + 4f * amount;
            var edge = -amount;
            var kernel = new[] { 0f, edge, 0f, edge, center, edge, 0f, edge, 0f };

            using var filter = SKImageFilter.CreateMatrixConvolution(
                new SKSizeI(3, 3), kernel, gain: 1f, bias: 0f, kernelOffset: new SKPointI(1, 1),
                tileMode: SKShaderTileMode.Clamp, convolveAlpha: false);

            return BlurFilter.ApplyImageFilter(source, filter);
        }
    }
}
