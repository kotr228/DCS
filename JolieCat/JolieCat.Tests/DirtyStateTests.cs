using JolieCat.Core.Documents;
using JolieCat.Core.History;
using JolieCat.Core.Serialization;
using SkiaSharp;

namespace JolieCat.Tests
{
    public class DirtyStateTests
    {
        private static LayerPixelsCommand Stroke(Scene scene, Layer layer, SKColor color)
        {
            var before = layer.Bitmap.Pixels;
            TestHelpers.FillRect(layer, new SKRect(0, 0, 2, 2), color);
            return new LayerPixelsCommand(scene, layer, false, before, layer.Bitmap.Pixels);
        }

        [Fact]
        public void FreshHistory_IsClean_EditMakesDirty_SaveCleans()
        {
            using var scene = new Scene("S");
            var layer = scene.AddLayer("L", 4, 4);
            var history = new HistoryManager();
            Assert.False(history.IsDirty);

            history.Push(Stroke(scene, layer, SKColors.Red));
            Assert.True(history.IsDirty);

            history.MarkSaved();
            Assert.False(history.IsDirty);
        }

        [Fact]
        public void UndoingBackToTheSavePoint_IsClean_UndoingPastItIsDirty()
        {
            using var scene = new Scene("S");
            var layer = scene.AddLayer("L", 4, 4);
            var history = new HistoryManager();
            history.Push(Stroke(scene, layer, SKColors.Red));
            history.MarkSaved();

            history.Push(Stroke(scene, layer, SKColors.Blue));
            Assert.True(history.IsDirty);
            history.Undo();
            Assert.False(history.IsDirty);
            history.Undo();
            Assert.True(history.IsDirty);
        }

        [Fact]
        public void ChangesOutsideHistory_StayDirtyUntilSaved()
        {
            var history = new HistoryManager();
            history.MarkModified();
            Assert.True(history.IsDirty);
            history.MarkSaved();
            Assert.False(history.IsDirty);
        }

        [Fact]
        public void ProjectSave_LeavesNoTemporaryFile_AndOverwrites()
        {
            var folder = Path.Combine(Path.GetTempPath(), "jc-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var path = Path.Combine(folder, "p.jolie");
                using var scene = new Scene("S");
                scene.AddLayer("L", 4, 4).Canvas.Clear(SKColors.Red);

                ProjectSerializer.Save(path, scene, Array.Empty<TimelineTrackData>(), 0, 0);
                scene.Layers[0].Canvas.Clear(SKColors.Blue);
                ProjectSerializer.Save(path, scene, Array.Empty<TimelineTrackData>(), 0, 0);

                Assert.False(File.Exists(path + ".saving"));
                var loaded = ProjectSerializer.Load(path);
                using (loaded.Scene)
                    Assert.Equal(SKColors.Blue, loaded.Scene.Layers[0].Bitmap.GetPixel(1, 1));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
