using System.IO;
using JolieCat.UI.Media;
using JolieCat.UI.ViewModels.ToolOptions;

namespace JolieCat.UI.Tests
{
    public class SwatchAndPresetTests
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
        public void Swatches_AddToTheFront_WithoutDuplicates_AndIgnoreJunk()
        {
            var library = new SwatchLibrary(path: null);
            Assert.True(library.Add("#ff0000"));
            Assert.True(library.Add("#00FF00"));
            Assert.True(library.Add("#FFFF0000"));     // #AARRGGBB: same red, moves to the front
            Assert.False(library.Add("red"));
            Assert.False(library.Add("#12"));

            Assert.Equal(new[] { "#FF0000", "#00FF00" }, library.Colors);
            library.Remove("#FF0000");
            Assert.Equal(new[] { "#00FF00" }, library.Colors);
        }

        [Fact]
        public void Swatches_AreKeptBetweenRuns()
        {
            var path = Path.Combine(Path.GetTempPath(), $"swatches_{Guid.NewGuid():N}.txt");
            try
            {
                new SwatchLibrary(path).Add("#123456");
                Assert.Equal(new[] { "#123456" }, new SwatchLibrary(path).Colors);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void SavedGradient_BecomesAPreset_LoadsBack_AndCanBeDeleted() => Sta(() =>
        {
            GradientLibrary.UseForTests(new GradientLibrary(path: null));
            var options = new GradientToolOptionsViewModel();
            options.LoadPresetCommand.Execute("Sunset");
            options.Stops[0].Hex = "#010203";

            options.SavePresetCommand.Execute(null);
            Assert.Contains("My gradient 1", options.Presets);

            // A fresh options panel sees the saved preset and loads its stops.
            var other = new GradientToolOptionsViewModel();
            Assert.Contains("My gradient 1", other.Presets);
            other.LoadPresetCommand.Execute("My gradient 1");
            Assert.Equal("#010203", other.Stops.OrderBy(s => s.Location).First().Hex);
            Assert.Equal(3, other.Stops.Count);

            other.DeletePresetCommand.Execute("Sunset");          // built-in: stays
            other.DeletePresetCommand.Execute("My gradient 1");
            Assert.Contains("Sunset", other.Presets);
            Assert.DoesNotContain("My gradient 1", other.Presets);
            Assert.Null(GradientLibrary.Shared.Find("My gradient 1"));
        });
    }
}
