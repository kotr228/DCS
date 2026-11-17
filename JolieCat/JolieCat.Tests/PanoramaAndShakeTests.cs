using JolieCat.Core.Filters;
using SkiaSharp;

namespace JolieCat.Tests
{
    public class PanoramaAndShakeTests
    {
        /// <summary>A wide scene with varied shapes to cut overlapping shots from.</summary>
        private static SKBitmap WideScene()
        {
            var bitmap = new SKBitmap(600, 200, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(new SKColor(70, 90, 120));
            var random = new Random(3);
            for (var i = 0; i < 40; i++)
            {
                using var paint = new SKPaint { Color = new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)), IsAntialias = true };
                canvas.DrawCircle(random.Next(600), random.Next(200), 8 + random.Next(25), paint);
            }
            return bitmap;
        }

        private static SKBitmap Cut(SKBitmap source, int x, int y, int w, int h)
        {
            var piece = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(piece);
            canvas.DrawBitmap(source, -x, -y);
            return piece;
        }

        [Fact]
        public void Stitch_PutsOverlappingShotsBackTogether()
        {
            using var scene = WideScene();
            using var a = Cut(scene, 0, 0, 260, 200);
            using var b = Cut(scene, 180, 6, 260, 190);    // overlaps a by 80 px, slightly lower
            using var c = Cut(scene, 340, 0, 260, 200);
            var (panorama, positions) = MultiImage.Stitch(new[] { a, b, c });
            using (panorama)
            {
                Assert.Equal(180, positions[1].X - positions[0].X);
                Assert.Equal(6, positions[1].Y - positions[0].Y);
                Assert.Equal(340, positions[2].X - positions[0].X);
                Assert.Equal(600, panorama.Width);
                var original = scene.GetPixel(300, 100);
                var stitched = panorama.GetPixel(300 + positions[0].X, 100 + positions[0].Y);
                Assert.InRange(stitched.Red, original.Red - 3, original.Red + 3);
            }
        }

        [Fact]
        public void ShakeReduction_SharpensAMotionBlurredEdge()
        {
            using var sharp = new SKBitmap(120, 40, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(sharp))
            {
                canvas.Clear(new SKColor(40, 40, 40));
                using var white = new SKPaint { Color = new SKColor(220, 220, 220) };
                canvas.DrawRect(60, 0, 60, 40, white);
            }
            using var shaken = EnhanceFilters.MotionBlur(sharp, 0, 10);
            using var fixedImage = PhotoFilters.ShakeReduction(shaken, 0, 10, 15);

            int Slope(SKBitmap b) => b.GetPixel(62, 20).Red - b.GetPixel(58, 20).Red;
            Assert.True(Slope(fixedImage) > Slope(shaken) + 20, $"edge {Slope(shaken)} -> {Slope(fixedImage)}");
        }
    }
}
