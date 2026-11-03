using JolieCat.Core.Documents;
using JolieCat.Shared.Enums;
using JolieCat.UI.Rendering;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class GuidesTests
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

        private static ToolboxViewModel _toolbox = null!;

        /// <summary>Zoom 1, no pan: device pixels = document pixels.</summary>
        private static DocumentViewModel Document()
        {
            _toolbox = new ToolboxViewModel();
            var document = new DocumentViewModel(_toolbox, "T");
            document.Canvas.Zoom = 1;
            document.Canvas.PanX = 0;
            document.Canvas.PanY = 0;
            return document;
        }

        private static void Select(DocumentViewModel document, ToolType type) => _toolbox.SelectTool(type);

        [Fact]
        public void Snap_PullsOntoNearbyGuides_GridAndEdges() => Sta(() =>
        {
            var canvas = Document().Canvas;
            canvas.AddGuide(isVertical: true, 300);
            canvas.AddGuide(isVertical: false, 120);

            Assert.Equal(new SKPoint(300, 120), canvas.Snap(new SKPoint(305, 115)));
            Assert.Equal(new SKPoint(330, 160), canvas.Snap(new SKPoint(330, 160)));   // too far
            Assert.Equal(0, canvas.Snap(new SKPoint(4, 500)).X);                        // document edge

            canvas.ShowGrid = true;
            canvas.GridSize = 50;
            Assert.Equal(new SKPoint(450, 400), canvas.Snap(new SKPoint(447, 403)));

            canvas.SnapToGuides = false;
            Assert.Equal(new SKPoint(305, 115), canvas.Snap(new SKPoint(305, 115)));
        });

        [Fact]
        public void DraggingFromTheRuler_MakesAGuide_AndDroppingOnItRemovesIt() => Sta(() =>
        {
            var document = Document();
            var canvas = document.Canvas;
            canvas.ShowRulers = true;

            // From the left ruler: a vertical guide where the pointer is let go.
            canvas.OnPointerPressed(new SKPoint(10, 300));
            canvas.OnPointerMoved(new SKPoint(200.4f, 300));
            canvas.OnPointerReleased(new SKPoint(200.4f, 300));
            Assert.Equal(new Guide(true, 200), Assert.Single(canvas.Guides));

            // Move tool: drag that guide back onto the ruler -> gone.
            Select(document, ToolType.Move);
            canvas.OnPointerPressed(new SKPoint(201, 500));
            canvas.OnPointerMoved(new SKPoint(5, 500));
            canvas.OnPointerReleased(new SKPoint(5, 500));
            Assert.Empty(canvas.Guides);
        });

        [Fact]
        public void MarqueeSnapsToGuides() => Sta(() =>
        {
            var document = Document();
            var canvas = document.Canvas;
            canvas.AddGuide(true, 100);
            canvas.AddGuide(false, 100);
            Select(document, ToolType.RectangularMarquee);

            canvas.OnPointerPressed(new SKPoint(104, 96));
            canvas.OnPointerMoved(new SKPoint(300, 250));
            canvas.OnPointerReleased(new SKPoint(300, 250));

            var bounds = document.Layers.Scene.Selection.Path!.Bounds;
            Assert.Equal(100, bounds.Left, 1);
            Assert.Equal(100, bounds.Top, 1);
        });

        [Fact]
        public void MoveTool_SnapsTheLayersEdgeToAGuide() => Sta(() =>
        {
            var document = Document();
            var canvas = document.Canvas;
            var layer = document.Layers.ActiveLayer!.Model;
            layer.Canvas.Clear(SKColors.Transparent);
            using (var red = new SKPaint { Color = SKColors.Red })
                layer.Canvas.DrawRect(10, 10, 20, 20, red);   // x 10..30
            canvas.AddGuide(isVertical: true, 100);
            Select(document, ToolType.Move);

            // Dragged 73 px right: the right edge (30 -> 103) is 3 px from the guide.
            canvas.OnPointerPressed(new SKPoint(20, 20));
            canvas.OnPointerMoved(new SKPoint(93, 20));
            canvas.OnPointerReleased(new SKPoint(93, 20));

            Assert.Equal(SKColors.Red, layer.Bitmap.GetPixel(99, 15));
            Assert.Equal(0, layer.Bitmap.GetPixel(100, 15).Alpha);
            Assert.Equal(0, layer.Bitmap.GetPixel(79, 15).Alpha);
        });

        [Fact]
        public void MoveTool_WithoutGuidesOrGrid_IsNotPulled() => Sta(() =>
        {
            var document = Document();
            var layer = document.Layers.ActiveLayer!.Model;
            layer.Canvas.Clear(SKColors.Transparent);
            using (var red = new SKPaint { Color = SKColors.Red })
                layer.Canvas.DrawRect(10, 10, 20, 20, red);
            Select(document, ToolType.Move);

            // 3 px from the document's left edge would snap if snapping were on.
            document.Canvas.OnPointerPressed(new SKPoint(20, 20));
            document.Canvas.OnPointerMoved(new SKPoint(13, 20));
            document.Canvas.OnPointerReleased(new SKPoint(13, 20));

            Assert.Equal(SKColors.Red, layer.Bitmap.GetPixel(3, 15));
            Assert.Equal(0, layer.Bitmap.GetPixel(2, 15).Alpha);
        });

        [Fact]
        public void RotatingTheCanvas_TurnsTheGuides_AndUndoPutsThemBack() => Sta(() =>
        {
            var document = Document();
            var w = document.Layers.DocumentWidth;
            document.Canvas.AddGuide(isVertical: true, 100);

            document.Layers.Rotate90CounterClockwiseCommand.Execute(null);
            Assert.Equal(new Guide(false, w - 100), Assert.Single(document.Canvas.Guides));

            document.Layers.History.Undo();
            Assert.Equal(new Guide(true, 100), Assert.Single(document.Canvas.Guides));
            document.Layers.History.Redo();
            Assert.Equal(new Guide(false, w - 100), Assert.Single(document.Canvas.Guides));
        });

        [Fact]
        public void RulerSteps_AreRoundNumbers()
        {
            Assert.Equal(100, CanvasRenderer.RulerStep(1));
            Assert.Equal(200, CanvasRenderer.RulerStep(0.5));
            Assert.Equal(10, CanvasRenderer.RulerStep(8));
            Assert.Equal(1, CanvasRenderer.RulerStep(200));
        }
    }
}
