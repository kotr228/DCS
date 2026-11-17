using System.IO;
using JolieCat.UI.Media;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class VideoExportTests
    {
        [Fact]
        public async Task AnimationDocument_ExportsAsMp4()
        {
            var path = Path.Combine(Path.GetTempPath(), "jc_anim_" + Guid.NewGuid().ToString("N") + ".mp4");
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var doc = new JolieCat.UI.ViewModels.DocumentViewModel(new JolieCat.UI.ViewModels.ToolboxViewModel(), "A",
                        JolieCat.Shared.Enums.ProjectType.ClipbarAnimation, 120, 90, null);
                    doc.Timeline.NewFrameCommand.Execute(null);
                    doc.Timeline.NewFrameCommand.Execute(null);
                    doc.Timeline.NewFrameCommand.Execute(null);
                    // No UI thread message loop here: run the async export to completion on a worker.
                    var written = Task.Run(() => JolieCat.UI.ViewModels.MainViewModel.ExportAnimationToAsync(doc, "MP4 Video", 1, path, "A")).GetAwaiter().GetResult();
                    Assert.Equal(3, written);
                }
                catch (Exception ex) { error = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            try
            {
                if (error is not null) throw new Exception(error.Message, error);
                var bytes = File.ReadAllBytes(path);
                Assert.Equal("ftyp", System.Text.Encoding.ASCII.GetString(bytes, 4, 4));
            }
            finally
            {
                File.Delete(path);
            }
            await Task.CompletedTask;
        }

        [Fact]
        public async Task Mp4_IsWritten()
        {
            var pictures = new List<(SKBitmap, int)>();
            foreach (var color in new[] { SKColors.Red, SKColors.Lime, SKColors.Blue })
            {
                var frame = new SKBitmap(161, 120, SKColorType.Rgba8888, SKAlphaType.Premul); // odd width on purpose
                frame.Erase(color);
                pictures.Add((frame, 4));
            }

            var path = Path.Combine(Path.GetTempPath(), "jc_video_" + Guid.NewGuid().ToString("N") + ".mp4");
            try
            {
                await VideoExporter.WriteMp4Async(path, pictures, 12);
                var bytes = File.ReadAllBytes(path);
                Assert.True(bytes.Length > 1000, $"{bytes.Length} bytes");
                Assert.Equal("ftyp", System.Text.Encoding.ASCII.GetString(bytes, 4, 4)); // an MP4 file
            }
            finally
            {
                File.Delete(path);
                foreach (var (picture, _) in pictures) picture.Dispose();
            }
        }
    }
}
