using System;
using SkiaSharp;

namespace JolieCat.Core.Documents
{
    /// <summary>
    /// Shared color-similarity test used by both flood fill (Paint Bucket, in
    /// <c>JolieCat.UI.ViewModels.CanvasViewModel</c>) and flood selection
    /// (<see cref="Selection.CreateRegionFromColorFlood"/>, Magic Wand/Quick Selection) -
    /// Euclidean distance across RGBA channels, compared against a caller-supplied
    /// tolerance, rather than each duplicating the same handful of lines.
    /// </summary>
    public static class ColorTolerance
    {
        public static bool IsWithin(SKColor a, SKColor b, float tolerance)
        {
            var dr = a.Red - b.Red;
            var dg = a.Green - b.Green;
            var db = a.Blue - b.Blue;
            var da = a.Alpha - b.Alpha;
            return Math.Sqrt(dr * dr + dg * dg + db * db + da * da) <= tolerance;
        }
    }
}
