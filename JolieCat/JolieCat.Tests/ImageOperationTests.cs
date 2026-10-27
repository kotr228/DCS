using JolieCat.Core.Documents;
using JolieCat.Core.Filters;
using SkiaSharp;

namespace JolieCat.Tests
{
    /// <summary>Image-menu operations (rotate, flip, resample, flatten), selection
    /// modifiers and the photo filters.</summary>
    public class ImageOperationTests
    {
        /// <summary>A 4×2 scene with a red pixel at the top-left corner.</summary>
        private static Scene MarkedScene()
        {
            var scene = new Scene("S");
            var layer = scene.AddLayer("L", 4, 2);
            layer.Canvas.Clear(SKColors.White);
            TestHelpers.FillRect(layer, new SKRect(0, 0, 1, 1), SKColors.Red);
            return scene;
        }

        [Fact]
        public void Rotate90Clockwise_SwapsSizeAndMovesTopLeftToTopRight()
        {
            using var scene = MarkedScene();
            var id = scene.Layers[0].Id;

            scene.RotateLayers(1);

            var bitmap = scene.Layers[0].Bitmap;
            Assert.Equal((2, 4), (bitmap.Width, bitmap.Height));
            Assert.Equal(SKColors.Red, bitmap.GetPixel(1, 0));
            Assert.Equal(SKColors.White, bitmap.GetPixel(0, 0));
            Assert.Equal(id, scene.Layers[0].Id);
        }

        [Fact]
        public void Rotate90CounterClockwise_MovesTopLeftToBottomLeft()
        {
            using var scene = MarkedScene();
            scene.RotateLayers(-1);

            Assert.Equal(SKColors.Red, scene.Layers[0].Bitmap.GetPixel(0, 3));
        }

        [Fact]
        public void Rotate180_MovesTopLeftToBottomRight()
        {
            using var scene = MarkedScene();
            scene.RotateLayers(2);

            Assert.Equal(SKColors.Red, scene.Layers[0].Bitmap.GetPixel(3, 1));
        }

        [Fact]
        public void FlipHorizontal_MirrorsContent()
        {
            using var scene = MarkedScene();
            scene.FlipLayers(horizontal: true);

            Assert.Equal(SKColors.Red, scene.Layers[0].Bitmap.GetPixel(3, 0));
            Assert.Equal(SKColors.White, scene.Layers[0].Bitmap.GetPixel(0, 0));
        }

        [Fact]
        public void FlipLayerContent_LeavesOtherLayersAlone()
        {
            using var scene = MarkedScene();
            var top = scene.AddLayer("Top", 4, 2);
            TestHelpers.FillRect(top, new SKRect(0, 0, 1, 2), SKColors.Blue);

            Scene.FlipLayerContent(top, horizontal: true);

            Assert.Equal(SKColors.Blue, top.Bitmap.GetPixel(3, 1));
            Assert.Equal(0, top.Bitmap.GetPixel(0, 1).Alpha);
            Assert.Equal(SKColors.Red, scene.Layers[0].Bitmap.GetPixel(0, 0));
        }

        [Fact]
        public void Resample_ChangesSizeAndKeepsColors()
        {
            using var scene = new Scene("S");
            scene.AddLayer("L", 100, 50).Canvas.Clear(SKColors.Green);

            scene.ResampleLayers(40, 20);

            var bitmap = scene.Layers[0].Bitmap;
            Assert.Equal((40, 20), (bitmap.Width, bitmap.Height));
            Assert.Equal(SKColors.Green, bitmap.GetPixel(20, 10));
        }

        [Fact]
        public void ResizeLayers_WithOffset_CentersContent()
        {
            using var scene = MarkedScene();
            scene.ResizeLayers(6, 4, 1, 1);

            var bitmap = scene.Layers[0].Bitmap;
            Assert.Equal(SKColors.Red, bitmap.GetPixel(1, 1));
            Assert.Equal(0, bitmap.GetPixel(0, 0).Alpha);
        }

        [Fact]
        public void Flatten_MergesVisibleAndDropsHidden()
        {
            using var scene = new Scene("S");
            scene.AddLayer("Bg", 4, 4).Canvas.Clear(SKColors.White);
            var hidden = scene.AddLayer("Hidden", 4, 4);
            hidden.Canvas.Clear(SKColors.Black);
            hidden.IsVisible = false;
            var top = scene.AddLayer("Top", 4, 4);
            TestHelpers.FillRect(top, new SKRect(0, 0, 2, 4), SKColors.Red);

            scene.Flatten();

            Assert.Single(scene.Layers);
            Assert.Equal(SKColors.Red, scene.Layers[0].Bitmap.GetPixel(0, 0));
            Assert.Equal(SKColors.White, scene.Layers[0].Bitmap.GetPixel(3, 0));
            Assert.Same(scene.Layers[0], scene.ActiveLayer);
        }

        [Fact]
        public void MergeVisible_KeepsHiddenLayers()
        {
            using var scene = new Scene("S");
            scene.AddLayer("Bg", 4, 4).Canvas.Clear(SKColors.White);
            var hidden = scene.AddLayer("Hidden", 4, 4);
            hidden.IsVisible = false;
            var top = scene.AddLayer("Top", 4, 4);
            TestHelpers.FillRect(top, new SKRect(0, 0, 2, 4), SKColors.Red);

            Assert.True(scene.MergeVisible());

            Assert.Equal(2, scene.Layers.Count);
            Assert.Equal("Bg", scene.Layers[0].Name);
            Assert.Equal(SKColors.Red, scene.Layers[0].Bitmap.GetPixel(0, 0));
            Assert.Same(hidden, scene.Layers[1]);
        }

        [Fact]
        public void SelectionExpandAndContract_ChangeArea()
        {
            var selection = new Selection();
            selection.SetRect(new SKRectI(40, 40, 60, 60));

            selection.Expand(5, 100, 100);
            Assert.True(selection.Contains(37, 50));
            Assert.False(selection.Contains(30, 50));

            selection.Contract(10, 100, 100);
            Assert.False(selection.Contains(43, 50));
            Assert.True(selection.Contains(50, 50));
        }

        [Fact]
        public void SelectionContract_TooFar_ClearsSelection()
        {
            var selection = new Selection();
            selection.SetRect(new SKRectI(0, 0, 10, 10));

            selection.Contract(20, 100, 100);

            Assert.False(selection.HasSelection);
        }

        [Fact]
        public void SelectionBorder_SelectsOnlyTheEdge()
        {
            var selection = new Selection();
            selection.SetRect(new SKRectI(20, 20, 80, 80));

            selection.Border(6, 100, 100);

            Assert.True(selection.Contains(20, 50));
            Assert.False(selection.Contains(50, 50));
        }

        [Fact]
        public void Median_RemovesIsolatedSpeck()
        {
            using var source = TestHelpers.Solid(9, 9, SKColors.White);
            source.SetPixel(4, 4, SKColors.Black);

            using var result = EnhanceFilters.Median(source, 1);

            Assert.Equal(SKColors.White, result.GetPixel(4, 4));
        }

        [Fact]
        public void MotionBlur_SpreadsAlongTheAngleOnly()
        {
            using var source = TestHelpers.Solid(21, 21, SKColors.Black);
            source.SetPixel(10, 10, SKColors.White);

            using var result = EnhanceFilters.MotionBlur(source, 0, 6);

            Assert.True(result.GetPixel(12, 10).Red > 0);   // along the horizontal streak
            Assert.Equal(0, result.GetPixel(10, 13).Red);   // not vertically
        }

        [Fact]
        public void AutoContrast_StretchesToFullRange()
        {
            using var source = TestHelpers.Solid(10, 10, new SKColor(100, 100, 100));
            for (var x = 0; x < 5; x++) source.SetPixel(x, 0, new SKColor(150, 150, 150));

            using var result = EnhanceFilters.AutoContrast(source);

            Assert.Equal(0, result.GetPixel(9, 9).Red);
            Assert.Equal(255, result.GetPixel(0, 0).Red);
        }

        [Fact]
        public void UnsharpMask_IncreasesEdgeContrast()
        {
            using var source = TestHelpers.Solid(20, 20, new SKColor(100, 100, 100));
            using (var canvas = new SKCanvas(source))
            using (var paint = new SKPaint { Color = new SKColor(160, 160, 160) })
                canvas.DrawRect(10, 0, 10, 20, paint);

            using var result = EnhanceFilters.UnsharpMask(source, 2, 100);

            Assert.True(result.GetPixel(10, 10).Red > 160); // bright side of the edge gets brighter
            Assert.True(result.GetPixel(9, 10).Red < 100);  // dark side gets darker
        }

        [Fact]
        public void Vibrance_SaturatesDullColorsMore()
        {
            using var dull = TestHelpers.Solid(2, 2, new SKColor(140, 120, 120));
            using var result = EnhanceFilters.Vibrance(dull, 100);

            var p = result.GetPixel(0, 0);
            Assert.True(p.Red - p.Green > 20);
        }
    }
}
