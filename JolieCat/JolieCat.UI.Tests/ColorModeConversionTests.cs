using JolieCat.Core.Filters;
using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class ColorModeConversionTests
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
        public void Grayscale_Indexed_Bitmap_AndApplyImage_EachUndoInOneStep() => Sta(() =>
        {
            var doc = new DocumentViewModel(new ToolboxViewModel(), "M", ProjectType.StandardImage, 40, 30, null);
            var layers = doc.Layers;
            SKBitmap Bg() => layers.Scene.Layers[0].Bitmap;
            Bg().Erase(new SKColor(200, 40, 40));
            layers.AddLayerCommand.Execute(null);
            using (var blue = new SKPaint { Color = SKColors.Blue }) layers.ActiveLayer!.Model.Canvas.DrawRect(0, 0, 10, 10, blue);

            layers.ConvertToGrayscale();
            var g = Bg().GetPixel(20, 20);
            Assert.Equal(g.Red, g.Green);
            Assert.Equal(g.Green, g.Blue);
            var top = layers.Scene.Layers[1].Bitmap.GetPixel(5, 5);
            Assert.Equal(top.Red, top.Blue);
            layers.History.Undo();
            Assert.Equal(new SKColor(200, 40, 40), Bg().GetPixel(20, 20));
            Assert.Equal(SKColors.Blue, layers.Scene.Layers[1].Bitmap.GetPixel(5, 5));

            layers.ConvertToBitmapMode(dither: true);
            Assert.All(Bg().Pixels, c => Assert.True(c.Red is 0 or 255));
            layers.History.Undo();

            layers.ConvertToIndexed(2, dither: false);
            Assert.True(Bg().Pixels.Distinct().Count() <= 2);
            layers.History.Undo();

            // Apply Image: the background's red channel, multiplied onto the top layer.
            layers.ApplyImage(layers.Scene.Layers[0], 1, false, ColorModes.ApplyBlend.Multiply, 100);
            var applied = layers.Scene.Layers[1].Bitmap.GetPixel(5, 5);
            Assert.Equal(0, applied.Red);                     // blue has no red
            Assert.InRange((int)applied.Blue, 198, 202);      // 255 x 200/255
            Assert.Equal(0, layers.Scene.Layers[1].Bitmap.GetPixel(30, 20).Alpha);   // see-through stays see-through
            layers.History.Undo();
            Assert.Equal(SKColors.Blue, layers.Scene.Layers[1].Bitmap.GetPixel(5, 5));
        });
    }
}
