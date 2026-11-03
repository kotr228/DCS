using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using JolieCat.UI.ViewModels.ToolOptions;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    /// <summary>
    /// End-to-end tool tests: each builds a document, fits it into an 800×600
    /// viewport and then presses/moves/releases the pointer at document positions,
    /// exactly as CanvasView forwards mouse input. Run on an STA thread because WPF's
    /// keyboard state (used for Alt/Shift modifiers) requires one.
    /// </summary>
    public class CanvasToolTests
    {
        private sealed class Harness
        {
            public ToolboxViewModel Toolbox { get; } = new();
            public DocumentViewModel Document { get; }
            public CanvasViewModel Canvas => Document.Canvas;
            public Core.Documents.Layer Layer => Document.Layers.ActiveLayer!.Model;

            public Harness()
            {
                Document = new DocumentViewModel(Toolbox, "Test");
                Canvas.SetViewportSize(800, 600);
                Layer.Canvas.Clear(SKColors.White);
            }

            public void Use(ToolType tool) => Toolbox.SelectTool(tool);

            public SKPoint Device(float docX, float docY) =>
                new((float)(docX * Canvas.Zoom + Canvas.PanX), (float)(docY * Canvas.Zoom + Canvas.PanY));

            public void Drag(params (float X, float Y)[] points)
            {
                Canvas.OnPointerPressed(Device(points[0].X, points[0].Y));
                foreach (var p in points.Skip(1)) Canvas.OnPointerMoved(Device(p.X, p.Y));
                var last = points[^1];
                Canvas.OnPointerReleased(Device(last.X, last.Y));
            }

            public SKColor Pixel(int x, int y) => Layer.Bitmap.GetPixel(x, y);
        }

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
        public void NewDocument_IsFittedAndCentered() => Sta(() =>
        {
            var h = new Harness();
            var docLeft = h.Canvas.PanX;
            var docRight = h.Canvas.PanX + h.Document.Layers.DocumentWidth * h.Canvas.Zoom;

            Assert.InRange(docLeft, 1, 799);
            Assert.InRange(Math.Abs(docLeft - (800 - docRight)), 0, 2); // centered horizontally
        });

        [Fact]
        public void BrushStroke_PaintsAndUndoes() => Sta(() =>
        {
            var h = new Harness();
            h.Use(ToolType.Brush);
            h.Canvas.PrimaryColor = System.Windows.Media.Colors.Red;

            h.Drag((100, 100), (300, 100));

            Assert.Equal(255, h.Pixel(200, 100).Red);
            Assert.True(h.Pixel(200, 100).Green < 50);

            h.Document.Layers.History.Undo();
            Assert.Equal(SKColors.White, h.Pixel(200, 100));
        });

        [Fact]
        public void PaintThenAddLayer_StrokeIsStillUndoable() => Sta(() =>
        {
            var h = new Harness();
            h.Use(ToolType.Brush);
            h.Canvas.PrimaryColor = System.Windows.Media.Colors.Black;
            h.Drag((100, 100), (200, 100));
            var background = h.Layer;

            h.Document.Layers.AddLayerCommand.Execute(null);
            Assert.Equal(2, h.Document.Layers.Scene.Layers.Count);

            h.Document.Layers.History.Undo(); // the new layer
            h.Document.Layers.History.Undo(); // the stroke

            Assert.Single(h.Document.Layers.Scene.Layers);
            Assert.Equal(SKColors.White, h.Document.Layers.Scene.Layers[0].Bitmap.GetPixel(150, 100));
        });

        [Fact]
        public void ToolOptions_SurviveSwitchingTools() => Sta(() =>
        {
            var h = new Harness();
            h.Use(ToolType.Brush);
            ((PaintToolOptionsViewModel)h.Toolbox.CurrentToolOptions).Size = 77;

            h.Use(ToolType.Eraser);
            h.Use(ToolType.Brush);

            Assert.Equal(77, ((PaintToolOptionsViewModel)h.Toolbox.CurrentToolOptions).Size);
        });

        [Fact]
        public void ShortcutLetters_WorkLikePhotoshop() => Sta(() =>
        {
            var toolbox = new ToolboxViewModel(); // starts on Rectangular Marquee

            toolbox.SelectToolByShortcut("M");    // already in the group: stays
            Assert.Equal(ToolType.RectangularMarquee, toolbox.ActiveTool.Type);

            toolbox.SelectToolByShortcut("M", cycle: true); // Shift+M
            Assert.Equal(ToolType.EllipticalMarquee, toolbox.ActiveTool.Type);

            toolbox.SelectToolByShortcut("B");
            Assert.Equal(ToolType.Brush, toolbox.ActiveTool.Type);

            toolbox.SelectToolByShortcut("M");    // back to the last-used marquee
            Assert.Equal(ToolType.EllipticalMarquee, toolbox.ActiveTool.Type);

            Assert.False(toolbox.SelectToolByShortcut("Q"));
        });

        [Fact]
        public void MoveTool_MovesSelectedPixels() => Sta(() =>
        {
            var h = new Harness();
            using (var red = new SKPaint { Color = SKColors.Red })
                h.Layer.Canvas.DrawRect(100, 100, 20, 20, red);

            h.Canvas.SelectAll(); // move everything
            h.Use(ToolType.Move);
            h.Drag((110, 110), (160, 110));

            Assert.Equal(SKColors.Red, h.Pixel(165, 110));
            Assert.Equal(SKColors.White, h.Pixel(105, 110)); // now holds what was 50px to the left
            Assert.Equal(0, h.Pixel(20, 110).Alpha);          // uncovered strip is transparent

            h.Document.Layers.History.Undo();
            Assert.Equal(SKColors.Red, h.Pixel(105, 110));
        });

        [Fact]
        public void ShapeTool_DrawsFilledRectangle() => Sta(() =>
        {
            var h = new Harness();
            h.Use(ToolType.Shape);
            // Pixels mode paints onto the active layer (the default makes a shape layer).
            var options = (JolieCat.UI.ViewModels.ToolOptions.ShapeToolOptionsViewModel)h.Toolbox.CurrentToolOptions;
            options.Mode = JolieCat.UI.ViewModels.ToolOptions.ShapeMode.Pixels;
            options.FillColor = "#0000FF";
            options.StrokeWidth = 0;

            h.Drag((50, 50), (150, 120));

            Assert.Equal(SKColors.Blue, h.Pixel(100, 85));
            Assert.Equal(SKColors.White, h.Pixel(200, 200));
        });

        [Fact]
        public void DodgeAndBurn_LightenAndDarken() => Sta(() =>
        {
            var h = new Harness();
            h.Layer.Canvas.Clear(new SKColor(128, 128, 128));

            h.Use(ToolType.Dodge);
            h.Drag((100, 100), (140, 100));
            Assert.True(h.Pixel(120, 100).Red > 128);

            h.Use(ToolType.Burn);
            h.Drag((100, 300), (140, 300));
            Assert.True(h.Pixel(120, 300).Red < 128);
        });

        [Fact]
        public void Sponge_RemovesColor() => Sta(() =>
        {
            var h = new Harness();
            h.Layer.Canvas.Clear(new SKColor(220, 40, 40));

            h.Use(ToolType.Sponge);
            h.Drag((100, 100), (140, 100), (100, 100), (140, 100));

            var p = h.Pixel(120, 100);
            Assert.True(p.Red - p.Green < 180);
        });

        [Fact]
        public void CloneStamp_WithoutSource_DoesNothing() => Sta(() =>
        {
            var h = new Harness();
            h.Use(ToolType.CloneStamp);

            h.Drag((100, 100), (140, 100));

            Assert.False(h.Document.Layers.History.CanUndo);
            Assert.Contains("Alt+click", h.Canvas.ToolHintText);
        });

        [Fact]
        public void SelectAllThenDelete_ClearsLayer() => Sta(() =>
        {
            var h = new Harness();
            h.Canvas.SelectAll();
            h.Canvas.DeleteSelectedPixels();

            Assert.Equal(0, h.Pixel(10, 10).Alpha);

            h.Document.Layers.History.Undo();
            Assert.Equal(SKColors.White, h.Pixel(10, 10));
        });

        [Fact]
        public void FilterInsideSelection_LeavesOutsideUntouched() => Sta(() =>
        {
            var h = new Harness();
            h.Layer.Canvas.Clear(SKColors.Black);
            h.Document.Layers.Scene.Selection.SetRect(new SKRectI(0, 0, 100, 100));

            h.Canvas.BeginAdjustment();
            h.Canvas.UpdateAdjustmentPreview(Core.Filters.StylizeFilters.Invert);
            h.Canvas.CommitAdjustment();

            Assert.Equal(SKColors.White, h.Pixel(50, 50));
            Assert.Equal(SKColors.Black, h.Pixel(300, 300));
        });

        [Fact]
        public void GradientDrag_GoesFromForegroundToBackground() => Sta(() =>
        {
            var h = new Harness();
            h.Canvas.PrimaryColor = System.Windows.Media.Colors.Black;
            h.Canvas.SecondaryColor = System.Windows.Media.Colors.White;
            h.Use(ToolType.Gradient);

            h.Drag((100, 100), (500, 100));

            Assert.True(h.Pixel(50, 300).Red < 10);     // before the start: first color
            Assert.True(h.Pixel(700, 300).Red > 245);   // past the end: second color
            Assert.InRange(h.Pixel(300, 300).Red, 100, 155); // halfway

            h.Document.Layers.History.Undo();
            Assert.Equal(SKColors.White, h.Pixel(50, 300));
        });

        [Fact]
        public void SwapAndResetColors() => Sta(() =>
        {
            var h = new Harness();
            h.Canvas.PrimaryColor = System.Windows.Media.Colors.Red;
            h.Canvas.SecondaryColor = System.Windows.Media.Colors.Blue;

            h.Canvas.SwapColors();
            Assert.Equal(System.Windows.Media.Colors.Blue, h.Canvas.PrimaryColor);
            Assert.Equal(System.Windows.Media.Colors.Red, h.Canvas.SecondaryColor);

            h.Canvas.ResetColors();
            Assert.Equal(System.Windows.Media.Colors.Black, h.Canvas.PrimaryColor);
            Assert.Equal(System.Windows.Media.Colors.White, h.Canvas.SecondaryColor);
        });

        [Fact]
        public void NewDocument_UsesRequestedSizeAndBackground() => Sta(() =>
        {
            var document = new DocumentViewModel(new ToolboxViewModel(), "Doc", ProjectType.StandardImage, 640, 480, null);
            Assert.Equal(640, document.Layers.DocumentWidth);
            Assert.Equal(480, document.Layers.DocumentHeight);
            Assert.Equal(0, document.Layers.Scene.Layers[0].Bitmap.GetPixel(5, 5).Alpha);

            var white = new DocumentViewModel(new ToolboxViewModel(), "Doc");
            Assert.Equal(SKColors.White, white.Layers.Scene.Layers[0].Bitmap.GetPixel(5, 5));
        });

        [Fact]
        public void RotateDocument_IsUndoable() => Sta(() =>
        {
            var h = new Harness();
            var (w, hgt) = (h.Document.Layers.DocumentWidth, h.Document.Layers.DocumentHeight);

            h.Document.Layers.Rotate90ClockwiseCommand.Execute(null);
            Assert.Equal((hgt, w), (h.Document.Layers.DocumentWidth, h.Document.Layers.DocumentHeight));

            h.Document.Layers.History.Undo();
            Assert.Equal((w, hgt), (h.Document.Layers.DocumentWidth, h.Document.Layers.DocumentHeight));
        });

        [Fact]
        public void FlipLayer_IsUndoable() => Sta(() =>
        {
            var h = new Harness();
            using (var red = new SKPaint { Color = SKColors.Red })
                h.Layer.Canvas.DrawRect(0, 0, 10, 10, red);
            var right = h.Document.Layers.DocumentWidth - 5;

            h.Document.Layers.FlipLayerHorizontalCommand.Execute(null);
            Assert.Equal(SKColors.Red, h.Pixel(right, 5));

            h.Document.Layers.History.Undo();
            Assert.Equal(SKColors.Red, h.Pixel(5, 5));
            Assert.Equal(SKColors.White, h.Pixel(right, 5));
        });

        [Fact]
        public void ReselectRestoresLastSelection() => Sta(() =>
        {
            var h = new Harness();
            h.Document.Layers.Scene.Selection.SetRect(new SKRectI(10, 10, 50, 50));

            h.Canvas.Deselect();
            Assert.False(h.Canvas.HasSelection);

            h.Canvas.Reselect();
            Assert.True(h.Document.Layers.Scene.Selection.Contains(30, 30));
            Assert.False(h.Document.Layers.Scene.Selection.Contains(60, 60));
        });

        [Fact]
        public void NewAdjustmentLayer_CommitIsOneUndoStep_CancelLeavesNoTrace() => Sta(() =>
        {
            var h = new Harness();
            var layers = h.Document.Layers;
            var invert = Core.Adjustments.AdjustmentSettings.Default(Core.Adjustments.AdjustmentKind.Invert);

            layers.BeginNewAdjustmentLayer(invert);
            layers.CancelNewAdjustmentLayer();
            Assert.Single(layers.Scene.Layers);
            Assert.False(layers.History.CanUndo);

            var adjustment = layers.BeginNewAdjustmentLayer(invert);
            layers.CommitNewAdjustmentLayer();
            Assert.Equal(2, layers.Scene.Layers.Count);
            Assert.Same(adjustment, layers.Scene.ActiveLayer);
            Assert.True(adjustment.IsMaskActive); // brushes paint the adjustment's mask

            layers.History.Undo();
            Assert.Single(layers.Scene.Layers);
            layers.History.Redo();
            Assert.Equal(Core.Adjustments.AdjustmentKind.Invert, layers.Scene.Layers[1].Adjustment?.Kind);
        });

        [Fact]
        public void EditingAnAdjustment_IsUndoable() => Sta(() =>
        {
            var h = new Harness();
            var layers = h.Document.Layers;
            var before = new Core.Adjustments.AdjustmentSettings(Core.Adjustments.AdjustmentKind.Posterize, new[] { 4.0 });
            var adjustment = layers.BeginNewAdjustmentLayer(before);
            layers.CommitNewAdjustmentLayer();

            var after = new Core.Adjustments.AdjustmentSettings(Core.Adjustments.AdjustmentKind.Posterize, new[] { 9.0 });
            layers.CommitAdjustmentEdit(adjustment, before, after);
            Assert.Equal(9.0, layers.Scene.Layers[1].Adjustment!.Values[0]);

            layers.History.Undo();
            Assert.Equal(4.0, layers.Scene.Layers[1].Adjustment!.Values[0]);
        });

        [Fact]
        public void SelectionModes_AddAndSubtract() => Sta(() =>
        {
            var h = new Harness();
            h.Use(ToolType.RectangularMarquee);
            var options = (SelectionToolOptionsViewModel)h.Toolbox.CurrentToolOptions;
            var selection = h.Document.Layers.Scene.Selection;

            h.Drag((10, 10), (50, 50));
            options.Mode = Core.Documents.SelectionCombine.Add;
            h.Drag((100, 10), (140, 50));
            Assert.True(selection.Contains(20, 20));
            Assert.True(selection.Contains(120, 20));

            options.Mode = Core.Documents.SelectionCombine.Subtract;
            h.Drag((0, 0), (30, 60));
            Assert.False(selection.Contains(20, 20));
            Assert.True(selection.Contains(40, 20));

            options.Mode = Core.Documents.SelectionCombine.New;
            h.Drag((300, 300), (340, 340));
            Assert.False(selection.Contains(120, 20));
            Assert.True(selection.Contains(320, 320));
        });

        [Fact]
        public void FeatheredSelection_FillsWithASoftEdge() => Sta(() =>
        {
            var h = new Harness();
            h.Use(ToolType.RectangularMarquee);
            ((SelectionToolOptionsViewModel)h.Toolbox.CurrentToolOptions).FeatherRadius = 20;
            h.Drag((100, 100), (300, 300));
            Assert.Equal(20f, h.Document.Layers.Scene.Selection.Feather);

            h.Canvas.PrimaryColor = System.Windows.Media.Colors.Black;
            h.Canvas.FillSelection();

            Assert.True(h.Pixel(200, 200).Red < 5);                 // deep inside: fully filled
            Assert.InRange(h.Pixel(100, 200).Red, 60, 200);         // on the edge: half way
            Assert.Equal(SKColors.White, h.Pixel(60, 200));         // well outside: untouched
        });

        [Fact]
        public void QuickSelection_GrowsOverSimilarColorOnly() => Sta(() =>
        {
            var h = new Harness();
            using (var red = new SKPaint { Color = SKColors.Red })
                h.Layer.Canvas.DrawRect(0, 0, 200, 400, red);
            h.Use(ToolType.QuickSelection);
            ((SelectionToolOptionsViewModel)h.Toolbox.CurrentToolOptions).BrushSize = 40;

            h.Drag((100, 100), (150, 100));

            var selection = h.Document.Layers.Scene.Selection;
            Assert.True(selection.Contains(190, 100));  // red, beyond the brush: grown into
            Assert.True(selection.Contains(100, 150));
            Assert.False(selection.Contains(215, 100)); // white: not selected
        });

        [Fact]
        public void RotatedView_PaintsWhereThePointerIs() => Sta(() =>
        {
            var h = new Harness();
            h.Canvas.ViewRotation = 37;
            h.Use(ToolType.Brush);
            h.Canvas.PrimaryColor = System.Windows.Media.Colors.Red;

            SKPoint Screen(float x, float y) => h.Canvas.ViewMatrix.MapPoint(new SKPoint(x, y));
            h.Canvas.OnPointerPressed(Screen(400, 300));
            h.Canvas.OnPointerMoved(Screen(600, 300));
            h.Canvas.OnPointerReleased(Screen(600, 300));

            Assert.Equal(255, h.Pixel(500, 300).Red);
            Assert.Equal(SKColors.White, h.Pixel(500, 360));
        });

        [Fact]
        public void RotatedView_WheelZoomKeepsThePointUnderTheCursor() => Sta(() =>
        {
            var h = new Harness();
            h.Canvas.ViewRotation = -50;
            var cursor = new SKPoint(520, 240);
            h.Canvas.ViewMatrix.TryInvert(out var inverse);
            var docUnderCursor = inverse.MapPoint(cursor);

            h.Canvas.OnMouseWheel(cursor, 120);

            var after = h.Canvas.ViewMatrix.MapPoint(docUnderCursor);
            Assert.InRange(after.X, cursor.X - 1.5f, cursor.X + 1.5f);
            Assert.InRange(after.Y, cursor.Y - 1.5f, cursor.Y + 1.5f);
        });

        [Fact]
        public void MagneticLasso_SnapsToTheShapeEdge() => Sta(() =>
        {
            var h = new Harness();
            using (var black = new SKPaint { Color = SKColors.Black })
                h.Layer.Canvas.DrawRect(100, 100, 200, 200, black);
            h.Use(ToolType.MagneticLasso);

            // Trace loosely, 5 px outside the square.
            h.Drag((95, 95), (305, 95), (305, 305), (95, 305), (95, 96));

            var selection = h.Document.Layers.Scene.Selection;
            Assert.True(selection.Contains(200, 200));
            Assert.True(selection.Contains(102, 200));
            Assert.False(selection.Contains(97, 200));   // the loose trace was pulled in to the edge
            Assert.False(selection.Contains(200, 303));
        });

        [Fact]
        public void ZoomCommands_StepAndFit() => Sta(() =>
        {
            var h = new Harness();
            var fitted = h.Canvas.Zoom;

            h.Canvas.ZoomIn();
            Assert.True(h.Canvas.Zoom > fitted);

            h.Canvas.ZoomActualPixels();
            Assert.Equal(1.0, h.Canvas.Zoom, 3);

            h.Canvas.ZoomToFit();
            Assert.Equal(fitted, h.Canvas.Zoom, 3);
        });
    }
}
