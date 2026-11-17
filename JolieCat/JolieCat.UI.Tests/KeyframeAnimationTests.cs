using System.IO;
using JolieCat.Core.Rendering;
using JolieCat.Core.Serialization;
using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class KeyframeAnimationTests
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
        public void KeyframesMoveAndFadeTheLayer_WithoutChangingItsPixels() => Sta(() =>
        {
            var doc = new DocumentViewModel(new ToolboxViewModel(), "Anim", ProjectType.ClipbarAnimation, 100, 60, null);
            var layer = doc.Layers.ActiveLayer!.Model;
            layer.Canvas.Clear(SKColors.Transparent);
            using (var red = new SKPaint { Color = SKColors.Red })
                layer.Canvas.DrawRect(0, 20, 20, 20, red);       // a red square at the left
            var timeline = doc.Timeline;

            timeline.AnimateActiveLayerCommand.Execute(null);
            var track = Assert.Single(timeline.Tracks);
            Assert.Same(track, timeline.SelectedTrack);
            Assert.Single(track.Keyframes);

            timeline.CurrentFrame = 10;
            timeline.PoseX = 60;                                 // auto-key at frame 10
            timeline.PoseEasing = Core.Animation.KeyEasing.Linear;
            Assert.Equal(2, track.Keyframes.Count);
            Assert.True(timeline.HasKeyAtPlayhead);
            timeline.PoseOpacity = 50;
            Assert.Equal(2, track.Keyframes.Count);              // same key updated
            Assert.Equal(11, timeline.PlaybackEnd);

            timeline.CurrentFrame = 0;
            Assert.Null(layer.AnimationMatrix);                  // frame 0: as drawn
            timeline.CurrentFrame = 10;
            using (var flat = SceneCompositor.Flatten(doc.Layers.Scene, 100, 60))
            {
                Assert.Equal(0, flat.GetPixel(10, 30).Alpha);    // moved away
                var moved = flat.GetPixel(70, 30);
                Assert.InRange(moved.Alpha, 120, 135);           // 50% opacity
            }
            Assert.Equal(255, layer.Bitmap.GetPixel(10, 30).Alpha); // pixels untouched

            timeline.CurrentFrame = 5;
            Assert.Equal(30, timeline.PoseX, 1);                 // halfway (frame 0's key is eased, so check the panel's value)
        });

        [Fact]
        public void Animation_SurvivesSaveAndLoad_AndExportsEveryFrame() => Sta(() =>
        {
            var doc = new DocumentViewModel(new ToolboxViewModel(), "Anim", ProjectType.ClipbarAnimation, 40, 30, null);
            var layer = doc.Layers.ActiveLayer!.Model;
            layer.Name = "Ball";
            var timeline = doc.Timeline;
            timeline.AnimateActiveLayerCommand.Execute(null);
            timeline.CurrentFrame = 4;
            timeline.PoseRotation = 45;

            var data = new[]
            {
                new TimelineTrackData
                {
                    Name = timeline.Tracks[0].Name, LayerName = "Ball",
                    Keys = timeline.Tracks[0].Keyframes.Select(k => new KeyframeData { Frame = k.Frame, Rotation = k.Pose.Rotation, Scale = 100, Opacity = 100, Easing = k.Pose.Easing.ToString() }).ToList(),
                },
            };
            var reopened = new DocumentViewModel(new ToolboxViewModel(), "Again", ProjectType.ClipbarAnimation, 40, 30, null);
            reopened.Layers.ActiveLayer!.Model.Name = "Ball";
            reopened.Timeline.LoadTracks(data, 240, 24);
            reopened.Timeline.RewireFrameLayers(reopened.Layers.Scene);
            reopened.Timeline.CurrentFrame = 4;
            Assert.NotNull(reopened.Layers.ActiveLayer!.Model.AnimationMatrix);

            var gif = Path.Combine(Path.GetTempPath(), "jc_key_" + Guid.NewGuid().ToString("N") + ".gif");
            try
            {
                timeline.CurrentFrame = 0;
                Assert.Equal(5, MainViewModel.ExportAnimationTo(doc, "Animated GIF", 1, gif, "a")); // frames 0..4 all differ
            }
            finally
            {
                File.Delete(gif);
            }
        });
    }
}
