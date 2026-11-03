using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using JolieCat.UI.ViewModels.ToolOptions;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class MultiShotAndStampTests
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

        private static void DrawScene(SKCanvas canvas, int dx, int dy, byte light)
        {
            canvas.Clear(new SKColor(40, 40, 40));
            using var paint = new SKPaint { Color = new SKColor(light, light, light), IsAntialias = true };
            canvas.DrawCircle(500 + dx, 400 + dy, 150, paint);
            canvas.DrawRect(900 + dx, 300 + dy, 300, 500, paint);
        }

        [Fact]
        public void AutoAlign_LinesUpAShiftedShot_AndMergeToHdrAddsALayer() => Sta(() =>
        {
            var doc = new DocumentViewModel(new ToolboxViewModel(), "T");
            var layers = doc.Layers;
            DrawScene(layers.Scene.Layers[0].Canvas, 0, 0, 200);
            layers.AddLayerCommand.Execute(null);
            var top = layers.Scene.Layers[^1];
            DrawScene(top.Canvas, 24, -16, 240);

            layers.AutoAlignLayersCommand.Execute(null);
            Assert.StartsWith("Aligned", layers.LastMultiShotMessage);
            Assert.Contains("(-24, 16)", layers.LastMultiShotMessage);
            Assert.Equal(240, top.Bitmap.GetPixel(500, 400).Red);       // circle back in place
            layers.History.Undo();
            Assert.Equal(240, layers.Scene.Layers[^1].Bitmap.GetPixel(524, 384).Red);

            layers.MergeToHdrProCommand.Execute(null);
            Assert.Equal("Merged HDR", layers.Scene.Layers[^1].Name);
            Assert.False(layers.Scene.Layers[0].IsVisible);
        });

        [Fact]
        public void AutoAlign_NeedsTwoLayers() => Sta(() =>
        {
            var doc = new DocumentViewModel(new ToolboxViewModel(), "T");
            doc.Layers.AutoAlignLayersCommand.Execute(null);
            Assert.Contains("at least two", doc.Layers.LastMultiShotMessage);
            Assert.False(doc.Layers.History.CanUndo);
        });

        [Fact]
        public void PatternStamp_AndMixerBrush_Paint() => Sta(() =>
        {
            var toolbox = new ToolboxViewModel();
            var doc = new DocumentViewModel(toolbox, "T");
            var layer = doc.Layers.ActiveLayer!.Model;
            layer.Canvas.Clear(SKColors.White);
            var canvas = doc.Canvas;
            canvas.SetViewportSize(800, 600);
            SKPoint Device(float x, float y) => new((float)(x * canvas.Zoom + canvas.PanX), (float)(y * canvas.Zoom + canvas.PanY));
            void Drag(float y)
            {
                canvas.OnPointerPressed(Device(100, y));
                for (var x = 110; x <= 400; x += 10) canvas.OnPointerMoved(Device(x, y));
                canvas.OnPointerReleased(Device(400, y));
            }

            toolbox.SelectTool(ToolType.PatternStamp);
            var stamp = (PaintToolOptionsViewModel)toolbox.CurrentToolOptions;
            Assert.True(stamp.ShowPattern);
            stamp.Size = 60; stamp.Hardness = 100;
            stamp.PatternName = "Checker";
            Drag(200);
            var colors = Enumerable.Range(150, 200).Select(x => layer.Bitmap.GetPixel(x, 200)).Distinct().Count();
            Assert.True(colors > 1, "the pattern shows (not one flat color)");

            toolbox.SelectTool(ToolType.MixerBrush);
            var mixer = (PaintToolOptionsViewModel)toolbox.CurrentToolOptions;
            mixer.Size = 40; mixer.Hardness = 100; mixer.Wet = 60;
            canvas.PrimaryColor = System.Windows.Media.Colors.Red;
            Drag(400);
            var start = layer.Bitmap.GetPixel(105, 400);
            var end = layer.Bitmap.GetPixel(395, 400);
            Assert.True(start.Green < 150, $"starts red {start}");
            Assert.True(end.Green > start.Green + 40, $"picks up the white canvas {end}");
        });
    }
}
