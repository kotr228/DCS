using JolieCat.Core.Documents;
using SkiaSharp;

namespace JolieCat.Tests
{
    public class PerspectiveCropTests
    {
        [Fact]
        public void SquareToQuad_HitsEveryCorner()
        {
            var quad = new[] { new SKPoint(10, 20), new SKPoint(90, 5), new SKPoint(100, 80), new SKPoint(0, 70) };
            var m = Scene.SquareToQuad(quad);
            var corners = new[] { new SKPoint(0, 0), new SKPoint(1, 0), new SKPoint(1, 1), new SKPoint(0, 1) };
            for (var i = 0; i < 4; i++)
            {
                var p = m.MapPoint(corners[i]);
                Assert.Equal(quad[i].X, p.X, 3);
                Assert.Equal(quad[i].Y, p.Y, 3);
            }
        }

        [Fact]
        public void PerspectiveCrop_StraightensASkewedShape_IntoTheWholeCanvas()
        {
            // A white 200x150 canvas with a red, perspective-skewed "sign" on it.
            using var scene = new Scene("P");
            var layer = scene.AddLayer("Bg", 200, 150);
            layer.Canvas.Clear(SKColors.White);
            var quad = new[] { new SKPoint(40, 30), new SKPoint(170, 20), new SKPoint(160, 130), new SKPoint(50, 110) };
            using (var path = new SKPath())
            using (var red = new SKPaint { Color = SKColors.Red, IsAntialias = false })
            {
                path.AddPoly(quad, close: true);
                layer.Canvas.DrawPath(path, red);
            }
            scene.Guides.Add(new Guide(true, 50));

            var size = Scene.PerspectiveCropSize(quad);
            scene.PerspectiveCropLayers(quad, size.Width, size.Height);

            var result = scene.Layers[0].Bitmap;
            Assert.Equal(size.Width, result.Width);
            Assert.Equal(size.Height, result.Height);
            // Inset a couple of pixels from the edge: all of it is the red sign now.
            foreach (var p in new[] { new SKPointI(3, 3), new SKPointI(size.Width - 4, 3), new SKPointI(size.Width / 2, size.Height / 2), new SKPointI(3, size.Height - 4) })
                Assert.True(result.GetPixel(p.X, p.Y).Red > 200 && result.GetPixel(p.X, p.Y).Green < 60, $"{p}: {result.GetPixel(p.X, p.Y)}");
            Assert.Empty(scene.Guides);
        }
    }
}
