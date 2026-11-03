using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class DocumentPanelsTests
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
        public void InfoHistogramAndNavigator_ReadTheActiveDocument() => Sta(() =>
        {
            var doc = new DocumentViewModel(new ToolboxViewModel(), "T");
            var layer = doc.Layers.ActiveLayer!.Model;
            layer.Canvas.Clear(SKColors.Black);
            using (var red = new SKPaint { Color = new SKColor(200, 10, 20) })
                layer.Canvas.DrawRect(0, 0, layer.Bitmap.Width / 2f, layer.Bitmap.Height, red);
            doc.Layers.NotifyContentChanged();
            doc.Canvas.SetViewportSize(800, 600);

            var panels = new DocumentPanelsViewModel(() => doc, startTimer: false);

            // Info: the color under the pointer.
            doc.Canvas.Zoom = 1;
            doc.Canvas.PanX = 0;
            doc.Canvas.PanY = 0;
            doc.Canvas.OnPointerMoved(new SKPoint(10, 10));
            panels.Refresh();
            Assert.Equal("#C80A14", panels.HexText);
            Assert.Contains("X 10", panels.PositionText);
            Assert.Contains("×", panels.DocumentText);

            // Histogram: half the pixels are black (luma 0), half red-ish.
            Assert.Contains("Pixels", panels.StatsText);
            Assert.StartsWith("0,100 0,0", panels.LumaPoints);   // the black column is the tallest

            // Navigator: clicking the thumbnail's middle centers the view there.
            Assert.True(panels.ThumbnailWidth is > 0 and <= DocumentPanelsViewModel.ThumbnailSize);
            panels.NavigateTo(panels.ThumbnailWidth / 2, panels.ThumbnailHeight / 2);
            var visible = doc.Canvas.VisibleDocumentArea;
            Assert.InRange(visible.MidX, doc.Layers.DocumentWidth / 2f - 2, doc.Layers.DocumentWidth / 2f + 2);
            Assert.True(panels.ViewBox.Width > 0);
        });
    }
}
