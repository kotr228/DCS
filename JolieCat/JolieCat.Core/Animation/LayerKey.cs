using System;
using System.Collections.Generic;
using SkiaSharp;

namespace JolieCat.Core.Animation
{
    /// <summary>How the move from one keyframe to the next goes.</summary>
    public enum KeyEasing
    {
        /// <summary>Constant speed.</summary>
        Linear,
        /// <summary>Starts and ends gently.</summary>
        EaseInOut,
        /// <summary>Starts slowly, speeds up.</summary>
        EaseIn,
        /// <summary>Starts fast, slows down at the end.</summary>
        EaseOut,
        /// <summary>No in-between: stays, then jumps at the next key.</summary>
        Hold,
        /// <summary>A speed curve of your own (a cubic Bezier from (0,0) to (1,1)).</summary>
        Custom,
    }

    /// <summary>
    /// One keyframe of a layer's animation: where it is (offset in pixels from where
    /// it was drawn), how big (percent), how turned (degrees) and how opaque (percent)
    /// at <see cref="Frame"/>. <see cref="Easing"/> is how it moves on to the next key.
    /// </summary>
    public sealed record LayerKey(double Frame, double X = 0, double Y = 0, double Scale = 100, double Rotation = 0,
        double Opacity = 100, KeyEasing Easing = KeyEasing.EaseInOut)
    {
        /// <summary>The Custom speed curve's two handles (like CSS cubic-bezier): time
        /// (0..1) and progress (may overshoot, -1..2) of each.</summary>
        public double CurveX1 { get; init; } = 0.42;
        public double CurveY1 { get; init; }
        public double CurveX2 { get; init; } = 0.58;
        public double CurveY2 { get; init; } = 1;

        /// <summary>Text layers: how much of the text shows (0..100 %) - a typewriter effect.</summary>
        public double Reveal { get; init; } = 100;

        /// <summary>Blur in pixels (0 = sharp): a focus pull, or a fast thing smearing.</summary>
        public double Blur { get; init; }

        /// <summary>Degrees the part past the track's bend point turns (an arm bending at the elbow).</summary>
        public double Bend { get; init; }

        /// <summary>Color effects: brightness -100..100 (0 = as drawn), saturation in percent
        /// (100 = as drawn, 0 = gray), hue shift in degrees.</summary>
        public double Brightness { get; init; }
        public double Saturation { get; init; } = 100;
        public double Hue { get; init; }

        /// <summary>The color effects as a 4x5 color matrix (Skia layout), or null for none.</summary>
        public float[]? ColorMatrix() => ColorEffect(Brightness, Saturation, Hue);

        /// <summary>A 4x5 color matrix: saturation and hue turned (around the gray axis),
        /// then brightness added (+100 white, -100 black). Null when it changes nothing.</summary>
        public static float[]? ColorEffect(double brightness, double saturation, double hue)
        {
            if (brightness == 0 && Math.Abs(saturation - 100) < 1e-6 && hue == 0) return null;
            const float lr = 0.2126f, lg = 0.7152f, lb = 0.0722f;
            var s = (float)Math.Max(0, saturation / 100);
            // Saturation: mix with the gray of each color.
            float[,] sat =
            {
                { lr + (1 - lr) * s, lg - lg * s, lb - lb * s },
                { lr - lr * s, lg + (1 - lg) * s, lb - lb * s },
                { lr - lr * s, lg - lg * s, lb + (1 - lb) * s },
            };
            // Hue: a turn around the gray axis (the usual luminance-preserving matrix).
            var a = hue * Math.PI / 180;
            float c = (float)Math.Cos(a), n = (float)Math.Sin(a);
            float[,] rot =
            {
                { lr + c * (1 - lr) + n * -lr, lg + c * -lg + n * -lg, lb + c * -lb + n * (1 - lb) },
                { lr + c * -lr + n * 0.143f, lg + c * (1 - lg) + n * 0.140f, lb + c * -lb + n * -0.283f },
                { lr + c * -lr + n * -(1 - lr), lg + c * -lg + n * lg, lb + c * (1 - lb) + n * lb },
            };
            var m = new float[20];
            for (var r = 0; r < 3; r++)
                for (var k = 0; k < 3; k++)
                {
                    float v = 0;
                    for (var j = 0; j < 3; j++) v += rot[r, j] * sat[j, k];
                    m[r * 5 + k] = v;
                }
            // Brightness: toward white (mix) or black (scale), so +100 is white and -100 black.
            var b = (float)Math.Clamp(brightness / 100, -1, 1);
            for (var r = 0; r < 3; r++)
            {
                if (b >= 0)
                {
                    for (var k = 0; k < 3; k++) m[r * 5 + k] *= 1 - b;
                    m[r * 5 + 4] = b;     // Skia's matrix offsets are 0..1
                }
                else
                {
                    for (var k = 0; k < 3; k++) m[r * 5 + k] *= 1 + b;
                }
            }
            m[18] = 1;                    // alpha unchanged
            return m;
        }

        /// <summary>Are the color effects the layer as drawn?</summary>
        public bool HasNoColorEffect => Brightness == 0 && Math.Abs(Saturation - 100) < 1e-6 && Hue == 0;

        /// <summary>Progress 0..1 between this key and the next at time <paramref name="t"/> (0..1).</summary>
        public double ShapeOf(double t) => Easing == KeyEasing.Custom ? Bezier(t, CurveX1, CurveY1, CurveX2, CurveY2) : Shape(t, Easing);

        /// <summary>A cubic Bezier speed curve from (0,0) to (1,1): finds where on the curve
        /// time is <paramref name="t"/>, and returns the progress there.</summary>
        public static double Bezier(double t, double x1, double y1, double x2, double y2)
        {
            x1 = Math.Clamp(x1, 0, 1);
            x2 = Math.Clamp(x2, 0, 1);
            static double Coord(double u, double a, double b) => 3 * (1 - u) * (1 - u) * u * a + 3 * (1 - u) * u * u * b + u * u * u;
            // Time grows with u (handles kept within 0..1), so bisection finds u.
            double lo = 0, hi = 1, mid = t;
            for (var i = 0; i < 40; i++)
            {
                mid = (lo + hi) / 2;
                if (Coord(mid, x1, x2) < t) lo = mid; else hi = mid;
            }
            return Coord(mid, y1, y2);
        }
        /// <summary>Progress 0..1 between two keys, shaped by <paramref name="easing"/>.</summary>
        public static double Shape(double t, KeyEasing easing) => easing switch
        {
            KeyEasing.EaseInOut => t * t * (3 - 2 * t),
            KeyEasing.EaseIn => t * t * t,
            KeyEasing.EaseOut => 1 - Math.Pow(1 - t, 3),
            KeyEasing.Hold => 0,
            _ => t,
        };

        /// <summary>The pose at <paramref name="frame"/>: the keys' values blended between
        /// the keys on either side (held before the first and after the last key).
        /// <paramref name="keys"/> must be sorted by frame.</summary>
        public static LayerKey At(IReadOnlyList<LayerKey> keys, double frame)
        {
            if (keys.Count == 0) return new LayerKey(frame);
            if (frame <= keys[0].Frame) return keys[0] with { Frame = frame };
            if (frame >= keys[^1].Frame) return keys[^1] with { Frame = frame };

            var i = 0;
            while (i + 1 < keys.Count && keys[i + 1].Frame <= frame) i++;
            var a = keys[i];
            var b = keys[i + 1];
            var t = (frame - a.Frame) / Math.Max(1e-9, b.Frame - a.Frame);
            t = a.ShapeOf(t);
            double Lerp(double p, double q) => p + (q - p) * t;
            // Built from key a, so its easing and curve handles come along.
            return a with
            {
                Frame = frame, X = Lerp(a.X, b.X), Y = Lerp(a.Y, b.Y), Scale = Lerp(a.Scale, b.Scale),
                Rotation = Lerp(a.Rotation, b.Rotation), Opacity = Lerp(a.Opacity, b.Opacity),
                Reveal = Lerp(a.Reveal, b.Reveal),
                Blur = Lerp(a.Blur, b.Blur),
                Bend = Lerp(a.Bend, b.Bend),
                Brightness = Lerp(a.Brightness, b.Brightness),
                Saturation = Lerp(a.Saturation, b.Saturation),
                Hue = Lerp(a.Hue, b.Hue),
            };
        }

        /// <summary>Is this pose the layer as drawn (nothing to change)?</summary>
        public bool IsIdentity => X == 0 && Y == 0 && Math.Abs(Scale - 100) < 1e-6 && Rotation == 0 && Math.Abs(Opacity - 100) < 1e-6 && Blur <= 0 && Bend == 0 && HasNoColorEffect;

        /// <summary>Is the matrix part (move/scale/turn) the layer as drawn?</summary>
        public bool IsStill => X == 0 && Y == 0 && Math.Abs(Scale - 100) < 1e-6 && Rotation == 0;

        /// <summary>The camera's view of a picture centred on <paramref name="center"/>: it
        /// looks at center + (X, Y), zoomed by Scale and turned by Rotation - so the picture
        /// moves the opposite way, as under a real camera.</summary>
        public SKMatrix ToCameraMatrix(SKPoint center)
        {
            var s = (float)Math.Max(0.01, Scale / 100);
            var m = SKMatrix.CreateTranslation(-(center.X + (float)X), -(center.Y + (float)Y));
            m = m.PostConcat(SKMatrix.CreateScale(s, s));
            m = m.PostConcat(SKMatrix.CreateRotationDegrees(-(float)Rotation));
            m = m.PostConcat(SKMatrix.CreateTranslation(center.X, center.Y));
            return m;
        }

        /// <summary>The drawing transform: scaled and turned around <paramref name="pivot"/>
        /// (the middle of the layer's content), then moved by X, Y.</summary>
        public SKMatrix ToMatrix(SKPoint pivot)
        {
            var s = (float)(Scale / 100);
            var m = SKMatrix.CreateTranslation(-pivot.X, -pivot.Y);
            m = m.PostConcat(SKMatrix.CreateScale(s, s));
            m = m.PostConcat(SKMatrix.CreateRotationDegrees((float)Rotation));
            m = m.PostConcat(SKMatrix.CreateTranslation(pivot.X + (float)X, pivot.Y + (float)Y));
            return m;
        }
    }
}
