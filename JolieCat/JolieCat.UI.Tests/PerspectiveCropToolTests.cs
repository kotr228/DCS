using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class PerspectiveCropToolTests
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

        private static void Drag(CanvasViewModel canvas, SKPoint from, SKPoint to)
        {
            canvas.OnPointerPressed(from);
            canvas.OnPointerMoved(to);
            canvas.OnPointerReleased(to);
        }

        [Fact]
        public void DraggingTheCorners_ThenEnter_StraightensAndCrops() => Sta(() =>
        {
            var toolbox = new ToolboxViewModel();
            var document = new DocumentViewModel(toolbox, "T");
            var canvas = document.Canvas;
            canvas.Zoom = 1;
            canvas.PanX = 0;
            canvas.PanY = 0;
            int w = document.Layers.DocumentWidth, h = document.Layers.DocumentHeight;

            toolbox.SelectTool(ToolType.PerspectiveCrop);
            Assert.NotNull(canvas.PerspectiveQuad);
            Assert.Equal(new SKPoint(w, h), canvas.PerspectiveQuad![2]);

            // Pull the corners in to a 200 x 100 area (slightly skewed at the top right).
            Drag(canvas, new SKPoint(0, 0), new SKPoint(100, 100));
            Drag(canvas, new SKPoint(w, 0), new SKPoint(300, 90));
            Drag(canvas, new SKPoint(w, h), new SKPoint(300, 200));
            Drag(canvas, new SKPoint(0, h), new SKPoint(100, 200));

            canvas.CommitPerspectiveCrop();
            Assert.Equal(200, document.Layers.DocumentWidth);
            Assert.InRange(document.Layers.DocumentHeight, 100, 106);

            // One undo step brings the whole document back.
            document.Layers.History.Undo();
            Assert.Equal(w, document.Layers.DocumentWidth);
            Assert.Equal(h, document.Layers.DocumentHeight);
        });

        [Fact]
        public void CrossedCorners_AreRefused()
        {
            Assert.True(CanvasViewModel.IsUsableQuad(new[] { new SKPoint(0, 0), new SKPoint(10, 0), new SKPoint(10, 10), new SKPoint(0, 10) }));
            Assert.False(CanvasViewModel.IsUsableQuad(new[] { new SKPoint(0, 0), new SKPoint(10, 10), new SKPoint(10, 0), new SKPoint(0, 10) }));
            Assert.False(CanvasViewModel.IsUsableQuad(new[] { new SKPoint(0, 0), new SKPoint(5, 0), new SKPoint(10, 0), new SKPoint(15, 0) }));
        }
    }
}
