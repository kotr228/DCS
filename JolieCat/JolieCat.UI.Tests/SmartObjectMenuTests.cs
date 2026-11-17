using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class SmartObjectMenuTests
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
        public void Convert_ThenReplace_KeepTheBox_AndUndo() => Sta(() =>
        {
            var doc = new DocumentViewModel(new ToolboxViewModel(), "S", ProjectType.StandardImage, 200, 200, null);
            var layers = doc.Layers;
            layers.AddLayerCommand.Execute(null);
            using (var red = new SKPaint { Color = SKColors.Red }) layers.ActiveLayer!.Model.Canvas.DrawRect(50, 60, 40, 20, red);
            layers.ActiveLayer!.Model.Opacity = 0.5;

            Assert.True(layers.ConvertToSmartObject());
            var smart = layers.Scene.ActiveLayer!;
            Assert.NotNull(smart.SmartObject);
            Assert.Equal(0.5, smart.Opacity);
            Assert.Equal(40, smart.SmartObject!.SourceBitmap.Width);
            Assert.Equal(SKColors.Red, smart.Bitmap.GetPixel(70, 70));          // same place
            Assert.Equal(0, smart.Bitmap.GetPixel(30, 70).Alpha);

            // A square blue picture in the 40 x 20 box: fitted 20 x 20, in the middle.
            using var blue = new SKBitmap(100, 100, SKColorType.Rgba8888, SKAlphaType.Premul);
            blue.Erase(SKColors.Blue);
            Assert.True(layers.ReplaceSmartObjectContents(blue, "blue"));
            var replaced = layers.Scene.ActiveLayer!;
            Assert.Equal(SKColors.Blue, replaced.Bitmap.GetPixel(70, 70));
            Assert.Equal(0, replaced.Bitmap.GetPixel(55, 70).Alpha);            // the box's sides are now empty
            Assert.Equal(100, replaced.SmartObject!.SourceBitmap.Width);         // full quality kept inside

            layers.History.Undo();
            Assert.Equal(SKColors.Red, layers.Scene.ActiveLayer!.Bitmap.GetPixel(70, 70));
            layers.History.Undo();
            Assert.Null(layers.Scene.ActiveLayer!.SmartObject);
        });
    }
}
