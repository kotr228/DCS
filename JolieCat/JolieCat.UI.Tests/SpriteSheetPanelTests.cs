using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class SpriteSheetPanelTests
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
        public void DetectGrid_SetsThePanelFromTheSprites_AndPreviewSkipsEmptyCells() => Sta(() =>
        {
            // 3 x 2 cells of 20 x 20 with 4 px between; 5 sprites, the 6th cell empty.
            var doc = new DocumentViewModel(new ToolboxViewModel(), "Sheet", ProjectType.SpriteSheet, 68, 44, null);
            var layers = doc.Layers;
            var layer = layers.Scene.Layers[0];
            layer.Canvas.Clear(SKColors.Transparent);
            using (var paint = new SKPaint { Color = SKColors.Green })
                for (var i = 0; i < 5; i++) layer.Canvas.DrawRect(i % 3 * 24, i / 3 * 24, 20, 20, paint);

            Assert.True(layers.DetectSpriteSheetGrid());
            Assert.Equal(3, layers.SpriteSheetColumns);
            Assert.Equal(2, layers.SpriteSheetRows);
            Assert.Equal(4, layers.SpriteSheetPaddingX);
            Assert.Equal(3, layers.SpriteSheetGrid.Columns);

            Assert.Equal(5, layers.SpritePreviewCells().Count);
            layers.SpritePreviewRow = 2;
            Assert.Equal(2, layers.SpritePreviewCells().Count);
        });

        [Fact]
        public void Cells_SwapAndPivot_Undo() => Sta(() =>
        {
            var doc = new DocumentViewModel(new ToolboxViewModel(), "Sheet", ProjectType.SpriteSheet, 40, 20, null);
            var layers = doc.Layers;
            layers.SpriteSheetColumns = 2;
            layers.SpriteSheetRows = 1;
            var layer = layers.Scene.Layers[0];
            layer.Canvas.Clear(SKColors.Transparent);
            using (var red = new SKPaint { Color = SKColors.Red }) layer.Canvas.DrawRect(0, 0, 20, 20, red);

            Assert.True(layers.ChangeCells(Core.Documents.SpriteSheetTools.CellAction.Swap, 0, 1));
            Assert.Equal(SKColors.Red, layers.Scene.Layers[0].Bitmap.GetPixel(30, 10));
            Assert.Equal(0, layers.Scene.Layers[0].Bitmap.GetPixel(10, 10).Alpha);
            layers.History.Undo();
            Assert.Equal(SKColors.Red, layers.Scene.Layers[0].Bitmap.GetPixel(10, 10));

            layers.SetCellPivot(1, 20, 30);
            Assert.Equal((20.0, 30.0), layers.SpriteSheetGrid.PivotFor(1));
            layers.History.Undo();
            Assert.Equal((50.0, 100.0), layers.SpriteSheetGrid.PivotFor(1));
            layers.History.Redo();
            Assert.Equal((20.0, 30.0), layers.SpriteSheetGrid.PivotFor(1));
        });
    }
}
