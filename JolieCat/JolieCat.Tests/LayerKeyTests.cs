using JolieCat.Core.Animation;
using SkiaSharp;

namespace JolieCat.Tests
{
    public class LayerKeyTests
    {
        [Fact]
        public void At_BlendsBetweenKeys_AndHoldsOutside()
        {
            var keys = new[] { new LayerKey(0, X: 0, Easing: KeyEasing.Linear), new LayerKey(10, X: 100, Opacity: 0) };
            Assert.Equal(50, LayerKey.At(keys, 5).X, 3);       // linear halfway
            Assert.Equal(50, LayerKey.At(keys, 5).Opacity, 3);
            Assert.Equal(0, LayerKey.At(keys, -3).X);          // held before
            Assert.Equal(100, LayerKey.At(keys, 30).X);        // held after
        }

        [Fact]
        public void At_EaseStartsAndEndsSlowly()
        {
            var keys = new[] { new LayerKey(0, X: 0, Easing: KeyEasing.EaseInOut), new LayerKey(10, X: 100) };
            Assert.True(LayerKey.At(keys, 1).X < 10);           // slower than linear at the start
            Assert.Equal(50, LayerKey.At(keys, 5).X, 3);        // symmetric
        }

        [Fact]
        public void CustomCurve_FollowsItsHandles_AndCanOvershoot()
        {
            Assert.Equal(0.5, LayerKey.Bezier(0.5, 0, 0, 1, 1), 2);            // straight handles: linear
            Assert.True(LayerKey.Bezier(0.25, 0.42, 0, 1, 1) < 0.1);           // ease in: slow start
            var back = Enumerable.Range(1, 99).Max(i => LayerKey.Bezier(i / 100.0, 0.34, 1.56, 0.64, 1));
            Assert.True(back > 1.05, $"overshoot {back}");                    // goes past the target, then back

            var keys = new[]
            {
                new LayerKey(0, X: 0, Easing: KeyEasing.Custom) { CurveX1 = 0.42, CurveY1 = 0, CurveX2 = 1, CurveY2 = 1 },
                new LayerKey(10, X: 100),
            };
            Assert.True(LayerKey.At(keys, 2.5).X < 10);                        // the key's own curve is used
            Assert.Equal(0.42, LayerKey.At(keys, 5).CurveX1, 3);               // and kept in the pose
        }

        [Fact]
        public void ToMatrix_TurnsAndScalesAroundThePivot()
        {
            var pivot = new SKPoint(50, 50);
            var turned = new LayerKey(0, Rotation: 90).ToMatrix(pivot);
            var p = turned.MapPoint(new SKPoint(60, 50));       // 10 px right of the pivot -> 10 px below
            Assert.Equal(50, p.X, 2);
            Assert.Equal(60, p.Y, 2);
            Assert.True(SKPoint.Distance(pivot, turned.MapPoint(pivot)) < 0.001f);

            var moved = new LayerKey(0, X: 5, Y: -5, Scale: 200).ToMatrix(pivot).MapPoint(new SKPoint(60, 50));
            Assert.Equal(75, moved.X, 2);                       // 2x farther, then +5
            Assert.Equal(45, moved.Y, 2);
        }
    }
}
