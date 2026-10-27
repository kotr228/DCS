using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class ColorRangeTests
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
        public void ColorRange_SelectsEveryMatchingArea_NotJustConnectedOnes() => Sta(() =>
        {
            var document = new DocumentViewModel(new ToolboxViewModel(), "Test");
            var layer = document.Layers.ActiveLayer!.Model;
            layer.Canvas.Clear(SKColors.White);
            using (var red = new SKPaint { Color = new SKColor(220, 20, 20) })
            {
                layer.Canvas.DrawRect(10, 10, 30, 30, red);
                layer.Canvas.DrawRect(200, 100, 30, 30, red);
            }

            document.Canvas.SelectColorRange(new SKColor(230, 30, 25), 40);

            var selection = document.Layers.Scene.Selection;
            Assert.True(selection.HasSelection);
            Assert.True(selection.Contains(20, 20));
            Assert.True(selection.Contains(210, 110));
            Assert.False(selection.Contains(100, 100));
        });

        [Fact]
        public void ColorRange_WithNoMatch_ClearsTheSelection() => Sta(() =>
        {
            var document = new DocumentViewModel(new ToolboxViewModel(), "Test");
            document.Layers.ActiveLayer!.Model.Canvas.Clear(SKColors.White);
            document.Layers.Scene.Selection.SetRect(new SKRectI(0, 0, 10, 10));

            document.Canvas.SelectColorRange(SKColors.Blue, 10);

            Assert.False(document.Layers.Scene.Selection.HasSelection);
        });
    }
}
