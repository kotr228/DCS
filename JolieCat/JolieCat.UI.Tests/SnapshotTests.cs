using JolieCat.Core.Documents;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class SnapshotTests
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
        public void ASnapshot_BringsBackTheDocument_AsOneUndoableStep() => Sta(() =>
        {
            var doc = new DocumentViewModel(new ToolboxViewModel(), "T");
            var layers = doc.Layers;
            layers.ActiveLayer!.Model.Canvas.Clear(SKColors.White);
            layers.NewSnapshotCommand.Execute(null);
            Assert.Equal("Snapshot 1", Assert.Single(layers.Snapshots).Name);

            // Lots of changes afterwards.
            layers.AddLayerCommand.Execute(null);
            layers.AddLayerCommand.Execute(null);
            layers.Rotate90ClockwiseCommand.Execute(null);
            var count = layers.Scene.Layers.Count;

            layers.RestoreSnapshotCommand.Execute(layers.Snapshots[0]);
            Assert.Single(layers.Scene.Layers);
            Assert.Equal(SKColors.White, layers.Scene.Layers[0].Bitmap.GetPixel(3, 3));

            layers.History.Undo();
            Assert.Equal(count, layers.Scene.Layers.Count);

            layers.DeleteSnapshotCommand.Execute(layers.Snapshots[0]);
            Assert.Empty(layers.Snapshots);
        });

        [Fact]
        public void DuplicateDocument_OpensAnIndependentCopy() => Sta(() =>
        {
            var main = new MainViewModel();
            var original = main.ActiveDocument!;
            original.Layers.ActiveLayer!.Model.Canvas.Clear(SKColors.Red);
            original.Layers.Scene.Guides.Add(new Guide(true, 10));
            original.Layers.AddLayerCommand.Execute(null);

            main.DuplicateDocumentCommand.Execute(null);

            var copy = main.ActiveDocument!;
            Assert.NotSame(original, copy);
            Assert.Equal(2, copy.Layers.Scene.Layers.Count);
            Assert.Equal(SKColors.Red, copy.Layers.Scene.Layers[0].Bitmap.GetPixel(1, 1));
            Assert.Single(copy.Layers.Scene.Guides);

            copy.Layers.Scene.Layers[0].Canvas.Clear(SKColors.Blue);   // independent pixels
            Assert.Equal(SKColors.Red, original.Layers.Scene.Layers[0].Bitmap.GetPixel(1, 1));
        });
    }
}
