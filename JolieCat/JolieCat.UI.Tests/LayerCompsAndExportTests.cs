using System.IO;
using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class LayerCompsAndExportTests
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
        public void LayerComp_BringsBackVisibilityOpacityAndBlend_AsOneUndoStep() => Sta(() =>
        {
            var doc = new DocumentViewModel(new ToolboxViewModel(), "T");
            var layers = doc.Layers;
            layers.AddLayerCommand.Execute(null);
            var top = layers.Scene.Layers[^1];
            top.Opacity = 0.4;
            top.BlendMode = BlendMode.Multiply;
            layers.NewLayerCompCommand.Execute(null);

            top.IsVisible = false;
            top.Opacity = 1;
            top.BlendMode = BlendMode.Normal;
            layers.NewLayerCompCommand.Execute(null);
            Assert.Equal("Layer Comp 2", layers.LayerComps[1].Name);

            layers.ApplyLayerCompCommand.Execute(layers.LayerComps[0]);
            Assert.True(top.IsVisible);
            Assert.Equal(0.4, top.Opacity, 3);
            Assert.Equal(BlendMode.Multiply, top.BlendMode);

            layers.History.Undo();
            var again = layers.Scene.Layers[^1];
            Assert.False(again.IsVisible);
            Assert.Equal(BlendMode.Normal, again.BlendMode);
        });

        [Fact]
        public void ExportAs_WritesOneFilePerSize() => Sta(() =>
        {
            Assert.Equal(new[] { 1.0, 0.5, 2.0 }, MainViewModel.ParseScales("100%, 50%, 2x, 2x, junk"));

            var main = new MainViewModel();
            var folder = Path.Combine(Path.GetTempPath(), "jc_export_" + Guid.NewGuid().ToString("N"));
            try
            {
                var w = main.Layers.DocumentWidth;
                var files = main.ExportScaled(folder, "pic", "PNG", 90, new[] { 1.0, 0.5, 2.0 }, trim: false);
                Assert.Equal(3, files.Count);
                Assert.EndsWith("pic.png", files[0]);
                Assert.EndsWith("pic@50pct.png", files[1]);
                Assert.EndsWith("pic@2x.png", files[2]);
                using var half = SKBitmap.Decode(files[1]);
                Assert.Equal((int)Math.Round(w * 0.5), half.Width);
                using var twice = SKBitmap.Decode(files[2]);
                Assert.Equal(w * 2, twice.Width);
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        });

        [Fact]
        public void LightingEffects_LightsTheCenterAndDimsTheRest()
        {
            using var source = new SKBitmap(100, 100, SKColorType.Rgba8888, SKAlphaType.Premul);
            source.Erase(new SKColor(160, 160, 160));
            var step = new Core.Actions.ActionStep { Kind = "Tone", Command = "LightingEffects" }
                .With("Kind", 1).With("Color", "#FFFFFF").With("X", 50).With("Y", 50).With("Radius", 40)
                .With("Intensity", 100).With("Ambient", 20).With("Texture", 30);
            using var lit = MainViewModel.FilterFor(step)!(source);
            Assert.True(lit.GetPixel(50, 50).Red > 150, $"center {lit.GetPixel(50, 50)}");
            Assert.True(lit.GetPixel(2, 2).Red < 50, $"corner {lit.GetPixel(2, 2)}");
        }
    }
}
