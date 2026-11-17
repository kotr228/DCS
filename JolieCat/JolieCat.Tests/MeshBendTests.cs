using JolieCat.Core.Animation;
using SkiaSharp;
using Xunit;

namespace JolieCat.Tests
{
    public class MeshBendTests
    {
        [Fact]
        public void Map_LeavesTheUpperPartAlone_AndTurnsThePartPastTheJoint()
        {
            var root = new SKPoint(0, 50);
            var joint = new SKPoint(100, 50);
            var near = MeshBend.Map(new SKPoint(20, 50), root, joint, 90, 10);
            Assert.Equal(20, near.X, 3);
            Assert.Equal(50, near.Y, 3);
            var far = MeshBend.Map(new SKPoint(150, 50), root, joint, 90, 10);
            Assert.Equal(100, far.X, 2);                    // 50 px past the elbow, now pointing down
            Assert.Equal(100, far.Y, 2);
            var middle = MeshBend.Map(new SKPoint(100, 50), root, joint, 90, 10);
            Assert.Equal(100, middle.X, 3);                 // the joint itself stays
        }

        [Fact]
        public void Bend_MovesTheForearmsPixels()
        {
            // A horizontal arm from x 0 to 160 at y 40..60; the elbow at x 80.
            using var arm = new SKBitmap(200, 200, SKColorType.Rgba8888, SKAlphaType.Premul);
            arm.Erase(SKColors.Transparent);
            using (var c = new SKCanvas(arm))
            using (var red = new SKPaint { Color = SKColors.Red })
                c.DrawRect(0, 40, 160, 20, red);
            using var bent = MeshBend.Bend(arm, new SKPoint(0, 50), new SKPoint(80, 50), 90);
            Assert.Equal(255, bent.GetPixel(20, 50).Alpha);   // the upper arm stays
            Assert.True(bent.GetPixel(150, 50).Alpha < 10);   // the hand isn't out there any more
            Assert.Equal(255, bent.GetPixel(80, 120).Alpha);  // it points down from the elbow
        }
    }
}
