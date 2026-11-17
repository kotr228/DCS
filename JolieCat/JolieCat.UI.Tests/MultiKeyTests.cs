using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class MultiKeyTests
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

        /// <summary>A layer track with keys at 0, 10 and 20 (X = 0, 50, 100).</summary>
        private static DocumentViewModel Animated()
        {
            var doc = new DocumentViewModel(new ToolboxViewModel(), "A", ProjectType.ClipbarAnimation, 100, 80, null);
            var t = doc.Timeline;
            using (var red = new SKPaint { Color = SKColors.Red })
                doc.Layers.ActiveLayer!.Model.Canvas.DrawRect(40, 30, 20, 20, red);
            t.AnimateActiveLayerCommand.Execute(null);
            t.CurrentFrame = 10; t.PoseX = 50;
            t.CurrentFrame = 20; t.PoseX = 100;
            return doc;
        }

        [Fact]
        public void ShiftClickedKeys_MoveTogether_AndDeleteTogether() => Sta(() =>
        {
            var doc = Animated();
            var t = doc.Timeline;
            var keys = t.Tracks[0].Keyframes.OrderBy(k => k.Frame).ToList();
            t.BeginKeyDrag(keys[1]); t.EndKeyDrag();                    // select the middle key
            t.BeginKeyDrag(keys[2], addToSelection: true); t.EndKeyDrag();
            Assert.Equal(2, t.SelectedKeys.Count);

            t.BeginKeyDrag(keys[1]);
            t.DragKeyBy(t.PixelsPerFrame * 5);
            t.EndKeyDrag();
            Assert.Equal(new double[] { 0, 15, 25 }, t.Tracks[0].Keyframes.Select(k => k.Frame).OrderBy(f => f));

            doc.Layers.History.Undo();
            Assert.Equal(new double[] { 0, 10, 20 }, t.Tracks[0].Keyframes.Select(k => k.Frame).OrderBy(f => f));

            foreach (var k in t.Tracks[0].Keyframes.Where(k => k.Frame > 0)) k.IsSelected = true;
            t.DeleteSelectedKeysCommand.Execute(null);
            Assert.Single(t.Tracks[0].Keyframes);
        });

        [Fact]
        public void CopiedKeys_PasteAtThePlayhead_KeepingTheirSpacing() => Sta(() =>
        {
            var doc = Animated();
            var t = doc.Timeline;
            foreach (var k in t.Tracks[0].Keyframes) k.IsSelected = k.Frame <= 10;   // keys 0 and 10
            t.CopyKeysCommand.Execute(null);
            t.CurrentFrame = 30;
            t.PasteKeysCommand.Execute(null);
            Assert.Equal(new double[] { 0, 10, 20, 30, 40 }, t.Tracks[0].Keyframes.Select(k => k.Frame).OrderBy(f => f));
            t.CurrentFrame = 40;
            Assert.Equal(50, t.PoseX, 1);                                // the pose of the copied frame-10 key
        });

        [Fact]
        public void MotionPath_FollowsTheLayer() => Sta(() =>
        {
            var doc = Animated();
            var t = doc.Timeline;
            t.CurrentFrame = 10;
            var path = t.MotionPath();
            Assert.Equal(21, path.Count);                                // frames 0..20
            Assert.Equal(50, path[0].Point.X, 1);                        // the square's middle
            Assert.Equal(150, path[20].Point.X, 1);                      // moved 100 px
            Assert.True(path[10].IsKey && path[10].IsCurrent);
            Assert.False(path[5].IsKey);
        });
    }
}
