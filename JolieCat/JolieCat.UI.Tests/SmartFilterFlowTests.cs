using JolieCat.Core.Actions;
using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class SmartFilterFlowTests
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

        private static ActionStep Invert => new() { Kind = "Filter", Command = "Invert", Title = "Invert" };

        private static ActionStep Levels(int inWhite) =>
            new ActionStep { Kind = "Levels", Command = "Levels", Title = "Levels" }.With("InWhite", inWhite).With("Gamma", 1);

        private static (DocumentViewModel Doc, ToolboxViewModel Toolbox, Core.Documents.Layer Layer) Setup()
        {
            var toolbox = new ToolboxViewModel();
            var document = new DocumentViewModel(toolbox, "T");
            var layer = document.Layers.ActiveLayer!.Model;
            layer.Canvas.Clear(new SKColor(100, 100, 100));
            document.Layers.ConvertForSmartFiltersCommand.Execute(null);
            return (document, toolbox, document.Layers.ActiveLayer!.Model);
        }

        [Fact]
        public void FiltersOnAConvertedLayer_StayEditable() => Sta(() =>
        {
            var (doc, _, layer) = Setup();
            Assert.True(layer.HasSmartFilters);

            Assert.True(MainViewModel.PlayActionStep(Invert, doc));
            Assert.True(MainViewModel.PlayActionStep(Levels(200), doc));
            Assert.Equal(2, layer.SmartFilters.Count);
            // 100 inverted = 155, then levels (white 200) = ~198.
            Assert.InRange(layer.Bitmap.GetPixel(1, 1).Red, 195, 200);

            var panel = new SmartFiltersViewModel(layer, doc.Canvas, (_, _) => false);
            panel.Filters[0].Enabled = false;                     // hide Invert
            Assert.InRange(layer.Bitmap.GetPixel(1, 1).Red, 126, 129);  // just levels on 100

            panel.Selected = panel.Filters[1];
            panel.MoveUpCommand.Execute(null);                    // Levels first now
            Assert.Equal("Levels", layer.SmartFilters[0].Title);

            panel.Selected = panel.Filters[0];
            panel.DeleteCommand.Execute(null);
            Assert.Single(layer.SmartFilters);

            // Every change was one undo step: undo the delete.
            doc.Layers.History.Undo();
            Assert.Equal(2, layer.SmartFilters.Count);
            Assert.Equal(new SKColor(100, 100, 100), layer.SmartFilterSource!.GetPixel(1, 1));
        });

        [Fact]
        public void PaintingIsBlocked_UntilTheLayerIsRasterized() => Sta(() =>
        {
            var (doc, toolbox, _) = Setup();
            MainViewModel.PlayActionStep(Invert, doc);
            doc.Canvas.Zoom = 1;
            toolbox.SelectTool(ToolType.Brush);

            doc.Canvas.OnPointerPressed(new SKPoint(5, 5));
            doc.Canvas.OnPointerReleased(new SKPoint(5, 5));
            Assert.Contains("Smart Filters", doc.Canvas.ToolHintText);
            Assert.Empty(doc.Layers.History.UndoNames.Where(n => n == "Brush"));

            doc.Layers.RasterizeSmartFiltersCommand.Execute(null);
            var layer = doc.Layers.ActiveLayer!.Model;
            Assert.False(layer.HasSmartFilters);
            Assert.Equal(new SKColor(155, 155, 155), layer.Bitmap.GetPixel(40, 40)); // keeps the filtered look
        });

        [Fact]
        public void MovingASmartFilterLayer_MovesItsOriginal_AndUndoBringsItBack() => Sta(() =>
        {
            var toolbox = new ToolboxViewModel();
            var doc = new DocumentViewModel(toolbox, "T");
            var layer = doc.Layers.ActiveLayer!.Model;
            layer.Canvas.Clear(SKColors.Transparent);
            using (var red = new SKPaint { Color = SKColors.Red })
                layer.Canvas.DrawRect(10, 10, 20, 20, red);
            doc.Layers.ConvertForSmartFiltersCommand.Execute(null);
            layer = doc.Layers.ActiveLayer!.Model;
            MainViewModel.PlayActionStep(Invert, doc);                 // red -> cyan
            doc.Canvas.Zoom = 1;
            doc.Canvas.PanX = 0;
            doc.Canvas.PanY = 0;
            toolbox.SelectTool(ToolType.Move);

            doc.Canvas.OnPointerPressed(new SKPoint(15, 15));
            doc.Canvas.OnPointerMoved(new SKPoint(115, 15));
            doc.Canvas.OnPointerReleased(new SKPoint(115, 15));

            Assert.Equal(SKColors.Red, layer.SmartFilterSource!.GetPixel(115, 15));
            Assert.Equal(0, layer.SmartFilterSource!.GetPixel(15, 15).Alpha);
            Assert.Equal(SKColors.Cyan, layer.Bitmap.GetPixel(115, 15));

            doc.Layers.History.Undo();
            Assert.Equal(SKColors.Red, layer.SmartFilterSource!.GetPixel(15, 15));
            Assert.Equal(SKColors.Cyan, layer.Bitmap.GetPixel(15, 15));
            Assert.Equal(0, layer.Bitmap.GetPixel(115, 15).Alpha);
        });

        [Fact]
        public void FreeTransform_OnASmartFilterLayer_TransformsTheOriginal() => Sta(() =>
        {
            var toolbox = new ToolboxViewModel();
            var doc = new DocumentViewModel(toolbox, "T");
            var layer = doc.Layers.ActiveLayer!.Model;
            layer.Canvas.Clear(SKColors.Transparent);
            using (var red = new SKPaint { Color = SKColors.Red })
                layer.Canvas.DrawRect(10, 10, 20, 20, red);
            doc.Layers.ConvertForSmartFiltersCommand.Execute(null);
            layer = doc.Layers.ActiveLayer!.Model;
            MainViewModel.PlayActionStep(Invert, doc);
            doc.Canvas.Zoom = 1;
            doc.Canvas.PanX = 0;
            doc.Canvas.PanY = 0;

            toolbox.SelectTool(ToolType.FreeTransform);
            doc.Canvas.OnPointerPressed(new SKPoint(400, 300));   // inside the box: moves it
            doc.Canvas.OnPointerMoved(new SKPoint(450, 300));
            doc.Canvas.OnPointerReleased(new SKPoint(450, 300));
            doc.Canvas.CommitTransform();

            Assert.Equal(SKColors.Red, layer.SmartFilterSource!.GetPixel(65, 15));
            Assert.Equal(SKColors.Cyan, layer.Bitmap.GetPixel(65, 15));
            Assert.Equal(0, layer.Bitmap.GetPixel(15, 15).Alpha);

            doc.Layers.History.Undo();
            Assert.Equal(SKColors.Red, layer.SmartFilterSource!.GetPixel(15, 15));
        });

        [Fact]
        public void ConvertingAndRasterizing_AreUndoable() => Sta(() =>
        {
            var (doc, _, _) = Setup();
            doc.Layers.History.Undo();
            Assert.False(doc.Layers.ActiveLayer!.Model.HasSmartFilters);
            doc.Layers.History.Redo();
            Assert.True(doc.Layers.ActiveLayer!.Model.HasSmartFilters);
        });
    }
}
