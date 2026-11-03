using JolieCat.Core.Documents;
using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using JolieCat.UI.ViewModels.ToolOptions;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class ShapeLayerTests
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

        private static (DocumentViewModel Doc, ToolboxViewModel Toolbox, ShapeToolOptionsViewModel Options) Setup()
        {
            var toolbox = new ToolboxViewModel();
            var doc = new DocumentViewModel(toolbox, "T");
            doc.Canvas.Zoom = 1;
            doc.Canvas.PanX = 0;
            doc.Canvas.PanY = 0;
            toolbox.SelectTool(ToolType.Shape);
            return (doc, toolbox, (ShapeToolOptionsViewModel)toolbox.CurrentToolOptions);
        }

        private static void Drag(DocumentViewModel doc, SKPoint from, SKPoint to)
        {
            doc.Canvas.OnPointerPressed(from);
            doc.Canvas.OnPointerMoved(to);
            doc.Canvas.OnPointerReleased(to);
        }

        [Fact]
        public void DrawingAShape_MakesAnEditableShapeLayer() => Sta(() =>
        {
            var (doc, _, options) = Setup();
            options.FillColor = "#FF0000";
            options.StrokeWidth = 0;
            var background = doc.Layers.ActiveLayer!.Model;

            Drag(doc, new SKPoint(100, 100), new SKPoint(200, 160));

            var layer = doc.Layers.ActiveLayer!.Model;
            Assert.NotSame(background, layer);
            Assert.True(layer.IsShape);
            Assert.Equal(SKColors.Red, layer.Bitmap.GetPixel(150, 130));
            Assert.NotEqual(SKColors.Red, background.Bitmap.GetPixel(150, 130));   // the background wasn't painted on

            // Change the fill with the layer selected: it redraws (one undo step after it settles).
            options.FillColor = "#00FF00";
            Assert.Equal(SKColors.Lime, layer.Bitmap.GetPixel(150, 130));
            doc.Canvas.FlushTextRestyle();
            doc.Layers.History.Undo();
            Assert.Equal(SKColors.Red, layer.Bitmap.GetPixel(150, 130));
        });

        [Fact]
        public void ChangingTheKind_DoesntTurnTheSelectedShapeIntoAnother() => Sta(() =>
        {
            var (doc, _, options) = Setup();
            Drag(doc, new SKPoint(100, 100), new SKPoint(200, 200));
            var layer = doc.Layers.ActiveLayer!.Model;

            options.Kind = ShapeKind.Star;
            Assert.Equal(VectorShapeKind.Rectangle, layer.Shape!.Kind);
        });

        [Fact]
        public void MovingAShapeLayer_MovesTheShape_AndPaintingRasterizesIt() => Sta(() =>
        {
            var (doc, toolbox, options) = Setup();
            options.StrokeWidth = 0;
            options.FillColor = "#FF0000";
            Drag(doc, new SKPoint(100, 100), new SKPoint(140, 140));
            var layer = doc.Layers.ActiveLayer!.Model;

            toolbox.SelectTool(ToolType.Move);
            Drag(doc, new SKPoint(120, 120), new SKPoint(220, 120));
            Assert.Equal(new SKPoint(200, 100), layer.Shape!.Start);
            Assert.Equal(SKColors.Red, layer.Bitmap.GetPixel(220, 120));

            toolbox.SelectTool(ToolType.Brush);
            Drag(doc, new SKPoint(210, 110), new SKPoint(212, 110));
            Assert.False(doc.Layers.ActiveLayer!.Model.IsShape);   // turned into pixels first
        });

        [Fact]
        public void PixelsMode_PaintsOnTheActiveLayer() => Sta(() =>
        {
            var (doc, _, options) = Setup();
            options.Mode = ShapeMode.Pixels;
            options.FillColor = "#0000FF";
            var layer = doc.Layers.ActiveLayer!.Model;
            var count = doc.Layers.Scene.Layers.Count;

            Drag(doc, new SKPoint(100, 100), new SKPoint(200, 200));

            Assert.Equal(count, doc.Layers.Scene.Layers.Count);
            Assert.Equal(SKColors.Blue, layer.Bitmap.GetPixel(150, 150));
        });

        [Fact]
        public void ExportLayersToFiles_WritesEachVisibleLayer_Trimmed() => Sta(() =>
        {
            var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "JolieCatLayers_" + Guid.NewGuid().ToString("N"));
            try
            {
                var main = new MainViewModel();
                main.Layers.ActiveLayer!.Model.Canvas.Clear(SKColors.White);
                main.Layers.AddShapeLayer(new ShapeContent(VectorShapeKind.Ellipse, new SKPoint(10, 10), new SKPoint(60, 40),
                    SKColors.Red, SKColors.Black, 0));
                main.Layers.AddLayerCommand.Execute(null);
                main.Layers.ActiveLayer!.Model.IsVisible = false;   // hidden: skipped

                var files = main.ExportLayers(folder, "PNG", trimToContent: true, prefix: "t_");

                Assert.Equal(2, files.Count);
                using var ellipse = SKBitmap.Decode(files.Single(f => f.Contains("Ellipse")));
                Assert.InRange(ellipse.Width, 50, 52);       // trimmed to the shape
                Assert.InRange(ellipse.Height, 30, 32);
                Assert.All(files, f => Assert.StartsWith("t_", System.IO.Path.GetFileName(f)));
            }
            finally
            {
                try { System.IO.Directory.Delete(folder, true); } catch (System.IO.IOException) { }
            }
        });

        [Fact]
        public void AFillLayer_IsMaskedToTheSelection_AndPaintingRasterizesIt() => Sta(() =>
        {
            var (doc, toolbox, _) = Setup();
            doc.Layers.Scene.Selection.SetRect(new SKRectI(50, 50, 150, 150));

            doc.Layers.AddFillLayer(new FillContent(FillKind.SolidColor, SKColors.Purple, SKColors.Black));
            var layer = doc.Layers.ActiveLayer!.Model;
            Assert.True(layer.IsFill);
            Assert.Equal(SKColors.Purple, layer.Bitmap.GetPixel(10, 10));          // the fill covers everything...
            Assert.Equal(SKColors.Black, layer.Mask!.Bitmap.GetPixel(10, 10));     // ...but shows only in the selection
            Assert.Equal(SKColors.White, layer.Mask.Bitmap.GetPixel(100, 100));

            doc.Layers.Scene.Selection.Clear();
            toolbox.SelectTool(ToolType.Brush);
            Drag(doc, new SKPoint(100, 100), new SKPoint(110, 100));
            Assert.False(doc.Layers.ActiveLayer!.Model.IsFill);                     // painted on: now pixels
        });

        [Fact]
        public void Shapes_AreSavedInProjects()
        {
            var star = new ShapeContent(VectorShapeKind.Star, new SKPoint(10, 20), new SKPoint(110, 120),
                SKColors.Gold, SKColors.Navy, 3, 8, 7, false);
            Assert.Equal(star, ShapeContent.Deserialize(star.Serialize()));
            Assert.Null(ShapeContent.Deserialize("nonsense"));
        }
    }
}
