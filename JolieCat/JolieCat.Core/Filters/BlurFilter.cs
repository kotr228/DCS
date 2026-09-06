using System;
using SkiaSharp;

namespace JolieCat.Core.Filters
{
    public enum BlurType
    {
        /// <summary>Skia's own native blur (<see cref="SKImageFilter.CreateBlur"/>) -
        /// a true Gaussian, and the one to prefer for quality/performance alike.</summary>
        Gaussian,

        /// <summary>A uniform-weight box kernel via <see cref="SKImageFilter.CreateMatrixConvolution"/> -
        /// visibly different (more "blocky") than Gaussian, offered as the
        /// distinct, simpler blur the task asks for alongside it.</summary>
        Box,
    }

    /// <summary>Gaussian and Box blur for a whole bitmap.</summary>
    public static class BlurFilter
    {
        /// <summary>Radius is clamped here (not just left to the caller) since Box
        /// blur's cost is quadratic in its kernel's side length - an unbounded radius
        /// from a stray large slider value would make one preview frame arbitrarily
        /// slow rather than merely "very blurred".</summary>
        private const float MaxBoxBlurRadius = 25f;

        public static SKBitmap Apply(SKBitmap source, BlurType type, float radius)
        {
            ArgumentNullException.ThrowIfNull(source);
            radius = Math.Max(0f, radius);

            using var filter = type == BlurType.Gaussian
                ? SKImageFilter.CreateBlur(radius, radius)
                : CreateBoxBlurFilter(Math.Min(radius, MaxBoxBlurRadius));

            return ApplyImageFilter(source, filter);
        }

        private static SKImageFilter CreateBoxBlurFilter(float radius)
        {
            var side = Math.Max(1, (int)Math.Round(radius)) * 2 + 1;
            var kernel = new float[side * side];
            var weight = 1f / (side * side);
            for (var i = 0; i < kernel.Length; i++) kernel[i] = weight;

            return SKImageFilter.CreateMatrixConvolution(
                new SKSizeI(side, side), kernel, gain: 1f, bias: 0f,
                kernelOffset: new SKPointI(side / 2, side / 2),
                tileMode: SKShaderTileMode.Clamp, convolveAlpha: false);
        }

        /// <summary>Draws <paramref name="source"/> through an <see cref="SKImageFilter"/>
        /// onto a fresh, same-size bitmap - the shared apply step Sharpen also uses
        /// for its own convolution filter.</summary>
        internal static SKBitmap ApplyImageFilter(SKBitmap source, SKImageFilter filter)
        {
            var result = new SKBitmap(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(result);
            canvas.Clear(SKColors.Transparent);

            using var paint = new SKPaint { ImageFilter = filter };
            canvas.DrawBitmap(source, 0, 0, paint);

            return result;
        }
    }
}
