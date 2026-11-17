using System.IO;
using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class FrameAnimationTests
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

        private static DocumentViewModel NewAnimation()
        {
            var doc = new DocumentViewModel(new ToolboxViewModel(), "Anim", ProjectType.ClipbarAnimation, 64, 48, null);
            Assert.Empty(doc.Timeline.Tracks);   // no demo tracks any more
            return doc;
        }

        private static void Paint(Core.Documents.Layer layer, SKColor color)
        {
            using var paint = new SKPaint { Color = color };
            layer.Canvas.DrawRect(10, 10, 20, 20, paint);
        }

        [Fact]
        public void NewDuplicateDelete_FramesShowOneAtATime() => Sta(() =>
        {
            var doc = NewAnimation();
            var timeline = doc.Timeline;
            var background = doc.Layers.ActiveLayer!.Model;
            timeline.NewFrameCommand.Execute(null);           // frame 1 (the background stays a background)
            Assert.Single(timeline.FrameClips);
            var first = doc.Layers.ActiveLayer!.Model;
            Assert.NotSame(background, first);
            Paint(first, SKColors.Red);

            timeline.NewFrameCommand.Execute(null);           // frame 2: blank
            Assert.Equal(2, timeline.FrameClips.Count);
            Assert.Equal(1, timeline.CurrentFrame);
            var second = doc.Layers.ActiveLayer!.Model;        // the new frame is the one to draw on
            Assert.NotSame(first, second);
            Assert.False(first.IsVisible);
            Paint(second, SKColors.Blue);

            timeline.DuplicateFrameCommand.Execute(null);     // frame 3: copy of frame 2
            var third = doc.Layers.ActiveLayer!.Model;
            Assert.Equal(SKColors.Blue, third.Bitmap.GetPixel(15, 15));
            Assert.Equal(3, timeline.PlaybackEnd);

            timeline.CurrentFrame = 0;
            Assert.True(first.IsVisible);
            Assert.False(second.IsVisible);
            Assert.True(background.IsVisible);                 // behind every frame
            Assert.Same(first, doc.Layers.ActiveLayer!.Model); // scrubbing picks that frame's layer

            timeline.CurrentFrame = 1;
            timeline.DeleteFrameCommand.Execute(null);
            Assert.Equal(2, timeline.FrameClips.Count);
            Assert.DoesNotContain(second, doc.Layers.Scene.Layers);
            Assert.Equal(1, timeline.FrameClips[1].StartFrame);  // frame 3 moved up
        });

        [Fact]
        public void OnionSkin_ListsTheNeighbours() => Sta(() =>
        {
            var doc = NewAnimation();
            var timeline = doc.Timeline;
            timeline.NewFrameCommand.Execute(null);
            timeline.NewFrameCommand.Execute(null);
            timeline.NewFrameCommand.Execute(null);
            timeline.CurrentFrame = 1;
            Assert.Empty(timeline.OnionSkinLayers());          // off by default

            timeline.OnionSkin = true;
            var shown = timeline.OnionSkinLayers();
            Assert.Equal(2, shown.Count);
            Assert.Contains(shown, s => s.Before && s.Layer == timeline.FrameClips[0].TargetLayer);
            Assert.Contains(shown, s => !s.Before && s.Layer == timeline.FrameClips[2].TargetLayer);
        });

        [Fact]
        public void ExportAnimation_WritesGifPngsAndSheet() => Sta(() =>
        {
            var doc = NewAnimation();
            var timeline = doc.Timeline;
            timeline.NewFrameCommand.Execute(null);
            Paint(doc.Layers.ActiveLayer!.Model, SKColors.Red);
            timeline.NewFrameCommand.Execute(null);
            Paint(doc.Layers.ActiveLayer!.Model, SKColors.Lime);
            timeline.FrameClips[1].LengthFrames = 3;           // the second picture is held for 3 frames
            timeline.FrameRate = 10;

            var folder = Path.Combine(Path.GetTempPath(), "jc_anim_" + Guid.NewGuid().ToString("N"));
            try
            {
                var gif = Path.Combine(folder, "a.gif");
                Directory.CreateDirectory(folder);
                Assert.Equal(2, MainViewModel.ExportAnimationTo(doc, "Animated GIF", 1, gif, "a"));
                using (var codec = SKCodec.Create(gif))
                {
                    Assert.Equal(2, codec.FrameCount);
                    Assert.Equal(100, codec.FrameInfo[0].Duration);
                    Assert.Equal(300, codec.FrameInfo[1].Duration);
                }

                var pngs = Path.Combine(folder, "seq");
                MainViewModel.ExportAnimationTo(doc, "PNG Sequence", 0.5, pngs, "a");
                Assert.Equal(4, Directory.GetFiles(pngs, "*.png").Length);     // one file per timeline frame
                using (var half = SKBitmap.Decode(Path.Combine(pngs, "a_000.png")))
                    Assert.Equal(32, half.Width);

                var sheet = Path.Combine(folder, "sheet.png");
                MainViewModel.ExportAnimationTo(doc, "Sprite Sheet (PNG)", 1, sheet, "a");
                using (var image = SKBitmap.Decode(sheet))
                {
                    Assert.Equal(128, image.Width);                            // 2 pictures side by side
                    Assert.Equal(SKColors.Lime, image.GetPixel(64 + 15, 15));
                }
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        });
    }
}
