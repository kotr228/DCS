using JolieCat.Core.Documents;
using JolieCat.Core.Effects;
using JolieCat.Core.Rendering;
using SkiaSharp;

namespace JolieCat.Tests
{
    public class GroupStyleTests
    {
        /// <summary>White background; a group holding two touching squares:
        /// red 10..20 and green 20..30 (both rows 10..20), on a 40x30 canvas.</summary>
        private static Scene Scene(out Layer group)
        {
            var scene = new Scene("S");
            scene.AddLayer("Bg", 40, 30).Canvas.Clear(SKColors.White);
            var red = scene.AddLayer("Red", 40, 30);
            TestHelpers.FillRect(red, new SKRect(10, 10, 20, 20), SKColors.Red);
            var green = scene.AddLayer("Green", 40, 30);
            TestHelpers.FillRect(green, new SKRect(20, 10, 30, 20), SKColors.Lime);
            group = scene.GroupLayers(new[] { red, green }, "G");
            return scene;
        }

        [Fact]
        public void GroupStroke_OutlinesTheWholeGroup_NotEachLayer()
        {
            using var scene = Scene(out var group);
            group.Style = new LayerStyle { Stroke = true, StrokeColor = SKColors.Blue, StrokeSize = 2 };
            using var flat = SceneCompositor.Flatten(scene, 40, 30);

            Assert.Equal(SKColors.Blue, flat.GetPixel(9, 15));    // outside the left edge
            Assert.Equal(SKColors.Blue, flat.GetPixel(31, 15));   // outside the right edge
            Assert.Equal(SKColors.Red, flat.GetPixel(19, 15));    // no stroke where the layers meet
            Assert.Equal(SKColors.Lime, flat.GetPixel(20, 15));
            Assert.Equal(SKColors.White, flat.GetPixel(2, 2));
        }

        [Fact]
        public void GroupColorOverlay_RecolorsEverythingInside()
        {
            using var scene = Scene(out var group);
            group.Style = new LayerStyle { ColorOverlay = true, OverlayColor = SKColors.Black };
            using var flat = SceneCompositor.Flatten(scene, 40, 30);

            Assert.Equal(SKColors.Black, flat.GetPixel(15, 15));
            Assert.Equal(SKColors.Black, flat.GetPixel(25, 15));
            Assert.Equal(SKColors.White, flat.GetPixel(35, 15));
        }

        [Fact]
        public void GroupStyle_RespectsGroupOpacity()
        {
            using var scene = Scene(out var group);
            group.Style = new LayerStyle { ColorOverlay = true, OverlayColor = SKColors.Black };
            group.Opacity = 0.5;
            using var flat = SceneCompositor.Flatten(scene, 40, 30);

            Assert.InRange(flat.GetPixel(15, 15).Red, 120, 135);
        }

        [Fact]
        public void MergingAStyledGroup_KeepsTheStyleLive()
        {
            using var scene = Scene(out var group);
            var style = new LayerStyle { Stroke = true, StrokeColor = SKColors.Blue, StrokeSize = 2 };
            group.Style = style;
            using var before = SceneCompositor.Flatten(scene, 40, 30);

            var merged = scene.MergeGroup(group);
            Assert.NotNull(merged);
            Assert.Equal(style, merged!.Style);
            using var after = SceneCompositor.Flatten(scene, 40, 30);
            Assert.Equal(before.Pixels, after.Pixels);
        }
    }
}
