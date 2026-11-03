using JolieCat.Core.Clipboard;
using JolieCat.Core.Documents;
using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class EditExtrasTests
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

        private static DocumentViewModel White(out Layer layer)
        {
            var doc = new DocumentViewModel(new ToolboxViewModel(), "T");
            layer = doc.Layers.ActiveLayer!.Model;
            layer.Canvas.Clear(SKColors.White);
            return doc;
        }

        [Theory]
        [InlineData(StrokeLocation.Inside, 101, 98)]
        [InlineData(StrokeLocation.Outside, 98, 101)]
        public void Stroke_DrawsOnTheChosenSideOfTheEdge(StrokeLocation location, int paintedX, int untouchedX) => Sta(() =>
        {
            var doc = White(out var layer);
            doc.Layers.Scene.Selection.SetRect(new SKRectI(100, 100, 200, 200));

            doc.Canvas.StrokeSelection(4, SKColors.Red, location);

            Assert.Equal(SKColors.Red, layer.Bitmap.GetPixel(paintedX, 150));
            Assert.Equal(SKColors.White, layer.Bitmap.GetPixel(untouchedX, 150));
            Assert.Equal("Stroke", doc.Layers.History.UndoNames[^1]);
        });

        [Fact]
        public void Fade_MixesTheLastEditBack() => Sta(() =>
        {
            var doc = White(out var layer);
            Assert.True(MainViewModel.PlayActionStep(new Core.Actions.ActionStep { Kind = "Filter", Command = "Invert" }, doc)); // white -> black
            Assert.True(doc.Canvas.CanFade);

            Assert.True(doc.Canvas.BeginFade("Invert"));
            doc.Canvas.UpdateFade(50, BlendMode.Normal);
            doc.Canvas.CommitAdjustment();

            Assert.InRange(layer.Bitmap.GetPixel(5, 5).Red, 126, 129);  // halfway between white and black
            Assert.Equal("Fade Invert", doc.Layers.History.UndoNames[^1]);
        });

        [Fact]
        public void Fade_IsOnlyOffered_RightAfterAPixelEdit() => Sta(() =>
        {
            var doc = White(out _);
            Assert.False(doc.Canvas.CanFade);
            MainViewModel.PlayActionStep(new Core.Actions.ActionStep { Kind = "Filter", Command = "Invert" }, doc);
            doc.Layers.AddLayerCommand.Execute(null);   // something else happened
            Assert.False(doc.Canvas.CanFade);
        });

        [Fact]
        public void MaskFromSelection_RevealsOrHidesTheSelectedArea() => Sta(() =>
        {
            var doc = White(out var layer);
            doc.Layers.Scene.Selection.SetRect(new SKRectI(10, 10, 50, 50));

            doc.Layers.AddMaskFromSelectionCommand.Execute("Reveal");
            Assert.Equal(SKColors.White, layer.Mask!.Bitmap.GetPixel(20, 20));
            Assert.Equal(SKColors.Black, layer.Mask.Bitmap.GetPixel(80, 80));

            doc.Layers.History.Undo();
            doc.Layers.AddMaskFromSelectionCommand.Execute("Hide");
            var mask = doc.Layers.ActiveLayer!.Model.Mask!;
            Assert.Equal(SKColors.Black, mask.Bitmap.GetPixel(20, 20));
            Assert.Equal(SKColors.White, mask.Bitmap.GetPixel(80, 80));
        });

        [Fact]
        public void CropToSelection_AndTrim() => Sta(() =>
        {
            var doc = White(out var layer);
            doc.Layers.Scene.Selection.SetRect(new SKRectI(10, 20, 110, 70));
            doc.Layers.CropToSelectionCommand.Execute(null);
            Assert.Equal(100, doc.Layers.DocumentWidth);
            Assert.Equal(50, doc.Layers.DocumentHeight);

            // A red dot on white: trimming by the corner color keeps just the dot.
            layer = doc.Layers.ActiveLayer!.Model;
            using (var red = new SKPaint { Color = SKColors.Red })
                layer.Canvas.DrawRect(30, 10, 5, 4, red);
            doc.Layers.TrimCommand.Execute("TopLeft");
            Assert.Equal(5, doc.Layers.DocumentWidth);
            Assert.Equal(4, doc.Layers.DocumentHeight);
        });

        [Fact]
        public void SavedSelections_ComeBack_AndCombine() => Sta(() =>
        {
            var doc = White(out _);
            var selection = doc.Layers.Scene.Selection;
            selection.SetRect(new SKRectI(0, 0, 50, 50));
            Assert.True(doc.Canvas.SaveSelection("Left"));
            selection.SetRect(new SKRectI(100, 0, 150, 50));
            Assert.True(doc.Canvas.SaveSelection("Right"));
            selection.Clear();

            Assert.True(doc.Canvas.LoadSelection("Left"));
            Assert.True(doc.Canvas.LoadSelection("Right", SelectionCombine.Add));
            Assert.True(selection.Contains(10, 10));
            Assert.True(selection.Contains(120, 10));
            Assert.False(selection.Contains(75, 10));
            Assert.Equal(new[] { "Left", "Right" }, doc.Canvas.SavedSelectionNames);
        });

        [Fact]
        public void SelectAndMask_GrowsAndFeathersTheSelection() => Sta(() =>
        {
            var doc = White(out _);
            var selection = doc.Layers.Scene.Selection;
            selection.SetRect(new SKRectI(50, 50, 100, 100));

            doc.Canvas.PreviewRefineSelection(0, 10);
            Assert.NotNull(doc.Canvas.LiveSelectionPath);
            Assert.False(selection.Contains(45, 75));          // only a preview so far

            doc.Canvas.RefineSelection(0, 10, 4);
            Assert.Null(doc.Canvas.LiveSelectionPath);
            Assert.True(selection.Contains(45, 75));
            Assert.Equal(4, selection.Feather);
        });

        [Fact]
        public void LoadLayerTransparency_SelectsThePaintedPixels() => Sta(() =>
        {
            var doc = new DocumentViewModel(new ToolboxViewModel(), "T");
            doc.Layers.AddLayerCommand.Execute(null);
            var layer = doc.Layers.ActiveLayer!.Model;
            using (var red = new SKPaint { Color = SKColors.Red })
                layer.Canvas.DrawCircle(100, 100, 30, red);

            doc.Canvas.SelectLayerTransparency();
            var selection = doc.Layers.Scene.Selection;
            Assert.True(selection.Contains(100, 100));
            Assert.False(selection.Contains(100, 140));
        });

        [Fact]
        public void Cut_ThenPasteInPlace_PutsThePixelsBackWhereTheyWere() => Sta(() =>
        {
            var main = new MainViewModel();
            var layer = main.Layers.ActiveLayer!.Model;
            layer.Canvas.Clear(SKColors.White);
            using (var red = new SKPaint { Color = SKColors.Red })
                layer.Canvas.DrawRect(40, 40, 20, 20, red);
            main.Layers.Scene.Selection.SetRect(new SKRectI(40, 40, 60, 60));

            main.CutCommand.Execute(null);
            Assert.Equal(0, layer.Bitmap.GetPixel(50, 50).Alpha);   // cleared
            Assert.True(PixelClipboard.HasContent);

            main.PasteInPlaceCommand.Execute(null);
            var pasted = main.Layers.ActiveLayer!.Model;
            Assert.NotSame(layer, pasted);
            Assert.Equal(SKColors.Red, pasted.Bitmap.GetPixel(50, 50));
            Assert.Equal(0, pasted.Bitmap.GetPixel(30, 30).Alpha);
        });
    }
}
