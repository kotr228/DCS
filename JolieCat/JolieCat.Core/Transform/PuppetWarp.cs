using System;
using System.Collections.Generic;
using SkiaSharp;

namespace JolieCat.Core.Transform
{
    /// <summary>
    /// Puppet Warp: pins stuck into the image; dragging a pin bends the image around
    /// the others as if it were made of rubber. Each mesh point follows the pins by
    /// "as-rigid-as-possible" moving least squares - near a pin it moves with that
    /// pin, between pins it rotates and shifts smoothly but doesn't stretch or shear.
    /// </summary>
    public sealed class PuppetWarp
    {
        public sealed class Pin
        {
            public Pin(SKPoint original)
            {
                Original = original;
                Current = original;
            }

            /// <summary>Where the pin was placed on the unwarped image.</summary>
            public SKPoint Original { get; }

            /// <summary>Where it has been dragged to.</summary>
            public SKPoint Current { get; set; }
        }

        public PuppetWarp(int width, int height, float cellSize = 16)
        {
            var columns = Math.Clamp((int)MathF.Ceiling(width / cellSize) + 1, 2, 120);
            var rows = Math.Clamp((int)MathF.Ceiling(height / cellSize) + 1, 2, 120);
            Mesh = new MeshWarp(rows, columns, width, height);
        }

        public MeshWarp Mesh { get; }

        public List<Pin> Pins { get; } = new();

        /// <summary>1..3: how far each pin's pull reaches (higher = stiffer, more local).</summary>
        public float Rigidity { get; set; } = 1;

        /// <summary>Moves every mesh point to follow the pins.</summary>
        public void Update()
        {
            var original = Mesh.OriginalGrid;
            var warped = Mesh.WarpedGrid;
            for (var r = 0; r < Mesh.Rows; r++)
                for (var c = 0; c < Mesh.Columns; c++)
                    warped[r, c] = Map(original[r, c]);
        }

        /// <summary>Where the unwarped point <paramref name="v"/> ends up.</summary>
        public SKPoint Map(SKPoint v)
        {
            if (Pins.Count == 0) return v;

            // Weights fall off with distance; a point right on a pin just follows it.
            var count = Pins.Count;
            Span<double> w = count <= 64 ? stackalloc double[count] : new double[count];
            double sw = 0, px = 0, py = 0, qx = 0, qy = 0;
            var power = Math.Clamp(Rigidity, 0.5f, 3f);
            for (var i = 0; i < count; i++)
            {
                var p = Pins[i].Original;
                double dx = p.X - v.X, dy = p.Y - v.Y;
                var d2 = dx * dx + dy * dy;
                if (d2 < 1e-6) return Pins[i].Current;
                w[i] = 1 / Math.Pow(d2, power);
                sw += w[i];
                px += w[i] * p.X; py += w[i] * p.Y;
                qx += w[i] * Pins[i].Current.X; qy += w[i] * Pins[i].Current.Y;
            }
            px /= sw; py /= sw; qx /= sw; qy /= sw;

            // The best rotation, as a complex number: sum of w * q^ * conj(p^).
            double mr = 0, mi = 0;
            for (var i = 0; i < count; i++)
            {
                double ax = Pins[i].Original.X - px, ay = Pins[i].Original.Y - py;
                double bx = Pins[i].Current.X - qx, by = Pins[i].Current.Y - qy;
                mr += w[i] * (bx * ax + by * ay);
                mi += w[i] * (by * ax - bx * ay);
            }

            double vx = v.X - px, vy = v.Y - py;
            var length = Math.Sqrt(mr * mr + mi * mi);
            if (length < 1e-9) return new SKPoint((float)(qx + vx), (float)(qy + vy)); // one pin: plain shift
            double cos = mr / length, sin = mi / length;
            return new SKPoint((float)(qx + vx * cos - vy * sin), (float)(qy + vx * sin + vy * cos));
        }

        /// <summary>The pin within <paramref name="reach"/> of <paramref name="at"/> (by
        /// its current position), or -1.</summary>
        public int PinAt(SKPoint at, float reach)
        {
            for (var i = Pins.Count - 1; i >= 0; i--)
            {
                var p = Pins[i].Current;
                if (Math.Abs(p.X - at.X) <= reach && Math.Abs(p.Y - at.Y) <= reach) return i;
            }
            return -1;
        }

        /// <summary>The unwarped point that currently shows at <paramref name="at"/>
        /// (found by stepping backwards), so a new pin sticks to what you clicked on.</summary>
        public SKPoint Unmap(SKPoint at)
        {
            var guess = at;
            for (var step = 0; step < 20; step++)
            {
                var mapped = Map(guess);
                float ex = at.X - mapped.X, ey = at.Y - mapped.Y;
                if (ex * ex + ey * ey < 0.01f) break;
                guess = new SKPoint(guess.X + ex, guess.Y + ey);
            }
            return guess;
        }
    }
}
