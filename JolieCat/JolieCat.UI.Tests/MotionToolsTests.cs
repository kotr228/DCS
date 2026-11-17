using JolieCat.Core.Animation;
using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class MotionToolsTests
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

        private static (DocumentViewModel Doc, Core.Documents.Layer Layer) Setup()
        {
            var doc = new DocumentViewModel(new ToolboxViewModel(), "M", ProjectType.ClipbarAnimation, 300, 200, null);
            var layer = doc.Layers.ActiveLayer!.Model;
            layer.Canvas.Clear(SKColors.Transparent);
            using (var red = new SKPaint { Color = SKColors.Red }) layer.Canvas.DrawRect(40, 90, 20, 20, red);   // middle at (50, 100)
            doc.Timeline.AnimateActiveLayerCommand.Execute(null);
            return (doc, layer);
        }

        [Fact]
        public void Wiggle_MovesWithoutKeys_StaysSmall_AndUndoes() => Sta(() =>
        {
            var (doc, layer) = Setup();
            var t = doc.Timeline;
            t.WiggleAmount = 10;
            var positions = new List<SKPoint>();
            for (var f = 0; f < 48; f++)
            {
                t.CurrentFrame = f;
                positions.Add(layer.AnimationMatrix?.MapPoint(new SKPoint(50, 100)) ?? new SKPoint(50, 100));
            }
            Assert.Contains(positions, p => Math.Abs(p.X - 50) > 2);                          // it moves
            Assert.All(positions, p => Assert.True(Math.Abs(p.X - 50) <= 10.5 && Math.Abs(p.Y - 100) <= 10.5));
            var steps = positions.Zip(positions.Skip(1), (a, b) => (b - a).Length).ToList();   // smoothly, no jumps
            Assert.True(steps.Max() < 12, $"max step {steps.Max()}");
            Assert.True(steps.Average() < 6, $"average step {steps.Average()}");
            doc.Layers.History.Undo();
            Assert.Equal(0, t.WiggleAmount);
        });

        [Fact]
        public void MoveAlongPath_FollowsTheCurve_AndTurns() => Sta(() =>
        {
            var (doc, layer) = Setup();
            var t = doc.Timeline;
            t.CurrentFrame = 0;
            using var path = new SKPath();
            path.MoveTo(50, 100);
            path.LineTo(250, 100);
            path.LineTo(250, 180);                          // then down
            Assert.True(t.MoveAlongPath(path, 28, orient: true));
            t.CurrentFrame = 10;                            // 200 of 280 px gone: on the first leg
            var mid = layer.AnimationMatrix!.Value.MapPoint(new SKPoint(50, 100));
            Assert.Equal(50 + 200f * 10 / 28 * 280 / 200, mid.X, 1);
            Assert.Equal(100, mid.Y, 1);
            t.CurrentFrame = 28;                            // the end, facing down
            var end = layer.AnimationMatrix!.Value.MapPoint(new SKPoint(50, 100));
            Assert.Equal(250, end.X, 1);
            Assert.Equal(180, end.Y, 1);
            Assert.Equal(90, t.PoseRotation, 1);
            doc.Layers.History.Undo();
            Assert.Single(t.SelectedTrack!.Keyframes);
        });

        [Fact]
        public void ReverseAndStretch_Keys() => Sta(() =>
        {
            var (doc, _) = Setup();
            var t = doc.Timeline;
            t.CurrentFrame = 0; t.PoseX = 1; t.PoseX = 0; t.PoseEasing = KeyEasing.EaseIn;
            t.CurrentFrame = 10; t.PoseX = 100;
            t.ReverseKeysCommand.Execute(null);
            var keys = t.SelectedTrack!.Keyframes.OrderBy(k => k.Frame).ToList();
            Assert.Equal(100, keys[0].Pose.X);
            Assert.Equal(0, keys[1].Pose.X);
            Assert.Equal(KeyEasing.EaseOut, keys[0].Pose.Easing);     // a gentle start, played backwards

            t.StretchKeys(200);
            keys = t.SelectedTrack!.Keyframes.OrderBy(k => k.Frame).ToList();
            Assert.Equal(new[] { 0.0, 20.0 }, keys.Select(k => k.Frame));
            doc.Layers.History.Undo();
            Assert.Equal(10, t.SelectedTrack!.Keyframes.Max(k => k.Frame));
        });
    }
}
