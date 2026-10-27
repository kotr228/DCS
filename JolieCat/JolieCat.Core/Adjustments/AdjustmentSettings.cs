using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SkiaSharp;

namespace JolieCat.Core.Adjustments
{
    public enum AdjustmentKind
    {
        BrightnessContrast,
        Levels,
        Curves,
        HueSaturation,
        Invert,
        Posterize,
        Threshold,
        Exposure,
        BlackWhite,
        ColorBalance,
        PhotoFilter,
        GradientMap,
    }

    /// <summary>
    /// The settings of a non-destructive adjustment layer: which adjustment, and its
    /// numbers. Immutable - editing an adjustment layer swaps in a new instance, which
    /// keeps undo/redo trivial.
    /// </summary>
    /// <remarks>
    /// <see cref="Values"/> layout per kind:
    /// Brightness/Contrast: [brightness -100..100, contrast -100..100];
    /// Levels: [input black, input white, gamma, output black, output white];
    /// Curves: control points flattened as x0, y0, x1, y1, ... (0-255);
    /// Hue/Saturation: [hue -180..180, saturation -100..100, lightness -100..100];
    /// Posterize: [levels 2..64]; Threshold: [level 0..255]; Invert: none.
    /// </remarks>
    public sealed class AdjustmentSettings
    {
        public AdjustmentKind Kind { get; }

        public IReadOnlyList<double> Values { get; }

        public AdjustmentSettings(AdjustmentKind kind, IEnumerable<double> values)
        {
            Kind = kind;
            Values = values.ToArray();
        }

        public static AdjustmentSettings Default(AdjustmentKind kind) => kind switch
        {
            AdjustmentKind.BrightnessContrast => new(kind, new[] { 0.0, 0.0 }),
            AdjustmentKind.Levels => new(kind, new[] { 0.0, 255.0, 1.0, 0.0, 255.0 }),
            AdjustmentKind.Curves => new(kind, new[] { 0.0, 0.0, 255.0, 255.0 }),
            AdjustmentKind.HueSaturation => new(kind, new[] { 0.0, 0.0, 0.0 }),
            AdjustmentKind.Posterize => new(kind, new[] { 6.0 }),
            AdjustmentKind.Threshold => new(kind, new[] { 128.0 }),
            AdjustmentKind.Exposure => new(kind, new[] { 0.0, 0.0, 1.0 }),
            AdjustmentKind.BlackWhite => new(kind, new[] { 40.0, 40.0, 20.0 }),
            AdjustmentKind.ColorBalance => new(kind, new double[9]),
            AdjustmentKind.PhotoFilter => new(kind, new[] { 25.0, 1.0, PackColor(new SKColor(236, 138, 0)) }),
            AdjustmentKind.GradientMap => new(kind, new[] { PackColor(SKColors.Black), PackColor(SKColors.White) }),
            _ => new(kind, Array.Empty<double>()),
        };

        public string DisplayName => Kind switch
        {
            AdjustmentKind.BrightnessContrast => "Brightness/Contrast",
            AdjustmentKind.HueSaturation => "Hue/Saturation",
            AdjustmentKind.BlackWhite => "Black & White",
            AdjustmentKind.ColorBalance => "Color Balance",
            AdjustmentKind.PhotoFilter => "Photo Filter",
            AdjustmentKind.GradientMap => "Gradient Map",
            _ => Kind.ToString(),
        };

        private double At(int index, double fallback) => index < Values.Count ? Values[index] : fallback;

        /// <summary>The color filter that applies this adjustment to whatever is
        /// composited beneath the layer.</summary>
        public SKColorFilter CreateColorFilter()
        {
            switch (Kind)
            {
                case AdjustmentKind.BrightnessContrast:
                    return Table(BrightnessContrastAdjustment.BuildLut(At(0, 0), At(1, 0)));

                case AdjustmentKind.Levels:
                    return Table(LevelsAdjustment.BuildLut((int)At(0, 0), (int)At(1, 255), At(2, 1), (int)At(3, 0), (int)At(4, 255)));

                case AdjustmentKind.Curves:
                {
                    var points = new List<(double X, double Y)>();
                    for (var i = 0; i + 1 < Values.Count; i += 2) points.Add((Values[i], Values[i + 1]));
                    return Table(CurvesAdjustment.BuildLut(points));
                }

                case AdjustmentKind.HueSaturation:
                    return HueSaturation(At(0, 0), At(1, 0), At(2, 0));

                case AdjustmentKind.Invert:
                {
                    var table = new byte[256];
                    for (var i = 0; i < 256; i++) table[i] = (byte)(255 - i);
                    return Rgb(table);
                }

                case AdjustmentKind.Posterize:
                {
                    var levels = Math.Clamp((int)Math.Round(At(0, 6)), 2, 64);
                    var table = new byte[256];
                    for (var i = 0; i < 256; i++)
                        table[i] = (byte)Math.Round(Math.Round(i / 255.0 * (levels - 1)) * 255.0 / (levels - 1));
                    return Rgb(table);
                }

                case AdjustmentKind.Threshold:
                {
                    var level = Math.Clamp((int)Math.Round(At(0, 128)), 0, 255);
                    var table = new byte[256];
                    for (var i = 0; i < 256; i++) table[i] = i >= level ? (byte)255 : (byte)0;
                    using var luma = SKColorFilter.CreateColorMatrix(LumaMatrix());
                    using var step = Rgb(table);
                    return SKColorFilter.CreateCompose(step, luma);
                }

                case AdjustmentKind.Exposure:
                {
                    // Photoshop-style: scale by 2^exposure, add offset, then gamma.
                    var gain = Math.Pow(2, At(0, 0));
                    var offset = At(1, 0);
                    var gamma = Math.Max(0.01, At(2, 1));
                    var table = new byte[256];
                    for (var i = 0; i < 256; i++)
                    {
                        var v = Math.Clamp(i / 255.0 * gain + offset, 0, 1);
                        table[i] = (byte)Math.Round(Math.Pow(v, 1 / gamma) * 255);
                    }
                    return Rgb(table);
                }

                case AdjustmentKind.BlackWhite:
                {
                    // Gray from the channels in the given proportions (percent, may go
                    // negative or above 100 for stronger effects).
                    float r = (float)(At(0, 40) / 100), g = (float)(At(1, 40) / 100), b = (float)(At(2, 20) / 100);
                    return SKColorFilter.CreateColorMatrix(new[]
                    {
                        r, g, b, 0, 0,
                        r, g, b, 0, 0,
                        r, g, b, 0, 0,
                        0, 0, 0, 1, 0,
                    });
                }

                case AdjustmentKind.ColorBalance:
                    return ColorBalance();

                case AdjustmentKind.PhotoFilter:
                {
                    // Like shooting through a colored lens: each channel scaled toward the
                    // filter color by Density; Preserve Luminosity keeps the brightness.
                    var density = Math.Clamp(At(0, 25), 0, 100) / 100;
                    var color = UnpackColor(At(2, 0));
                    var fr = 1 - density + density * color.Red / 255.0;
                    var fg = 1 - density + density * color.Green / 255.0;
                    var fb = 1 - density + density * color.Blue / 255.0;
                    if (At(1, 1) >= 0.5)
                    {
                        var luma = 0.2126 * fr + 0.7152 * fg + 0.0722 * fb;
                        if (luma > 1e-6) { fr /= luma; fg /= luma; fb /= luma; }
                    }
                    return SKColorFilter.CreateColorMatrix(new[]
                    {
                        (float)fr, 0, 0, 0, 0,
                        0, (float)fg, 0, 0, 0,
                        0, 0, (float)fb, 0, 0,
                        0, 0, 0, 1, 0,
                    });
                }

                case AdjustmentKind.GradientMap:
                {
                    // Brightness picks a color between the shadow and highlight colors.
                    var dark = UnpackColor(At(0, PackColor(SKColors.Black)));
                    var light = UnpackColor(At(1, PackColor(SKColors.White)));
                    byte[] red = new byte[256], green = new byte[256], blue = new byte[256];
                    for (var i = 0; i < 256; i++)
                    {
                        var t = i / 255.0;
                        red[i] = (byte)Math.Round(dark.Red + (light.Red - dark.Red) * t);
                        green[i] = (byte)Math.Round(dark.Green + (light.Green - dark.Green) * t);
                        blue[i] = (byte)Math.Round(dark.Blue + (light.Blue - dark.Blue) * t);
                    }
                    using var luma = SKColorFilter.CreateColorMatrix(LumaMatrix());
                    using var map = SKColorFilter.CreateTable(Identity(), red, green, blue);
                    return SKColorFilter.CreateCompose(map, luma);
                }

                default:
                    return Rgb(Identity());
            }
        }

        /// <summary>Color Balance: values are [shadows cyan-red, magenta-green,
        /// yellow-blue, midtones ..., highlights ...], each -100..100. Each tone range
        /// shifts the channels mostly in its part of the brightness scale.</summary>
        private SKColorFilter ColorBalance()
        {
            byte[] red = new byte[256], green = new byte[256], blue = new byte[256];
            for (var i = 0; i < 256; i++)
            {
                var x = i / 255.0;
                var shadows = Math.Pow(1 - x, 2);
                var midtones = 1 - Math.Pow(2 * x - 1, 2);
                var highlights = x * x;
                double Shift(int axis) =>
                    (At(axis, 0) * shadows + At(3 + axis, 0) * midtones + At(6 + axis, 0) * highlights) / 100 * 0.4;

                red[i] = (byte)Math.Clamp(Math.Round((x + Shift(0)) * 255), 0, 255);
                green[i] = (byte)Math.Clamp(Math.Round((x + Shift(1)) * 255), 0, 255);
                blue[i] = (byte)Math.Clamp(Math.Round((x + Shift(2)) * 255), 0, 255);
            }
            return SKColorFilter.CreateTable(Identity(), red, green, blue);
        }

        /// <summary>Colors are stored among the numeric values as 0xRRGGBB.</summary>
        public static double PackColor(SKColor color) => (color.Red << 16) | (color.Green << 8) | color.Blue;

        public static SKColor UnpackColor(double packed)
        {
            var v = (int)Math.Clamp(packed, 0, 0xFFFFFF);
            return new SKColor((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        }

        /// <summary>Hue rotation and saturation as luminance-preserving color matrices,
        /// lightness as a table (toward white for positive, black for negative values).
        /// A close, fast match for the destructive Hue/Saturation command.</summary>
        private static SKColorFilter HueSaturation(double hueDegrees, double saturationPercent, double lightnessPercent)
        {
            var radians = hueDegrees * Math.PI / 180.0;
            float cos = (float)Math.Cos(radians), sin = (float)Math.Sin(radians);
            var hue = new[]
            {
                0.213f + cos * 0.787f - sin * 0.213f, 0.715f - cos * 0.715f - sin * 0.715f, 0.072f - cos * 0.072f + sin * 0.928f, 0, 0,
                0.213f - cos * 0.213f + sin * 0.143f, 0.715f + cos * 0.285f + sin * 0.140f, 0.072f - cos * 0.072f - sin * 0.283f, 0, 0,
                0.213f - cos * 0.213f - sin * 0.787f, 0.715f - cos * 0.715f + sin * 0.715f, 0.072f + cos * 0.928f + sin * 0.072f, 0, 0,
                0, 0, 0, 1, 0,
            };

            var s = (float)(1.0 + Math.Clamp(saturationPercent, -100, 100) / 100.0);
            var saturation = new[]
            {
                0.213f + 0.787f * s, 0.715f - 0.715f * s, 0.072f - 0.072f * s, 0, 0,
                0.213f - 0.213f * s, 0.715f + 0.285f * s, 0.072f - 0.072f * s, 0, 0,
                0.213f - 0.213f * s, 0.715f - 0.715f * s, 0.072f + 0.928f * s, 0, 0,
                0, 0, 0, 1, 0,
            };

            var l = Math.Clamp(lightnessPercent, -100, 100) / 100.0;
            var table = new byte[256];
            for (var i = 0; i < 256; i++)
                table[i] = (byte)Math.Clamp(Math.Round(l >= 0 ? i + (255 - i) * l : i * (1 + l)), 0, 255);

            using var hueFilter = SKColorFilter.CreateColorMatrix(hue);
            using var saturationFilter = SKColorFilter.CreateColorMatrix(saturation);
            using var lightness = Rgb(table);
            using var colorPart = SKColorFilter.CreateCompose(saturationFilter, hueFilter);
            return SKColorFilter.CreateCompose(lightness, colorPart);
        }

        private static float[] LumaMatrix() => new[]
        {
            0.2126f, 0.7152f, 0.0722f, 0, 0,
            0.2126f, 0.7152f, 0.0722f, 0, 0,
            0.2126f, 0.7152f, 0.0722f, 0, 0,
            0, 0, 0, 1, 0,
        };

        private static SKColorFilter Table(AdjustmentLut lut) => SKColorFilter.CreateTable(Identity(), lut.Red, lut.Green, lut.Blue);

        private static SKColorFilter Rgb(byte[] table) => SKColorFilter.CreateTable(Identity(), table, table, table);

        private static byte[] Identity()
        {
            var table = new byte[256];
            for (var i = 0; i < 256; i++) table[i] = (byte)i;
            return table;
        }

        /// <summary>Compact text form for project files: "Kind:v1,v2,...".</summary>
        public string Serialize() =>
            Kind + ":" + string.Join(",", Values.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));

        public static AdjustmentSettings? Deserialize(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            var parts = text.Split(':', 2);
            if (!Enum.TryParse<AdjustmentKind>(parts[0], out var kind)) return null;

            var values = parts.Length > 1 && parts[1].Length > 0
                ? parts[1].Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture))
                : Enumerable.Empty<double>();
            return new AdjustmentSettings(kind, values);
        }
    }
}
