using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class CornerPinTests
    {
        private static void Sta(Action test)
        {
            Exception? error = null;
            var thread = new Thread(() => { try { test(); } catch (Exception ex) { error = ex; } });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error is not null) throw new Exception(error.Message, error);
        }

        [Fact]
        public void Corners_Distort_PerspectiveMirrors_AndTheLayerChanges() => Sta(() =>
        {
            using var background = new SKBitmap(200, 100, SKColorType.Rgba8888, SKAlphaType.Premul);
            background.Erase(SKColors.White);
            using var content = new SKBitmap(100, 50, SKColorType.Rgba8888, SKAlphaType.Premul);
            content.Erase(SKColors.Red);
            using var model = new CornerPinViewModel(background, content.Copy(), SKRectI.Create(50, 25, 100, 50), 200, 100);
            Assert.NotNull(model.Preview);
            var s = model.Scale;

            model.Perspective = true;
            model.DragCorner(0, 20 * s, 0);                     // top-left in by 20: top-right in by 20 too
            Assert.Equal(70, model.Corners[0].X, 2);
            Assert.Equal(130, model.Corners[1].X, 2);
            using (var r = model.Render())
            {
                Assert.Equal(0, r.GetPixel(55, 27).Alpha);       // the top's corners are now further in
                Assert.Equal(SKColors.Red, r.GetPixel(55, 73));  // the bottom is as wide as before
                Assert.Equal(SKColors.Red, r.GetPixel(100, 30));
            }

            model.Reset();
            model.Perspective = false;
            model.DragCorner(2, 30 * s, 20 * s);                 // just one corner
            Assert.Equal(new SKPoint(180, 95), model.Corners[2]);
            Assert.Equal(new SKPoint(50, 75), model.Corners[3]);

            var doc = new DocumentViewModel(new ToolboxViewModel(), "D", ProjectType.StandardImage, 200, 100, SKColors.White);
            var layer = doc.Layers.Scene.Layers[0];
            using (var pinned = model.Render()) doc.Layers.ReplaceLayerPixels(layer, pinned, "Distort");
            Assert.Equal(SKColors.Red, doc.Layers.Scene.Layers[0].Bitmap.GetPixel(170, 88));
            doc.Layers.History.Undo();
            Assert.Equal(SKColors.White, doc.Layers.Scene.Layers[0].Bitmap.GetPixel(170, 88));
        });
    }
}
