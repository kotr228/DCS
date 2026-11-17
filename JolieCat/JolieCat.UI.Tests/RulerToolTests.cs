using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using JolieCat.UI.ViewModels.ToolOptions;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class RulerToolTests
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
        public void Ruler_Measures_AndStraightensATiltedLine() => Sta(() =>
        {
            var toolbox = new ToolboxViewModel();
            var doc = new DocumentViewModel(toolbox, "R", ProjectType.StandardImage, 200, 200, null);
            var canvas = doc.Canvas;
            SKPoint Device(float x, float y) => new((float)(x * canvas.Zoom + canvas.PanX), (float)(y * canvas.Zoom + canvas.PanY));
            toolbox.SelectTool(ToolType.Ruler);
            var options = (RulerToolOptionsViewModel)toolbox.CurrentToolOptions;

            canvas.OnPointerPressed(Device(20, 100));
            canvas.OnPointerMoved(Device(50, 60));
            canvas.OnPointerReleased(Device(50, 60));
            Assert.Equal("50.0 px", options.LengthText);         // 30 across, 40 up
            Assert.Equal("53.1°", options.AngleText);
            Assert.True(options.HasLine);

            // A line tilted 10° down to the right, drawn along a red bar tilted the same way.
            var layer = doc.Layers.Scene.Layers[0];
            layer.Canvas.Clear(SKColors.Transparent);
            using (var red = new SKPaint { Color = SKColors.Red })
            {
                layer.Canvas.Save();
                layer.Canvas.RotateDegrees(10, 100, 100);
                layer.Canvas.DrawRect(20, 98, 160, 4, red);
                layer.Canvas.Restore();
            }
            var a = 10 * MathF.PI / 180;
            canvas.SetRulerLine(new SKPoint(100 - 80 * MathF.Cos(a), 100 - 80 * MathF.Sin(a)), new SKPoint(100 + 80 * MathF.Cos(a), 100 + 80 * MathF.Sin(a)));
            var turn = canvas.StraightenLayerToRuler();
            Assert.Equal(-10, turn!.Value, 1);
            var bitmap = doc.Layers.Scene.Layers[0].Bitmap;
            Assert.Equal(SKColors.Red, bitmap.GetPixel(30, 100));            // level now
            Assert.Equal(SKColors.Red, bitmap.GetPixel(170, 100));
            Assert.Null(canvas.RulerLine);

            doc.Layers.History.Undo();
            Assert.Equal(0, doc.Layers.Scene.Layers[0].Bitmap.GetPixel(30, 100).Alpha);
        });
    }
}
