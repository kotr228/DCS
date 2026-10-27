using JolieCat.Core.Documents;
using JolieCat.Core.History;
using SkiaSharp;

namespace JolieCat.Tests
{
    public class HistoryPanelTests
    {
        /// <summary>Three named fills on one layer: red, green, blue.</summary>
        private static HistoryManager ThreeSteps(Scene scene, Layer layer)
        {
            var history = new HistoryManager();
            foreach (var (color, name) in new[] { (SKColors.Red, "Red"), (SKColors.Lime, "Green"), (SKColors.Blue, "Blue") })
            {
                var before = layer.Bitmap.Pixels;
                layer.Canvas.Clear(color);
                history.Push(new LayerPixelsCommand(scene, layer, false, before, layer.Bitmap.Pixels), name);
            }
            return history;
        }

        [Fact]
        public void Steps_KeepTheirNames_ThroughUndoAndRedo()
        {
            using var scene = new Scene("S");
            var layer = scene.AddLayer("L", 8, 8);
            var history = ThreeSteps(scene, layer);

            Assert.Equal(new[] { "Red", "Green", "Blue" }, history.UndoNames);

            history.Undo();
            Assert.Equal(new[] { "Red", "Green" }, history.UndoNames);
            Assert.Equal(new[] { "Blue" }, history.RedoNames);

            history.Redo();
            Assert.Equal(new[] { "Red", "Green", "Blue" }, history.UndoNames);
            Assert.Empty(history.RedoNames);
        }

        [Fact]
        public void JumpTo_GoesBackAndForward_WithOneChangedEvent()
        {
            using var scene = new Scene("S");
            var layer = scene.AddLayer("L", 8, 8);
            var history = ThreeSteps(scene, layer);
            var changes = 0;
            history.Changed += (_, _) => changes++;

            history.JumpTo(1);
            Assert.Equal(SKColors.Red, layer.Bitmap.GetPixel(2, 2));
            Assert.Equal(new[] { "Green", "Blue" }, history.RedoNames);
            Assert.Equal(1, changes);

            history.JumpTo(3);
            Assert.Equal(SKColors.Blue, layer.Bitmap.GetPixel(2, 2));

            history.JumpTo(0);
            Assert.Equal(0, layer.Bitmap.GetPixel(2, 2).Alpha);
            Assert.Equal(3, changes);
        }

        [Fact]
        public void NewStepAfterJumpingBack_DropsTheUndoneSteps()
        {
            using var scene = new Scene("S");
            var layer = scene.AddLayer("L", 8, 8);
            var history = ThreeSteps(scene, layer);

            history.JumpTo(1);
            var before = layer.Bitmap.Pixels;
            layer.Canvas.Clear(SKColors.Yellow);
            history.Push(new LayerPixelsCommand(scene, layer, false, before, layer.Bitmap.Pixels), "Yellow");

            Assert.Equal(new[] { "Red", "Yellow" }, history.UndoNames);
            Assert.Empty(history.RedoNames);
        }

        [Fact]
        public void UnnamedSteps_UseTheDefaultNamer()
        {
            using var scene = new Scene("S");
            var layer = scene.AddLayer("L", 8, 8);
            var history = new HistoryManager { DefaultName = _ => "Brush" };

            var before = layer.Bitmap.Pixels;
            layer.Canvas.Clear(SKColors.Red);
            history.Push(new LayerPixelsCommand(scene, layer, false, before, layer.Bitmap.Pixels));

            Assert.Equal("Brush", history.UndoNames[0]);
        }
    }
}
