using System;
using System.Collections.Generic;
using System.Linq;

namespace JolieCat.Core.Adjustments
{
    /// <summary>
    /// Builds a 256-entry lookup table from a set of (input, output) control points -
    /// the Curves tool's own math. Interpolates with a Catmull-Rom spline through the
    /// sorted points (each result still clamped to a valid byte, since a spline can
    /// briefly overshoot between widely-spaced points) rather than a plain linear
    /// join, so the curve reads as smooth rather than faceted between control points.
    /// </summary>
    public static class CurvesAdjustment
    {
        public static AdjustmentLut BuildLut(IReadOnlyList<(double X, double Y)> controlPoints)
        {
            ArgumentNullException.ThrowIfNull(controlPoints);
            if (controlPoints.Count == 0) return AdjustmentLut.Identity();

            var points = controlPoints.OrderBy(p => p.X).ToList();

            // Every input value (0-255) needs a defined output, including outside
            // the user's own first/last placed point - extend flat from the nearest
            // endpoint rather than extrapolating the spline's tangent indefinitely.
            if (points[0].X > 0) points.Insert(0, (0, points[0].Y));
            if (points[^1].X < 255) points.Add((255, points[^1].Y));

            var table = new byte[256];
            for (var i = 0; i < 256; i++)
            {
                var y = Evaluate(points, i);
                table[i] = (byte)Math.Clamp(Math.Round(y), 0, 255);
            }

            return AdjustmentLut.Uniform(table);
        }

        private static double Evaluate(List<(double X, double Y)> points, double x)
        {
            var segment = points.Count - 2;
            for (var i = 0; i < points.Count - 1; i++)
            {
                if (x >= points[i].X && x <= points[i + 1].X) { segment = i; break; }
            }

            var p1 = points[segment];
            var p2 = points[segment + 1];

            // At either boundary, there's no real neighbor to take a tangent from -
            // reflecting the opposite point through the segment's own endpoint (the
            // standard Catmull-Rom "phantom point" construction) gives the correct
            // tangent there. Duplicating the endpoint itself instead (an earlier,
            // wrong version of this method did that) breaks the simplest possible
            // curve - two points with a straight line between them - into a visibly
            // curved, non-identity remap; reflection reduces correctly to that
            // straight line, confirmed against a probe.
            var p0 = segment > 0 ? points[segment - 1] : Reflect(p1, p2);
            var p3 = segment < points.Count - 2 ? points[segment + 2] : Reflect(p2, p1);

            var span = p2.X - p1.X;
            if (span <= 0) return p1.Y;
            var t = (x - p1.X) / span;

            // Standard uniform Catmull-Rom spline between p1 and p2, using p0/p3 to
            // define the tangent at each end.
            var t2 = t * t;
            var t3 = t2 * t;
            return 0.5 * (
                2 * p1.Y +
                (-p0.Y + p2.Y) * t +
                (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y) * t2 +
                (-p0.Y + 3 * p1.Y - 3 * p2.Y + p3.Y) * t3);
        }

        /// <summary>Mirrors <paramref name="b"/> through <paramref name="a"/> - the
        /// phantom point standing in for a Catmull-Rom segment's missing outer
        /// neighbor at either end of the curve.</summary>
        private static (double X, double Y) Reflect((double X, double Y) a, (double X, double Y) b) =>
            (2 * a.X - b.X, 2 * a.Y - b.Y);
    }
}
