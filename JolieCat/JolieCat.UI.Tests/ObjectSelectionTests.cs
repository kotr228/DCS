using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class ObjectSelectionTests
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
        public void BoxAroundOneThing_SelectsJustThatThing() => Sta(() =>
        {
            var toolbox = new ToolboxViewModel();
            var doc = new DocumentViewModel(toolbox, "O", ProjectType.StandardImage, 400, 200, new SKColor(60, 120, 170));
            var bg = doc.Layers.Scene.Layers[0];
            using (var orange = new SKPaint { Color = new SKColor(240, 140, 40), IsAntialias = true })
            {
                bg.Canvas.DrawCircle(100, 100, 50, orange);     // the one we want
                bg.Canvas.DrawCircle(300, 100, 50, orange);     // another, same color
            }
            var canvas = doc.Canvas;
            SKPoint Device(float x, float y) => new((float)(x * canvas.Zoom + canvas.PanX), (float)(y * canvas.Zoom + canvas.PanY));
            toolbox.SelectTool(ToolType.ObjectSelection);
            canvas.OnPointerPressed(Device(30, 30));
            canvas.OnPointerMoved(Device(180, 170));
            Assert.NotNull(canvas.ObjectBoxDraft);
            canvas.OnPointerReleased(Device(180, 170));

            var selection = doc.Layers.Scene.Selection;
            Assert.True(selection.HasSelection);
            var b = selection.Region!.Bounds;
            Assert.InRange(b.Left, 45, 56);
            Assert.InRange(b.Right, 144, 156);
            Assert.InRange(b.Top, 45, 56);
            Assert.InRange(b.Bottom, 144, 156);
            Assert.False(selection.Region.Contains(300, 100));   // not the other one
        });
    }
}
