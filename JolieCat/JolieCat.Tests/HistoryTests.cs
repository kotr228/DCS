using JolieCat.Core.Documents;
using JolieCat.Core.History;
using SkiaSharp;

namespace JolieCat.Tests
{
    public class HistoryTests
    {
        [Fact]
        public void PixelCommand_KeepsOnlyChangedTiles()
        {
            using var layer = TestHelpers.SolidLayer("L", 512, 512, SKColors.White);
            var before = layer.Bitmap.Pixels;

            // A 10x10 dot inside a single 64x64 tile.
            TestHelpers.FillRect(layer, new SKRect(5, 5, 15, 15), SKColors.Red);
            var command = new LayerPixelsCommand(layer.Bitmap, before, layer.Bitmap.Pixels);

            Assert.Equal(1, command.TileCount);
            Assert.True(command.ByteSize < 64 * 64 * 4 * 2 + 1);
        }

        [Fact]
        public void PixelCommand_UndoRedo_RestoresExactPixels()
        {
            using var layer = TestHelpers.SolidLayer("L", 200, 150, SKColors.Blue);
            var before = layer.Bitmap.Pixels;
            TestHelpers.FillRect(layer, new SKRect(60, 30, 140, 120), SKColors.Yellow);
            var after = layer.Bitmap.Pixels;

            var command = new LayerPixelsCommand(layer.Bitmap, before, after);

            command.Undo();
            Assert.Equal(before, layer.Bitmap.Pixels);

            command.Redo();
            Assert.Equal(after, layer.Bitmap.Pixels);
        }

        [Fact]
        public void History_SkipsEditsThatChangedNothing()
        {
            using var layer = TestHelpers.SolidLayer("L", 64, 64, SKColors.White);
            var history = new HistoryManager();
            var pixels = layer.Bitmap.Pixels;

            history.Push(new LayerPixelsCommand(layer.Bitmap, pixels, pixels));

            Assert.False(history.CanUndo);
        }

        /// <summary>The bug this guards against: adding a layer used to wipe all undo
        /// history, and structural undo rebuilt layers so older pixel entries pointed at
        /// dead bitmaps.</summary>
        [Fact]
        public void PaintThenAddLayer_BothStepsUndoInOrder()
        {
            using var scene = new Scene("S");
            var background = scene.AddLayer("Background", 100, 100);
            background.Canvas.Clear(SKColors.White);
            var history = new HistoryManager();

            var beforePaint = background.Bitmap.Pixels;
            TestHelpers.FillRect(background, new SKRect(10, 10, 50, 50), SKColors.Black);
            history.Push(new LayerPixelsCommand(scene, background, isMask: false, beforePaint, background.Bitmap.Pixels));

            var beforeAdd = scene.CaptureLayers();
            scene.AddLayer("Layer 2", 100, 100);
            history.PushStructural(new SceneStructuralCommand(scene, beforeAdd, 0, scene.CaptureLayers(), 1));

            Assert.Equal(2, history.UndoCount);

            history.Undo(); // removes Layer 2 (rebuilds Background as a new object, same Id)
            Assert.Single(scene.Layers);
            Assert.Equal(SKColors.Black, scene.Layers[0].Bitmap.GetPixel(20, 20));

            history.Undo(); // the stroke - must find the rebuilt Background by Id
            Assert.Equal(SKColors.White, scene.Layers[0].Bitmap.GetPixel(20, 20));

            history.Redo();
            Assert.Equal(SKColors.Black, scene.Layers[0].Bitmap.GetPixel(20, 20));
        }

        [Fact]
        public void History_DropsOldestEntriesPastDepthLimit()
        {
            using var layer = TestHelpers.SolidLayer("L", 8, 8, SKColors.White);
            var history = new HistoryManager();

            for (var i = 0; i < HistoryManager.MaxDepth + 10; i++)
            {
                var before = layer.Bitmap.Pixels;
                layer.Canvas.Clear(new SKColor((byte)i, 0, 0));
                history.Push(new LayerPixelsCommand(layer.Bitmap, before, layer.Bitmap.Pixels));
            }

            Assert.Equal(HistoryManager.MaxDepth, history.UndoCount);
        }
    }
}
