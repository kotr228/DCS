using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class TypeOnPathUiTests
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
        public void PenClicks_ThenTypeOnPath_MakesAnEditableTextLayer() => Sta(() =>
        {
            var toolbox = new ToolboxViewModel();
            var doc = new DocumentViewModel(toolbox, "P", ProjectType.StandardImage, 400, 300, null);
            var canvas = doc.Canvas;
            SKPoint Device(float x, float y) => new((float)(x * canvas.Zoom + canvas.PanX), (float)(y * canvas.Zoom + canvas.PanY));

            Assert.False(canvas.TypeOnPenPath("no path yet"));
            toolbox.SelectTool(ToolType.Pen);
            foreach (var (x, y) in new[] { (20f, 200f), (200f, 60f), (380f, 200f) })
            {
                canvas.OnPointerPressed(Device(x, y));
                canvas.OnPointerReleased(Device(x, y));
            }
            Assert.NotNull(canvas.PenPath);

            Assert.True(canvas.TypeOnPenPath("Hello path"));
            var layer = doc.Layers.Scene.ActiveLayer!;
            Assert.NotNull(layer.Text?.PathData);
            Assert.Equal("Hello path", layer.Text!.Text);
            Assert.Null(canvas.PenPath);                         // the path is used up
            var b = layer.Text.Bounds;
            Assert.True(b.Top < 150 && b.Bottom > 120);          // it climbs along the path
            doc.Layers.History.Undo();
            Assert.DoesNotContain(doc.Layers.Scene.Layers, l => l.Text is not null);
        });
    }
}
