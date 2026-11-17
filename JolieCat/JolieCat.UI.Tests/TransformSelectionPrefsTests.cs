using System.IO;
using JolieCat.Shared.Enums;
using JolieCat.UI.Media;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class TransformSelectionPrefsTests
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
        public void TransformSelection_ScalesTurnsAndMovesTheOutline() => Sta(() =>
        {
            var doc = new DocumentViewModel(new ToolboxViewModel(), "T", ProjectType.StandardImage, 200, 200, null);
            var selection = doc.Layers.Scene.Selection;
            using (var rect = new SKPath()) { rect.AddRect(new SKRect(50, 50, 100, 70)); selection.SetPath(new SKPath(rect), 200, 200); }
            selection.Feather = 3;

            doc.Canvas.PreviewTransformSelection(200, 100, 0, 0, 0);
            Assert.Equal(new SKRect(50, 50, 100, 70), selection.Path!.Bounds);   // only previewed

            doc.Canvas.TransformSelection(200, 100, 90, 10, 0);                 // 100 x 20, turned: 20 x 100
            var b = selection.Path!.Bounds;
            Assert.Equal(20, b.Width, 1);
            Assert.Equal(100, b.Height, 1);
            Assert.Equal(75 + 10, b.MidX, 1);
            Assert.Equal(60, b.MidY, 1);
            Assert.Equal(3, selection.Feather);
        });

        [Fact]
        public void Preferences_SaveLoad_AndStayInRange()
        {
            var file = Path.Combine(Path.GetTempPath(), $"prefs-{Guid.NewGuid():N}.json");
            var old = Preferences.FilePath;
            Preferences.FilePath = file;
            try
            {
                Assert.Equal(200, Preferences.Load().HistoryStates);            // no file: defaults
                new Preferences { HistoryStates = 50, AutosaveMinutes = 0, RecentFiles = 99 }.Clamped().Save();
                var loaded = Preferences.Load();
                Assert.Equal(50, loaded.HistoryStates);
                Assert.Equal(0, loaded.AutosaveMinutes);
                Assert.Equal(30, loaded.RecentFiles);                          // clamped
                File.WriteAllText(file, "{ broken");
                Assert.Equal(10, Preferences.Load().RecentFiles);
            }
            finally
            {
                Preferences.FilePath = old;
                File.Delete(file);
            }
        }
    }
}
