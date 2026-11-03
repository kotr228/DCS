using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class QuickMaskTests
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
        public void PaintingInQuickMask_ChangesTheSelection_NotTheLayer() => Sta(() =>
        {
            var toolbox = new ToolboxViewModel();
            var doc = new DocumentViewModel(toolbox, "T");
            var canvas = doc.Canvas;
            canvas.Zoom = 1;
            canvas.PanX = 0;
            canvas.PanY = 0;
            var layer = doc.Layers.ActiveLayer!.Model;
            layer.Canvas.Clear(SKColors.White);
            doc.Layers.Scene.Selection.SetRect(new SKRectI(0, 0, 200, 200));

            canvas.ToggleQuickMask();
            Assert.True(canvas.IsQuickMask);
            Assert.False(doc.Layers.Scene.Selection.HasSelection);
            Assert.Equal(SKColors.White, canvas.QuickMaskBitmap!.GetPixel(100, 100));
            Assert.Equal(SKColors.Black, canvas.QuickMaskBitmap!.GetPixel(300, 300));

            // Fill a second area white (selects it) - through the normal Fill command.
            canvas.ResetColors();
            canvas.SwapColors();                                   // foreground white
            canvas.FillSelection();                                // no selection: whole mask
            Assert.Equal(SKColors.White, canvas.QuickMaskBitmap!.GetPixel(300, 300));
            Assert.Equal(SKColors.White, layer.Bitmap.GetPixel(300, 300));   // the layer wasn't touched

            // Undo that fill, then leave Quick Mask: back to the first area only.
            doc.Layers.History.Undo();
            canvas.ToggleQuickMask();
            Assert.False(canvas.IsQuickMask);
            var selection = doc.Layers.Scene.Selection;
            Assert.True(selection.Contains(100, 100));
            Assert.False(selection.Contains(300, 300));
            Assert.Null(layer.PaintRedirect);
        });

        [Fact]
        public void BrushStrokesInQuickMask_PaintTheMask() => Sta(() =>
        {
            var toolbox = new ToolboxViewModel();
            var doc = new DocumentViewModel(toolbox, "T");
            var canvas = doc.Canvas;
            canvas.Zoom = 1;
            canvas.PanX = 0;
            canvas.PanY = 0;
            var layer = doc.Layers.ActiveLayer!.Model;
            layer.Canvas.Clear(SKColors.White);

            canvas.ToggleQuickMask();                              // no selection: all clear
            canvas.ResetColors();                                  // foreground black
            toolbox.SelectTool(ToolType.Brush);
            canvas.OnPointerPressed(new SKPoint(50, 50));
            canvas.OnPointerMoved(new SKPoint(150, 50));
            canvas.OnPointerReleased(new SKPoint(150, 50));

            Assert.True(canvas.QuickMaskBitmap!.GetPixel(100, 50).Red < 60);
            Assert.Equal(SKColors.White, layer.Bitmap.GetPixel(100, 50));

            canvas.ToggleQuickMask();
            var selection = doc.Layers.Scene.Selection;
            Assert.True(selection.HasSelection);
            Assert.False(selection.Contains(100, 50));             // painted black: left out
            Assert.True(selection.Contains(400, 400));
        });
    }
}
