using JolieCat.Core.Filters;
using SkiaSharp;

namespace JolieCat.Tests
{
    public class BlurGalleryTests
    {
        /// <summary>100x100 vertical black/white stripes, 2 px wide: blurring turns them gray.</summary>
        private static SKBitmap Stripes()
        {
            var bitmap = new SKBitmap(100, 100, SKColorType.Rgba8888, SKAlphaType.Premul);
            for (var y = 0; y < 100; y++)
                for (var x = 0; x < 100; x++)
                    bitmap.SetPixel(x, y, (x / 2) % 2 == 0 ? SKColors.Black : SKColors.White);
            return bitmap;
        }

        private static int Contrast(SKBitmap b, int y) => Math.Abs(b.GetPixel(10, y).Red - b.GetPixel(12, y).Red);

        [Fact]
        public void TiltShift_KeepsTheBandSharp_AndBlursAboveAndBelow()
        {
            using var source = Stripes();
            using var result = BlurGallery.Apply(source, new BlurGallerySettings { CenterY = 0.5, Size = 0.1, Transition = 0.2, Blur = 6 });

            Assert.Equal(255, Contrast(result, 50));   // in the band: untouched
            Assert.True(Contrast(result, 2) < 40, $"{Contrast(result, 2)}");    // top: blurred
            Assert.True(Contrast(result, 97) < 40);    // bottom: blurred
            var middle = Contrast(result, 64);         // early in the transition: partly
            Assert.InRange(middle, 40, 254);
        }

        [Fact]
        public void IrisBlur_KeepsTheEllipseSharp_AndBlursTheCorners()
        {
            using var source = Stripes();
            using var result = BlurGallery.Apply(source, new BlurGallerySettings { Iris = true, CenterX = 0.5, CenterY = 0.5, Size = 0.2, SizeY = 0.2, Transition = 0.1, Blur = 6 });

            Assert.Equal(255, Math.Abs(result.GetPixel(48, 50).Red - result.GetPixel(50, 50).Red));
            Assert.True(Math.Abs(result.GetPixel(2, 2).Red - result.GetPixel(4, 2).Red) < 40);
        }

        [Fact]
        public void BlurringAnOpaqueImage_KeepsItsEdgesOpaque()
        {
            using var source = TestHelpers.Solid(40, 40, SKColors.Red);
            using var blurred = BlurFilter.Apply(source, BlurType.Gaussian, 8);
            Assert.Equal(255, blurred.GetPixel(0, 0).Alpha);
            Assert.Equal(255, blurred.GetPixel(39, 20).Alpha);
        }

        [Fact]
        public void Weight_RisesSmoothly_FromTheSharpArea()
        {
            var s = new BlurGallerySettings { CenterY = 0.5, Size = 0.1, Transition = 0.2 };
            Assert.Equal(0, BlurGallery.Weight(s, 50, 55, 100, 100));
            Assert.InRange(BlurGallery.Weight(s, 50, 70, 100, 100), 0.3f, 0.7f);
            Assert.Equal(1, BlurGallery.Weight(s, 50, 95, 100, 100));

            // A 90° band is vertical: distance is measured left-right.
            var turned = s with { Angle = 90 };
            Assert.Equal(0, BlurGallery.Weight(turned, 55, 5, 100, 100));
            Assert.Equal(1, BlurGallery.Weight(turned, 95, 50, 100, 100));
        }
    }
}
