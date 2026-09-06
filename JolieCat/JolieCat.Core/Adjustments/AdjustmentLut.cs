using System;
using SkiaSharp;

namespace JolieCat.Core.Adjustments
{
    /// <summary>
    /// A 256-entry lookup table per RGB channel, and how to apply it to a bitmap -
    /// the shared mechanism behind Levels, Curves, and Brightness/Contrast (each just
    /// builds a different <see cref="AdjustmentLut"/>; applying it is identical).
    /// </summary>
    /// <remarks>
    /// Always applies via <see cref="SKColorFilter.CreateTable(byte[], byte[], byte[], byte[])"/>'s
    /// four-array overload, with an identity alpha table - never the single-array
    /// convenience overload. Verified directly: that overload applies its one table to
    /// alpha as well as color, so an adjustment that darkens shadows (mapping low
    /// input values toward 0) would also darken - and at the extreme, fully erase -
    /// low-alpha pixels, silently corrupting transparency that was never supposed to
    /// be part of the adjustment at all.
    /// </remarks>
    public sealed class AdjustmentLut
    {
        private static readonly byte[] IdentityTable = BuildIdentity();

        public byte[] Red { get; }

        public byte[] Green { get; }

        public byte[] Blue { get; }

        public AdjustmentLut(byte[] red, byte[] green, byte[] blue)
        {
            if (red.Length != 256 || green.Length != 256 || blue.Length != 256)
                throw new ArgumentException("Each channel's LUT must have exactly 256 entries.");

            Red = red;
            Green = green;
            Blue = blue;
        }

        /// <summary>The same 256-entry table applied to all three color channels -
        /// the common case for Levels/Curves/Brightness-Contrast, which (in this
        /// round's scope) adjust luminance/RGB uniformly rather than per-channel.</summary>
        public static AdjustmentLut Uniform(byte[] table) => new(table, table, (byte[])table.Clone());

        /// <summary>The identity LUT (every value maps to itself) - a no-op
        /// adjustment, useful as a starting point or a "reset" state.</summary>
        public static AdjustmentLut Identity() => Uniform(BuildIdentity());

        private static byte[] BuildIdentity()
        {
            var table = new byte[256];
            for (var i = 0; i < 256; i++) table[i] = (byte)i;
            return table;
        }

        /// <summary>Renders <paramref name="source"/> through this LUT into a new,
        /// same-size bitmap - alpha is always passed through unchanged (see this
        /// class's own remarks for why that has to be a separate, identity table
        /// rather than omitted).</summary>
        public SKBitmap Apply(SKBitmap source)
        {
            ArgumentNullException.ThrowIfNull(source);

            var result = new SKBitmap(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(result);
            canvas.Clear(SKColors.Transparent);

            using var paint = new SKPaint { ColorFilter = SKColorFilter.CreateTable(IdentityTable, Red, Green, Blue) };
            canvas.DrawBitmap(source, 0, 0, paint);

            return result;
        }
    }
}
