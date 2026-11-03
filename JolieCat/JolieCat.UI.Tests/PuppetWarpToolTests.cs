using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class PuppetWarpToolTests
    {
        private static void Sta(Action test)
        {
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try { test(); }
                catch (Exception ex) { error = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error is not null) throw new Exception(error.Message, error);
        }

        [Fact]
        public void PinsBendTheLayer_EnterAppliesOneUndoStep_EscCancels() => Sta(() =>
        {
            var toolbox = new ToolboxViewModel();
            var doc = new DocumentViewModel(toolbox, "T");
            var layer = doc.Layers.ActiveLayer!.Model;
            layer.Canvas.Clear(SKColors.Transparent);
            using (var bar = new SKPaint { Color = SKColors.Black })
                layer.Canvas.DrawRect(100, 290, 1000, 20, bar);     // a horizontal bar
            var canvas = doc.Canvas;
            canvas.SetViewportSize(800, 600);
            SKPoint Device(float x, float y) => new((float)(x * canvas.Zoom + canvas.PanX), (float)(y * canvas.Zoom + canvas.PanY));
            void Click(float x, float y) { canvas.OnPointerPressed(Device(x, y)); canvas.OnPointerReleased(Device(x, y)); }

            toolbox.SelectTool(ToolType.PuppetWarp);
            Assert.NotNull(canvas.Puppet);
            Click(150, 300);                                     // hold the left end
            Click(1050, 300);                                    // pin the right end...
            canvas.OnPointerPressed(Device(1050, 300));          // ...and pull it down
            canvas.OnPointerMoved(Device(1050, 500));
            canvas.OnPointerReleased(Device(1050, 500));
            Assert.Equal(2, canvas.Puppet!.Pins.Count);
            Assert.Equal(255, layer.Bitmap.GetPixel(1050, 300).Alpha);   // not applied yet

            canvas.CommitPuppetWarp();
            Assert.Null(canvas.Puppet);
            Assert.True(layer.Bitmap.GetPixel(1050, 500).Alpha > 200, "the bar's end moved down");
            Assert.Equal(0, layer.Bitmap.GetPixel(1050, 300).Alpha);
            Assert.Equal(255, layer.Bitmap.GetPixel(150, 300).Alpha);   // held end stayed

            doc.Layers.History.Undo();
            Assert.Equal(255, layer.Bitmap.GetPixel(1050, 300).Alpha);

            // Esc: nothing changes.
            toolbox.SelectTool(ToolType.Brush);
            toolbox.SelectTool(ToolType.PuppetWarp);
            canvas.OnPointerPressed(Device(1050, 300));
            canvas.OnPointerMoved(Device(1050, 100));
            canvas.OnPointerReleased(Device(1050, 100));
            canvas.CancelPuppetWarp();
            Assert.Equal(255, layer.Bitmap.GetPixel(1050, 300).Alpha);
        });
    }
}
