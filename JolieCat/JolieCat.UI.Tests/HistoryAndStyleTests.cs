using JolieCat.Core.Effects;
using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    /// <summary>History step names as the app produces them, and layer styles
    /// applied through the Layers view model.</summary>
    public class HistoryAndStyleTests
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

        private static (ToolboxViewModel Toolbox, DocumentViewModel Document) NewDocument()
        {
            var toolbox = new ToolboxViewModel();
            var document = new DocumentViewModel(toolbox, "Test");
            document.Canvas.SetViewportSize(800, 600);
            document.Layers.ActiveLayer!.Model.Canvas.Clear(SKColors.White);
            return (toolbox, document);
        }

        private static void Drag(CanvasViewModel canvas, (float X, float Y) from, (float X, float Y) to)
        {
            SKPoint Device((float X, float Y) p) => new((float)(p.X * canvas.Zoom + canvas.PanX), (float)(p.Y * canvas.Zoom + canvas.PanY));
            canvas.OnPointerPressed(Device(from));
            canvas.OnPointerMoved(Device(to));
            canvas.OnPointerReleased(Device(to));
        }

        [Fact]
        public void Steps_AreNamedAfterTheToolOrAction() => Sta(() =>
        {
            var (toolbox, document) = NewDocument();
            var history = document.Layers.History;

            toolbox.SelectTool(ToolType.Brush);
            Drag(document.Canvas, (100, 100), (200, 100));
            document.Layers.AddLayerCommand.Execute(null);
            document.Canvas.SelectAll();
            document.Canvas.FillSelection();

            Assert.Equal(new[] { "Brush", "New Layer", "Fill" }, history.UndoNames);
        });

        [Fact]
        public void LayerStyle_CommitIsOneUndoStep_AndShowsTheBadge() => Sta(() =>
        {
            var (_, document) = NewDocument();
            var layers = document.Layers;
            var item = layers.ActiveLayer!;
            var style = new LayerStyle { Stroke = true, StrokeColor = SKColors.Blue };

            layers.PreviewLayerStyle(item.Model, style);   // live preview: no history yet
            Assert.Equal(0, layers.History.UndoCount);

            layers.CommitLayerStyle(item.Model, null, style);
            Assert.True(layers.ActiveLayer!.HasStyle);
            Assert.Equal("Layer Style", layers.History.UndoNames[^1]);

            layers.History.Undo();
            Assert.Null(layers.ActiveLayer!.Model.Style);
            Assert.False(layers.ActiveLayer!.HasStyle);
        });

        [Fact]
        public void EmptyStyle_IsStoredAsNoStyle() => Sta(() =>
        {
            var (_, document) = NewDocument();
            var layer = document.Layers.ActiveLayer!.Model;

            document.Layers.CommitLayerStyle(layer, null, new LayerStyle());

            Assert.Null(layer.Style);
            Assert.Equal(0, document.Layers.History.UndoCount);
        });

        [Fact]
        public void SpotHealing_RemovesASpot() => Sta(() =>
        {
            var (toolbox, document) = NewDocument();
            var layer = document.Layers.ActiveLayer!.Model;
            using (var dot = new SKPaint { Color = SKColors.Black, IsAntialias = true })
                layer.Canvas.DrawCircle(400, 300, 6, dot);

            toolbox.SelectTool(ToolType.SpotHealing);
            if (toolbox.CurrentToolOptions is JolieCat.UI.ViewModels.ToolOptions.PaintToolOptionsViewModel options) options.Size = 30;
            Drag(document.Canvas, (395, 300), (405, 300));

            var c = layer.Bitmap.GetPixel(400, 300);
            Assert.True(c.Red > 230 && c.Green > 230 && c.Blue > 230, $"spot still there: {c}");
            Assert.Equal("Spot Healing Brush", document.Layers.History.UndoNames[^1]);
            Assert.Null(document.Canvas.SpotHealingStroke);
        });

        [Fact]
        public void ContentAwareFill_ReplacesTheSelection() => Sta(() =>
        {
            var (_, document) = NewDocument();
            var layer = document.Layers.ActiveLayer!.Model;
            using (var red = new SKPaint { Color = SKColors.Red })
                layer.Canvas.DrawRect(380, 280, 40, 40, red);

            document.Layers.Scene.Selection.SetRect(new SKRectI(375, 275, 425, 325));
            document.Canvas.ContentAwareFill();

            var c = layer.Bitmap.GetPixel(400, 300);
            Assert.True(c.Green > 230, $"object still there: {c}");
            Assert.Equal("Content-Aware Fill", document.Layers.History.UndoNames[^1]);

            document.Layers.History.Undo();
            Assert.Equal(SKColors.Red, layer.Bitmap.GetPixel(400, 300));
        });
    }
}
