using System.IO;
using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using JolieCat.UI.ViewModels.Timeline;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class FrameEditingTests
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

        private static DocumentViewModel ThreeFrames()
        {
            var doc = new DocumentViewModel(new ToolboxViewModel(), "A", ProjectType.ClipbarAnimation, 40, 30, null);
            for (var i = 0; i < 3; i++)
            {
                doc.Timeline.NewFrameCommand.Execute(null);
                using var paint = new SKPaint { Color = new[] { SKColors.Red, SKColors.Lime, SKColors.Blue }[i] };
                doc.Layers.ActiveLayer!.Model.Canvas.DrawRect(0, 0, 40, 30, paint);
            }
            doc.Timeline.RefreshFrameStrip();
            return doc;
        }

        [Fact]
        public void HoldAndMove_FromTheFrameStrip() => Sta(() =>
        {
            var doc = ThreeFrames();
            var t = doc.Timeline;
            t.CurrentFrame = 1;                                         // frame 2 shown and being drawn on
            t.FrameStrip[0].HoldCommand.Execute(3);                    // frame 1 shows for 3
            Assert.Same(t.FrameClips[0].TargetLayer, doc.Layers.ActiveLayer!.Model); // frame 1 shows at 1 now: draw there
            Assert.Equal(new double[] { 0, 3, 4 }, t.FrameClips.Select(c => c.StartFrame));
            Assert.Equal(5, t.PlaybackEnd);

            var blue = t.FrameClips[2].TargetLayer;
            t.FrameStrip[2].MoveEarlierCommand.Execute(null);          // blue before lime
            Assert.Same(blue, t.FrameClips[1].TargetLayer);
            Assert.Equal(new double[] { 0, 3, 4 }, t.FrameClips.Select(c => c.StartFrame));

            doc.Layers.History.Undo();                                  // the move
            Assert.NotSame(blue, t.FrameClips[1].TargetLayer);
            doc.Layers.History.Undo();                                  // the hold
            Assert.Equal(1, t.FrameClips[0].LengthFrames);
        });

        [Fact]
        public void LoopModes_Once_And_PingPong() => Sta(() =>
        {
            var doc = ThreeFrames();
            var t = doc.Timeline;
            t.LoopMode = PlaybackLoop.Once;
            t.CurrentFrame = 0;
            t.PlayCommand.Execute(null);
            t.AdvancePlayback(); t.AdvancePlayback(); t.AdvancePlayback();
            Assert.Equal(2, t.CurrentFrame);                            // stopped on the last frame
            Assert.False(t.IsPlaying);

            t.LoopMode = PlaybackLoop.PingPong;
            t.CurrentFrame = 0;
            t.PlayCommand.Execute(null);
            var seen = new List<double>();
            for (var i = 0; i < 6; i++) { t.AdvancePlayback(); seen.Add(t.CurrentFrame); }
            Assert.Equal(new double[] { 1, 2, 1, 0, 1, 2 }, seen);
            t.PauseCommand.Execute(null);
        });

        [Fact]
        public void AnimatedPng_IsExported_WithTransparency() => Sta(() =>
        {
            var doc = ThreeFrames();
            doc.Layers.Scene.Layers[0].IsVisible = false;                  // no background: transparent
            var file = Path.Combine(Path.GetTempPath(), "jc_apng_" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                Assert.Equal(3, MainViewModel.ExportAnimationTo(doc, "Animated PNG (transparent)", 1, file, "a"));
                var text = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(file));
                Assert.Contains("acTL", text);
                using var first = SKBitmap.Decode(file);
                Assert.Equal(SKColors.Red, first.GetPixel(10, 10));
            }
            finally
            {
                File.Delete(file);
            }
        });

        [Fact]
        public void Gif_FollowsTheLoopMode() => Sta(() =>
        {
            var doc = ThreeFrames();
            var gif = Path.Combine(Path.GetTempPath(), "jc_loop_" + Guid.NewGuid().ToString("N") + ".gif");
            try
            {
                doc.Timeline.LoopMode = PlaybackLoop.PingPong;
                MainViewModel.ExportAnimationTo(doc, "Animated GIF", 1, gif, "a");
                using (var codec = SKCodec.Create(gif))
                    Assert.Equal(4, codec.FrameCount);                  // 1 2 3 2 (then back to 1)

                doc.Timeline.LoopMode = PlaybackLoop.Once;
                MainViewModel.ExportAnimationTo(doc, "Animated GIF", 1, gif, "a");
                // Plays once: by the GIF standard (and in browsers) a GIF without the
                // NETSCAPE repeat block plays a single time.
                Assert.DoesNotContain("NETSCAPE", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(gif)));
            }
            finally
            {
                File.Delete(gif);
            }
        });
    }
}
