using System.IO;
using JolieCat.Core.Serialization;
using JolieCat.Shared.Enums;
using JolieCat.UI.Media;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class FrameImportTests
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
        public void AnimatedGif_ImportsAsFrames_WithTheirLengths_AndUndoes() => Sta(() =>
        {
            var gif = Path.Combine(Path.GetTempPath(), "jc_in_" + Guid.NewGuid().ToString("N") + ".gif");
            var sheet = Path.Combine(Path.GetTempPath(), "jc_sheet_" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                var colors = new[] { SKColors.Red, SKColors.Lime, SKColors.Blue };
                var bitmaps = colors.Select(c => { var b = new SKBitmap(40, 30, SKColorType.Rgba8888, SKAlphaType.Premul); b.Erase(c); return b; }).ToList();
                using (var file = File.Create(gif)) GifEncoder.Write(file, bitmaps, new[] { 100, 200, 100 });
                foreach (var b in bitmaps) b.Dispose();

                var doc = new DocumentViewModel(new ToolboxViewModel(), "A", ProjectType.ClipbarAnimation, 80, 60, null);
                doc.Timeline.FrameRate = 10;
                var pictures = FrameDecoder.Decode(new[] { gif }, 10);
                Assert.Equal(new[] { 1, 2, 1 }, pictures.Select(p => p.Frames));
                doc.Timeline.ImportFrames(pictures, "in");
                foreach (var (p, _) in pictures) p.Dispose();

                var frames = doc.Timeline.FrameClips;
                Assert.Equal(3, frames.Count);
                Assert.Equal(2, frames[1].LengthFrames);
                Assert.Equal(SKColors.Lime, frames[1].TargetLayer!.Bitmap.GetPixel(40, 30));   // fitted, centered
                Assert.Equal(0, frames[1].TargetLayer!.Bitmap.GetPixel(5, 5).Alpha);           // smaller than the canvas
                Assert.Equal(3, doc.Timeline.FrameStrip.Count);
                Assert.All(doc.Timeline.FrameStrip, t => Assert.NotNull(t.Thumbnail));
                Assert.Equal("2  ×2", doc.Timeline.FrameStrip[1].Label);

                doc.Layers.History.Undo();
                Assert.Empty(doc.Timeline.FrameClips);
                Assert.Single(doc.Layers.Scene.Layers);
                doc.Layers.History.Redo();
                Assert.Equal(3, doc.Timeline.FrameClips.Count);
                Assert.Equal(SKColors.Blue, doc.Timeline.FrameClips[2].TargetLayer!.Bitmap.GetPixel(40, 30)); // pixels came back too

                Assert.Equal(3, MainViewModel.ExportAnimationTo(doc, "Sprite Sheet (PNG)", 1, sheet, "A"));
                var json = File.ReadAllText(Path.ChangeExtension(sheet, ".json"));
                Assert.Contains("\"durationMs\": 200", json);
                Assert.Contains("\"frameWidth\": 80", json);
            }
            finally
            {
                File.Delete(gif);
                File.Delete(sheet);
                File.Delete(Path.ChangeExtension(sheet, ".json"));
            }
        });
    }
}
