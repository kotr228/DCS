using JolieCat.Core.Filters;
using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class CalculationsTests
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
        public void GrayResult_AsLayerSelectionOrMask_AndMaskFromTransparency() => Sta(() =>
        {
            var doc = new DocumentViewModel(new ToolboxViewModel(), "C", ProjectType.StandardImage, 100, 50, SKColors.White);
            var layers = doc.Layers;
            layers.AddLayerCommand.Execute(null);
            using (var red = new SKPaint { Color = SKColors.Red }) layers.ActiveLayer!.Model.Canvas.DrawRect(0, 0, 50, 50, red);

            // Mask from transparency: shows the painted left half only.
            Assert.True(layers.AddMaskFromTransparency());
            var mask = layers.Scene.ActiveLayer!.Mask!;
            Assert.Equal(255, mask.Bitmap.GetPixel(10, 10).Red);
            Assert.Equal(0, mask.Bitmap.GetPixel(80, 10).Red);
            layers.History.Undo();
            Assert.Null(layers.Scene.ActiveLayer!.Mask);

            // Calculations: white where the layer is see-through (transparency inverted).
            using var gray = ColorModes.Calculate(layers.Scene.ActiveLayer.Bitmap, 4, true, layers.Scene.ActiveLayer.Bitmap, 4, true, ColorModes.ApplyBlend.Normal, 100);
            layers.UseGrayResult(gray, "Selection");
            var bounds = layers.Scene.Selection.Region!.Bounds;
            Assert.Equal(50, bounds.Left);
            Assert.Equal(100, bounds.Right);

            var count = layers.Scene.Layers.Count;
            layers.UseGrayResult(gray, "New Layer");
            Assert.Equal(count + 1, layers.Scene.Layers.Count);
            Assert.Equal("Calculations", layers.Scene.ActiveLayer!.Name);
        });
    }
}
