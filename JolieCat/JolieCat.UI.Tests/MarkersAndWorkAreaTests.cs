using System.IO;
using JolieCat.Core.History;
using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class MarkersAndWorkAreaTests
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

        private static DocumentViewModel Frames(int count)
        {
            var doc = new DocumentViewModel(new ToolboxViewModel(), "A", ProjectType.ClipbarAnimation, 40, 30, null);
            for (var i = 0; i < count; i++)
            {
                doc.Timeline.NewFrameCommand.Execute(null);
                using var paint = new SKPaint { Color = new SKColor((byte)(i * 30), 100, 200) };
                doc.Layers.ActiveLayer!.Model.Canvas.DrawRect(0, 0, 40, 30, paint);
            }
            doc.Timeline.RefreshFrameStrip();
            return doc;
        }

        [Fact]
        public void Markers_AreAddedAndJumpedTo_AndDontLengthenTheAnimation() => Sta(() =>
        {
            var doc = Frames(6);
            var t = doc.Timeline;
            t.CurrentFrame = 1; t.AddMarker("step");
            t.CurrentFrame = 4; t.AddMarker("");
            t.CurrentFrame = 30; t.AddMarker("far");               // past the end: must not lengthen it
            Assert.Equal(6, t.PlaybackEnd);
            var markers = t.Tracks.Single(tr => tr.IsMarkers);
            Assert.Equal(new[] { "step", "Marker 2", "far" }, markers.Clips.Select(c => c.Name));
            Assert.All(markers.Clips, c => Assert.True(c.IsMarker));

            t.CurrentFrame = 0;
            t.NextMarkerCommand.Execute(null); Assert.Equal(1, t.CurrentFrame);
            t.NextMarkerCommand.Execute(null); Assert.Equal(4, t.CurrentFrame);
            t.PreviousMarkerCommand.Execute(null); Assert.Equal(1, t.CurrentFrame);

            doc.Layers.History.Undo();
            Assert.Equal(2, t.Tracks.Single(tr => tr.IsMarkers).Clips.Count);
        });

        [Fact]
        public void WorkArea_LimitsPlaybackAndExport() => Sta(() =>
        {
            var doc = Frames(6);
            var t = doc.Timeline;
            t.CurrentFrame = 2; t.SetInPointCommand.Execute(null);
            t.CurrentFrame = 4; t.SetOutPointCommand.Execute(null);
            Assert.True(t.HasWorkArea);
            Assert.Equal(2, t.PlayStart);
            Assert.Equal(5, t.PlayEnd);

            t.CurrentFrame = 0;
            t.PlayCommand.Execute(null);
            Assert.Equal(2, t.CurrentFrame);                         // starts at In
            var seen = new List<double>();
            for (var i = 0; i < 4; i++) { t.AdvancePlayback(); seen.Add(t.CurrentFrame); }
            Assert.Equal(new double[] { 3, 4, 2, 3 }, seen);         // loops inside the work area
            t.PauseCommand.Execute(null);

            var gif = Path.Combine(Path.GetTempPath(), "jc_wa_" + Guid.NewGuid().ToString("N") + ".gif");
            try
            {
                Assert.Equal(3, MainViewModel.ExportAnimationTo(doc, "Animated GIF", 1, gif, "a")); // frames 3..5 only
            }
            finally { File.Delete(gif); }

            t.ClearWorkAreaCommand.Execute(null);
            Assert.Equal(0, t.PlayStart);
            Assert.Equal(6, t.PlayEnd);
        });

        [Fact]
        public void AStroke_RedoesOnlyThatFramesPicture() => Sta(() =>
        {
            var doc = Frames(4);
            var t = doc.Timeline;
            var before = t.FrameStrip.Select(f => f.Thumbnail).ToList();
            var layer = t.FrameClips[2].TargetLayer!;
            var pixels = layer.Bitmap.Pixels;
            using (var white = new SKPaint { Color = SKColors.White })
                layer.Canvas.DrawRect(0, 0, 10, 10, white);
            doc.Layers.History.Push(new LayerPixelsCommand(doc.Layers.Scene, layer, false, pixels, layer.Bitmap.Pixels));

            var after = t.FrameStrip.Select(f => f.Thumbnail).ToList();
            Assert.Same(before[0], after[0]);
            Assert.Same(before[1], after[1]);
            Assert.NotSame(before[2], after[2]);                     // only the drawn-on frame
            Assert.Same(before[3], after[3]);
        });
    }
}
