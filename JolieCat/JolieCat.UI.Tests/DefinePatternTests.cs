using System.IO;
using JolieCat.Core.Documents;
using JolieCat.Core.Effects;
using JolieCat.Core.Rendering;
using JolieCat.UI.Media;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class DefinePatternTests
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
        public void Manager_RenamesAndDeletes_AndStylesFollowTheNewName() => Sta(() =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "JolieCatPatterns_" + Guid.NewGuid().ToString("N"));
            PatternLibrary.Folder = folder;
            var renamed = $"Bricks {Guid.NewGuid():N}"[..20];
            string? name = null;
            try
            {
                var main = new MainViewModel();
                main.Layers.ActiveLayer!.Model.Canvas.Clear(SKColors.Orange);
                name = main.DefinePatternFromDocument()!;
                main.Layers.ActiveLayer!.Model.Style = new LayerStyle { PatternOverlay = true, PatternName = name };

                var manager = new PatternManagerViewModel();
                manager.Renamed += main.RenamePatternInStyles;
                manager.Selected = manager.Patterns.Single(p => p.Name == name);

                manager.Selected.EditName = "bad,name";
                manager.RenameCommand.Execute(null);
                Assert.Contains("can't contain", manager.Message);
                Assert.Equal(name, manager.Selected.Name);

                manager.Selected.EditName = renamed;
                manager.RenameCommand.Execute(null);
                Assert.True(PatternTiles.IsCustom(renamed));
                Assert.False(PatternTiles.IsCustom(name));
                Assert.True(File.Exists(Path.Combine(folder, renamed + ".png")));
                Assert.Equal(renamed, main.Layers.ActiveLayer!.Model.Style!.PatternName);

                manager.DeleteCommand.Execute(null);
                Assert.False(PatternTiles.IsCustom(renamed));
                Assert.False(File.Exists(Path.Combine(folder, renamed + ".png")));
                Assert.DoesNotContain(manager.Patterns, p => p.Name == renamed);
            }
            finally
            {
                if (name is not null) PatternTiles.Unregister(name);
                PatternTiles.Unregister(renamed);
                try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
            }
        });

        [Fact]
        public void TheSelectedArea_BecomesASavedPattern_ThatPatternOverlayDraws() => Sta(() =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "JolieCatPatterns_" + Guid.NewGuid().ToString("N"));
            PatternLibrary.Folder = folder;
            string? name = null;
            try
            {
                var main = new MainViewModel();
                var layer = main.Layers.ActiveLayer!.Model;
                layer.Canvas.Clear(SKColors.White);
                using (var blue = new SKPaint { Color = SKColors.Blue })
                    layer.Canvas.DrawRect(10, 10, 2, 4, blue);           // blue at the left of a 4x4 area
                main.Layers.Scene.Selection.SetRect(new SKRectI(10, 10, 14, 14));

                name = main.DefinePatternFromDocument();
                Assert.NotNull(name);
                Assert.True(PatternTiles.IsCustom(name));
                Assert.True(File.Exists(Path.Combine(folder, name + ".png")));

                // The 4x4 tile repeats: blue in its left half, white in its right half.
                using var scene = new Scene("S");
                var target = scene.AddLayer("L", 16, 8);
                target.Canvas.Clear(SKColors.Red);
                target.Style = new LayerStyle { PatternOverlay = true, PatternName = name!, PatternOpacity = 100 };
                using var flat = SceneCompositor.Flatten(scene, 16, 8);
                Assert.Equal(SKColors.Blue, flat.GetPixel(4, 1));
                Assert.Equal(SKColors.White, flat.GetPixel(6, 1));
                Assert.Equal(SKColors.Blue, flat.GetPixel(13, 5));
            }
            finally
            {
                if (name is not null) PatternTiles.Unregister(name);
                try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
            }
        });
    }
}
