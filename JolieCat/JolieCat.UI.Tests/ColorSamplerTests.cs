using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class ColorSamplerTests
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
        public void Samplers_AreAddedMovedAndShownInInfo() => Sta(() =>
        {
            var toolbox = new ToolboxViewModel();
            var doc = new DocumentViewModel(toolbox, "T");
            var layer = doc.Layers.ActiveLayer!.Model;
            layer.Canvas.Clear(SKColors.White);
            using (var red = new SKPaint { Color = new SKColor(200, 20, 30) })
                layer.Canvas.DrawRect(0, 0, 100, 100, red);
            doc.Layers.NotifyContentChanged();
            var canvas = doc.Canvas;
            canvas.SetViewportSize(800, 600);
            SKPoint Device(float x, float y) => new((float)(x * canvas.Zoom + canvas.PanX), (float)(y * canvas.Zoom + canvas.PanY));

            toolbox.SelectTool(ToolType.ColorSampler);
            canvas.OnPointerPressed(Device(50, 50));
            canvas.OnPointerReleased(Device(50, 50));
            canvas.OnPointerPressed(Device(400, 400));
            canvas.OnPointerReleased(Device(400, 400));
            Assert.Equal(2, canvas.ColorSamplers.Count);
            Assert.False(doc.Layers.History.CanUndo);   // samplers don't change the image

            var panels = new DocumentPanelsViewModel(() => doc, startTimer: false);
            panels.Refresh();
            Assert.Equal(2, panels.Samplers.Count);
            Assert.StartsWith("R 200", panels.Samplers[0].Text);
            Assert.StartsWith("R 255", panels.Samplers[1].Text);

            // Drag the second one onto the red square.
            canvas.OnPointerPressed(Device(400, 400));
            canvas.OnPointerMoved(Device(20.5f, 30.5f));
            canvas.OnPointerReleased(Device(20.5f, 30.5f));
            panels.Refresh();
            Assert.StartsWith("R 200", panels.Samplers[1].Text);
            Assert.Contains("(20, 30)", panels.Samplers[1].Text);

            panels.ClearSamplersCommand.Execute(null);
            Assert.Empty(canvas.ColorSamplers);
        });
    }
}
