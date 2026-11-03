using JolieCat.Core.Documents;
using SkiaSharp;

namespace JolieCat.Tests
{
    public class FillAndVectorTests
    {
        [Fact]
        public void FillLayers_DrawSolidGradientAndPattern()
        {
            using var solid = new SKBitmap(40, 20);
            using (var c = new SKCanvas(solid))
                new FillContent(FillKind.SolidColor, SKColors.Teal, SKColors.Black).Render(c, 40, 20);
            Assert.Equal(SKColors.Teal, solid.GetPixel(20, 10));

            using var gradient = new SKBitmap(40, 20);
            using (var c = new SKCanvas(gradient))
                new FillContent(FillKind.Gradient, SKColors.Black, SKColors.White, Angle: 0).Render(c, 40, 20);
            Assert.True(gradient.GetPixel(1, 10).Red < 20);
            Assert.True(gradient.GetPixel(38, 10).Red > 235);

            using var pattern = new SKBitmap(32, 32);
            using (var c = new SKCanvas(pattern))
                new FillContent(FillKind.Pattern, SKColors.Black, SKColors.White, PatternName: "Checker").Render(c, 32, 32);
            Assert.Equal(SKColors.Black, pattern.GetPixel(2, 2));
            Assert.Equal(SKColors.White, pattern.GetPixel(10, 2));
        }

        [Fact]
        public void FillLayers_StayFills_ThroughCanvasChanges_AndSaving()
        {
            using var scene = new Scene("F");
            var layer = scene.AddLayer("Fill", 40, 20);
            layer.Fill = new FillContent(FillKind.SolidColor, SKColors.Orange, SKColors.Black);
            layer.RenderFill();

            scene.ResizeLayers(60, 30, 10, 5);
            Assert.True(scene.Layers[0].IsFill);
            Assert.Equal(SKColors.Orange, scene.Layers[0].Bitmap.GetPixel(2, 2));   // the new area is filled too

            scene.RotateLayers(1);
            Assert.True(scene.Layers[0].IsFill);
            Assert.Equal(30, scene.Layers[0].Bitmap.Width);

            var fill = new FillContent(FillKind.Gradient, SKColors.Red, SKColors.Blue, 33, true, "Dots", 150);
            Assert.Equal(fill, FillContent.Deserialize(fill.Serialize()));
        }

        [Fact]
        public void ImageSize_ScalesTextAndShapes_AndKeepsThemEditable()
        {
            using var scene = new Scene("V");
            scene.AddLayer("Bg", 200, 100).Canvas.Clear(SKColors.White);
            var text = scene.AddLayer("Text", 200, 100);
            text.Text = new TextContent("Hi", "Arial", 20, false, false, SKColors.Black, new SKPoint(10, 10), false);
            text.RenderText();
            var shape = scene.AddLayer("Shape", 200, 100);
            shape.Shape = new ShapeContent(VectorShapeKind.Rectangle, new SKPoint(50, 20), new SKPoint(90, 60), SKColors.Red, SKColors.Black, 2);
            shape.RenderShape();

            scene.ResampleLayers(400, 200);

            Assert.Equal(40, scene.Layers[1].Text!.FontSize, 3);
            Assert.Equal(new SKPoint(20, 20), scene.Layers[1].Text!.Origin);
            Assert.Equal(new SKPoint(100, 40), scene.Layers[2].Shape!.Start);
            Assert.Equal(4, scene.Layers[2].Shape!.StrokeWidth, 3);
        }

        [Fact]
        public void Rotating_KeepsBoxShapes_ButTurnsTextIntoPixels()
        {
            using var scene = new Scene("R");
            var text = scene.AddLayer("Text", 200, 100);
            text.Text = new TextContent("Hi", "Arial", 20, false, false, SKColors.Black, new SKPoint(10, 10), false);
            text.RenderText();
            var shape = scene.AddLayer("Shape", 200, 100);
            shape.Shape = new ShapeContent(VectorShapeKind.Ellipse, new SKPoint(10, 20), new SKPoint(60, 40), SKColors.Red, SKColors.Black, 0);
            shape.RenderShape();

            scene.RotateLayers(1);   // 90° clockwise: (x, y) -> (100 - y, x)

            Assert.Null(scene.Layers[0].Text);
            var turned = scene.Layers[1].Shape!;
            Assert.Equal(new SKRect(60, 10, 80, 60), turned.Box);
            Assert.Equal(SKColors.Red, scene.Layers[1].Bitmap.GetPixel(70, 35));
        }
    }
}
