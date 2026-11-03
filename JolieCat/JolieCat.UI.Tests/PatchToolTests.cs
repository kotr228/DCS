using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class PatchToolTests
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

        /// <summary>Zoom 1, no pan: device pixels = document pixels. A warm gray image
        /// with a dark blemish at 100..120.</summary>
        private static (DocumentViewModel Document, ToolboxViewModel Toolbox) Setup()
        {
            var toolbox = new ToolboxViewModel();
            var document = new DocumentViewModel(toolbox, "T");
            document.Canvas.Zoom = 1;
            document.Canvas.PanX = 0;
            document.Canvas.PanY = 0;
            var layer = document.Layers.ActiveLayer!.Model;
            layer.Canvas.Clear(new SKColor(150, 140, 130));
            using (var dark = new SKPaint { Color = new SKColor(30, 25, 20) })
                layer.Canvas.DrawRect(100, 100, 20, 20, dark);
            toolbox.SelectTool(ToolType.Patch);
            return (document, toolbox);
        }

        [Fact]
        public void DraggingTheSelection_ReplacesTheBlemish_WithHealedTexture() => Sta(() =>
        {
            var (document, _) = Setup();
            var layer = document.Layers.ActiveLayer!.Model;
            document.Layers.Scene.Selection.SetRect(new SKRectI(95, 95, 125, 125));

            document.Canvas.OnPointerPressed(new SKPoint(110, 110));
            document.Canvas.OnPointerMoved(new SKPoint(170, 110));
            document.Canvas.OnPointerReleased(new SKPoint(170, 110));

            var fixedPixel = layer.Bitmap.GetPixel(110, 110);
            Assert.InRange(fixedPixel.Red, 140, 160);
            Assert.InRange(fixedPixel.Blue, 120, 140);

            // One undo step brings the blemish back.
            document.Layers.History.Undo();
            Assert.Equal(new SKColor(30, 25, 20), layer.Bitmap.GetPixel(110, 110));
        });

        private static void Drag(DocumentViewModel document, SKPoint from, SKPoint to)
        {
            document.Canvas.OnPointerPressed(from);
            document.Canvas.OnPointerMoved(to);
            document.Canvas.OnPointerReleased(to);
        }

        private static readonly SKColor Blemish = new(30, 25, 20);
        private static readonly SKColor Ground = new(150, 140, 130);

        private static bool IsDark(SKColor c) => c.Red < 70;

        [Fact]
        public void DestinationMode_CopiesTheAreaToTheDropPlace() => Sta(() =>
        {
            var (document, toolbox) = Setup();
            ((JolieCat.UI.ViewModels.ToolOptions.PatchToolOptionsViewModel)toolbox.CurrentToolOptions).Mode =
                JolieCat.UI.ViewModels.ToolOptions.PatchMode.Destination;
            var layer = document.Layers.ActiveLayer!.Model;
            document.Layers.Scene.Selection.SetRect(new SKRectI(95, 95, 125, 125));

            Drag(document, new SKPoint(110, 110), new SKPoint(210, 110));

            Assert.True(IsDark(layer.Bitmap.GetPixel(210, 110)), $"{layer.Bitmap.GetPixel(210, 110)}"); // the copy
            Assert.Equal(Blemish, layer.Bitmap.GetPixel(110, 110));                                    // the original stays
            Assert.True(document.Layers.Scene.Selection.Contains(210, 110));                            // selection followed
        });

        [Fact]
        public void ContentAwareMove_MovesTheObject_AndFillsWhereItWas() => Sta(() =>
        {
            var (document, toolbox) = Setup();
            toolbox.SelectTool(ToolType.ContentAwareMove);
            var layer = document.Layers.ActiveLayer!.Model;
            document.Layers.Scene.Selection.SetRect(new SKRectI(95, 95, 125, 125));

            Drag(document, new SKPoint(110, 110), new SKPoint(210, 150));

            Assert.True(IsDark(layer.Bitmap.GetPixel(210, 150)), $"{layer.Bitmap.GetPixel(210, 150)}");
            var old = layer.Bitmap.GetPixel(110, 110);
            Assert.InRange(old.Red, 135, 165);   // filled with the surrounding ground
            Assert.Equal("Content-Aware Move", document.Layers.History.UndoNames[^1]);
        });

        [Fact]
        public void ContentAwareMove_Extend_KeepsTheOriginal() => Sta(() =>
        {
            var (document, toolbox) = Setup();
            toolbox.SelectTool(ToolType.ContentAwareMove);
            ((JolieCat.UI.ViewModels.ToolOptions.ContentAwareMoveToolOptionsViewModel)toolbox.CurrentToolOptions).Mode =
                JolieCat.UI.ViewModels.ToolOptions.ContentAwareMoveMode.Extend;
            var layer = document.Layers.ActiveLayer!.Model;
            document.Layers.Scene.Selection.SetRect(new SKRectI(95, 95, 125, 125));

            Drag(document, new SKPoint(110, 110), new SKPoint(210, 150));

            Assert.True(IsDark(layer.Bitmap.GetPixel(210, 150)));
            Assert.Equal(Blemish, layer.Bitmap.GetPixel(110, 110));
            Assert.Equal(Ground, layer.Bitmap.GetPixel(300, 300));
        });

        [Fact]
        public void PressingOutsideASelection_DrawsOneFreehand() => Sta(() =>
        {
            var (document, _) = Setup();

            document.Canvas.OnPointerPressed(new SKPoint(90, 90));
            document.Canvas.OnPointerMoved(new SKPoint(130, 90));
            document.Canvas.OnPointerMoved(new SKPoint(130, 130));
            document.Canvas.OnPointerMoved(new SKPoint(90, 130));
            document.Canvas.OnPointerReleased(new SKPoint(90, 130));

            var selection = document.Layers.Scene.Selection;
            Assert.True(selection.HasSelection);
            Assert.True(selection.Contains(110, 110));
            Assert.Equal(new SKColor(30, 25, 20), document.Layers.ActiveLayer!.Model.Bitmap.GetPixel(110, 110)); // nothing patched yet
        });
    }
}
