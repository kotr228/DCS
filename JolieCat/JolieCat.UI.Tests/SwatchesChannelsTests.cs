using System.IO;
using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class SwatchesChannelsTests
    {
        private static void Sta(Action test)
        {
            Exception? error = null;
            var thread = new Thread(() => { try { test(); } catch (Exception ex) { error = ex; } });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error is not null) throw new Exception(error.Message, error);
        }

        [Fact]
        public void Swatches_PickAddRemove_AndAreKept() => Sta(() =>
        {
            var file = Path.Combine(Path.GetTempPath(), $"swatches-{Guid.NewGuid():N}.json");
            var old = DocumentPanelsViewModel.SwatchesFile;
            DocumentPanelsViewModel.SwatchesFile = file;
            try
            {
                var doc = new DocumentViewModel(new ToolboxViewModel(), "S", ProjectType.StandardImage, 40, 30, null);
                var panels = new DocumentPanelsViewModel(() => doc, startTimer: false);
                Assert.True(panels.Swatches.Count > 20);                       // the starter colors

                var red = panels.Swatches.First(s => s.Color.R > 200 && s.Color.G < 80 && s.Color.B < 80);
                panels.PickSwatchCommand.Execute(red);
                Assert.Equal(red.Color, doc.Canvas.PrimaryColor);
                panels.PickBackgroundSwatchCommand.Execute(panels.Swatches[0]);
                Assert.Equal(panels.Swatches[0].Color, doc.Canvas.SecondaryColor);

                doc.Canvas.PrimaryColor = System.Windows.Media.Color.FromRgb(12, 34, 56);
                var count = panels.Swatches.Count;
                panels.AddSwatchCommand.Execute(null);
                panels.AddSwatchCommand.Execute(null);                         // the same one again: not twice
                Assert.Equal(count + 1, panels.Swatches.Count);

                var again = new DocumentPanelsViewModel(() => doc, startTimer: false);   // read back from the file
                Assert.Equal("#0C2238", again.Swatches[^1].Hex);
                again.RemoveSwatchCommand.Execute(again.Swatches[^1]);
                Assert.Equal(count, new DocumentPanelsViewModel(() => doc, startTimer: false).Swatches.Count);
            }
            finally
            {
                DocumentPanelsViewModel.SwatchesFile = old;
                File.Delete(file);
            }
        });

        [Fact]
        public void Channels_ShowOneChannel_AndLoadSavedSelections() => Sta(() =>
        {
            var doc = new DocumentViewModel(new ToolboxViewModel(), "C", ProjectType.StandardImage, 40, 30, null);
            doc.Layers.Scene.Layers[0].Canvas.Clear(new SKColor(255, 0, 0));
            doc.Layers.Scene.SavedSelections["Left"] = "M0 0 L20 0 L20 30 L0 30 Z";
            var panels = new DocumentPanelsViewModel(() => doc, startTimer: false) { ShowChannels = true };
            panels.Refresh();
            Assert.Equal(new[] { "RGB", "Red", "Green", "Blue", "Left" }, panels.Channels.Select(c => c.Name));
            Assert.True(panels.Channels[0].IsViewed);

            panels.ChooseChannelCommand.Execute(panels.Channels[2]);           // Green alone
            Assert.Equal(2, doc.Canvas.ViewChannel);
            Assert.True(panels.Channels[2].IsViewed);
            Assert.False(panels.Channels[0].IsViewed);

            panels.ChooseChannelCommand.Execute(panels.Channels[4]);           // the saved selection
            Assert.True(doc.Layers.Scene.Selection.HasSelection);
            Assert.Equal(2, doc.Canvas.ViewChannel);                           // the view stays

            panels.DeleteSavedSelectionCommand.Execute(panels.Channels[4]);
            Assert.Equal(4, panels.Channels.Count);
        });
    }
}
