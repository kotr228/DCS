using System.IO;
using JolieCat.Shared.Enums;
using JolieCat.UI.Media;
using JolieCat.UI.ViewModels;
using JolieCat.UI.ViewModels.ToolOptions;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class PictureBrushTests
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

        /// <summary>A black ring (hollow square) on white.</summary>
        private static SKBitmap Ring()
        {
            var b = new SKBitmap(60, 60, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var c = new SKCanvas(b);
            c.Clear(SKColors.White);
            using var black = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 6 };
            c.DrawRect(15, 15, 30, 30, black);
            return b;
        }

        [Fact]
        public void Tip_IsTrimmedAndInverted_AndSavedAndLoaded()
        {
            using var ring = Ring();
            using var tip = BrushLibrary.MakeTip(ring)!;
            Assert.Equal(SKColorType.Alpha8, tip.ColorType);
            Assert.InRange(tip.Width, 35, 37);                         // trimmed to the ring
            Assert.Null(BrushLibrary.MakeTip(new SKBitmap(10, 10)));   // nothing dark: no tip

            var folder = Path.Combine(Path.GetTempPath(), $"brushes-{Guid.NewGuid():N}");
            var library = new BrushLibrary(Path.Combine(folder, "brushes.json"));
            var preset = library.SaveTip("Ring", tip);
            Assert.True(File.Exists(preset.TipFile));
            using var back = BrushLibrary.LoadTip(preset.TipFile)!;
            Assert.Equal(tip.Width, back.Width);
            Assert.Equal("Ring", new BrushLibrary(Path.Combine(folder, "brushes.json")).Find("Ring")!.Name);
            Directory.Delete(folder, true);
        }

        [Fact]
        public void PaintingWithAPictureTip_StampsItsShape() => Sta(() =>
        {
            var toolbox = new ToolboxViewModel();
            var doc = new DocumentViewModel(toolbox, "B", ProjectType.StandardImage, 200, 200, null);
            var canvas = doc.Canvas;
            SKPoint Device(float x, float y) => new((float)(x * canvas.Zoom + canvas.PanX), (float)(y * canvas.Zoom + canvas.PanY));
            toolbox.SelectTool(ToolType.Brush);
            var options = (PaintToolOptionsViewModel)toolbox.CurrentToolOptions;
            using (var ring = Ring()) options.UseTip("Ring", BrushLibrary.MakeTip(ring)!);
            options.Size = 60;
            canvas.PrimaryColor = System.Windows.Media.Colors.Red;
            var layer = doc.Layers.Scene.Layers[0];
            layer.Canvas.Clear(SKColors.Transparent);

            canvas.OnPointerPressed(Device(100, 100));
            canvas.OnPointerReleased(Device(100, 100));
            var bitmap = doc.Layers.Scene.Layers[0].Bitmap;
            Assert.Equal(0, bitmap.GetPixel(100, 100).Alpha);           // the ring's hole stays empty
            var edge = bitmap.GetPixel(100, 72);                        // on the ring (60 px tip: ring ~28 px from the middle)
            Assert.True(edge.Alpha > 150 && edge.Red > 150, edge.ToString());

            options.UseRoundTipCommand.Execute(null);
            Assert.Null(options.Tip);
            Assert.Null(options.TipName);
        });
    }
}
