using System.IO;
using JolieCat.Core.Serialization;
using JolieCat.UI.Media;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class RecoveryTests
    {
        private static void Sta(Func<Task> test)
        {
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try { test().GetAwaiter().GetResult(); }
                catch (Exception ex) { error = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error is not null) throw new Exception(error.Message, error);
        }

        [Fact]
        public void Autosave_WritesOnlyDirtyDocuments_AndTheCopyReopens() => Sta(async () =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "jc-rec-" + Guid.NewGuid().ToString("N"));
            try
            {
                var recovery = new DocumentRecovery(folder);
                var clean = new DocumentViewModel(new ToolboxViewModel(), "Clean");
                var dirty = new DocumentViewModel(new ToolboxViewModel(), "Dirty", Shared.Enums.ProjectType.StandardImage, 40, 30, SKColors.White);
                dirty.Layers.AddLayerCommand.Execute(null);
                dirty.Layers.Scene.ActiveLayer!.Canvas.Clear(SKColors.Green);

                await recovery.AutosaveAsync(new[] { clean, dirty }, _ => Array.Empty<TimelineTrackData>());

                var leftovers = recovery.FindLeftovers();
                var found = Assert.Single(leftovers);
                Assert.Equal("Dirty", found.Title);

                var loaded = ProjectSerializer.Load(found.ProjectPath);
                using (loaded.Scene)
                {
                    Assert.Equal(2, loaded.Scene.Layers.Count);
                    Assert.Equal(SKColors.Green, loaded.Scene.Layers[1].Bitmap.GetPixel(5, 5));
                }

                recovery.Delete(dirty);
                Assert.Empty(recovery.FindLeftovers());
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        });

        [Fact]
        public void Autosave_SkipsUnchangedDocumentsOnTheNextRound() => Sta(async () =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "jc-rec-" + Guid.NewGuid().ToString("N"));
            try
            {
                var recovery = new DocumentRecovery(folder);
                var doc = new DocumentViewModel(new ToolboxViewModel(), "Doc", Shared.Enums.ProjectType.StandardImage, 20, 20, SKColors.White);
                doc.Layers.AddLayerCommand.Execute(null);

                await recovery.AutosaveAsync(new[] { doc }, _ => Array.Empty<TimelineTrackData>());
                var file = recovery.FindLeftovers()[0].ProjectPath;
                var firstWrite = File.GetLastWriteTimeUtc(file);

                await Task.Delay(50);
                await recovery.AutosaveAsync(new[] { doc }, _ => Array.Empty<TimelineTrackData>());

                Assert.Equal(firstWrite, File.GetLastWriteTimeUtc(file));
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        });
    }
}
