using System.Diagnostics;
using JolieCat.Core.Adjustments;
using JolieCat.Core.Filters;
using SkiaSharp;
using Xunit.Abstractions;

namespace JolieCat.Tests
{
    /// <summary>Timings of the heavier filters on a 12-megapixel photo. Only runs with
    /// the JC_BENCH environment variable set (it's slow); prints the timings.</summary>
    public class FilterBenchmarks
    {
        private readonly ITestOutputHelper _output;

        public FilterBenchmarks(ITestOutputHelper output) => _output = output;

        [Fact]
        public void Time12MegapixelFilters()
        {
            if (Environment.GetEnvironmentVariable("JC_BENCH") is null) return;

            using var photo = new SKBitmap(4000, 3000, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(photo))
            {
                using var shader = SKShader.CreatePerlinNoiseFractalNoise(0.01f, 0.01f, 4, 3);
                using var paint = new SKPaint { Shader = shader };
                canvas.DrawRect(0, 0, 4000, 3000, paint);
            }
            using var second = photo.Copy();

            void Time(string name, Func<SKBitmap> run)
            {
                var watch = Stopwatch.StartNew();
                using var result = run();
                _output.WriteLine($"{name,-22} {watch.ElapsedMilliseconds,6} ms");
            }

            Time("Equalize", () => ToneFilters.Equalize(photo));
            Time("Shadows/Highlights", () => ToneFilters.ShadowsHighlights(photo, 35, 20, 50, 30));
            Time("Replace Color", () => ToneFilters.ReplaceColor(photo, SKColors.Red, 40, 90, 0, 0));
            Time("Match Color", () => PhotoFilters.MatchColor(photo, ColorStats.Of(second), 100, 100, 0, false));
            Time("Lens Blur r=15", () => PhotoFilters.LensBlur(photo, 15, 30, 220));
            Time("Lens Blur r=50", () => PhotoFilters.LensBlur(photo, 50, 30, 220));
            Time("Smart Sharpen", () => ArtFilters.SmartSharpen(photo, 150, 1.5f));
            Time("Oil Paint", () => ArtFilters.OilPaint(photo, 4, 20));
            Time("Spherize", () => ArtFilters.Spherize(photo, 100));
            Time("Ripple", () => ArtFilters.Ripple(photo, 100, 24));
            Time("Lens Correction", () => ArtFilters.LensCorrection(photo, 20, 10, 10, -30, 50));
            Time("Lens Flare", () => ArtFilters.LensFlare(photo, 0.3f, 0.3f, 100, 0));
            Time("Lighting Effects", () => ArtFilters.LightingEffects(photo, 0, 0.4f, 0.4f, 0.6f, 100, 25, SKColors.White, 30));
            Time("Clouds", () => ArtFilters.Clouds(photo, SKColors.Black, SKColors.White, 1));
            Time("Merge to HDR (2)", () => MultiImage.FuseExposures(new[] { photo, second }));
            var watch = Stopwatch.StartNew();
            MultiImage.FindOffset(photo, second);
            _output.WriteLine($"{"Auto-Align",-22} {watch.ElapsedMilliseconds,6} ms");
        }
    }
}
