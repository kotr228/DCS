using JolieCat.Core.Documents;
using JolieCat.Core.Formats;
using JolieCat.Core.Rendering;
using SkiaSharp;

namespace JolieCat.Tests
{
    public class ClippingMaskTests
    {
        private const int W = 10, H = 10;

        /// <summary>White background; a base with a red square (2..6); a solid blue
        /// layer clipped to it.</summary>
        private static Scene ClippedScene(out Layer baseLayer, out Layer clipped)
        {
            var scene = new Scene("S");
            scene.AddLayer("Bg", W, H).Canvas.Clear(SKColors.White);
            baseLayer = scene.AddLayer("Base", W, H);
            TestHelpers.FillRect(baseLayer, new SKRect(2, 2, 6, 6), SKColors.Red);
            clipped = scene.AddLayer("Clipped", W, H);
            clipped.Canvas.Clear(SKColors.Blue);
            clipped.IsClipped = true;
            return scene;
        }

        [Fact]
        public void ClippedLayer_ShowsOnlyOverTheBase()
        {
            using var scene = ClippedScene(out _, out _);
            using var flat = SceneCompositor.Flatten(scene, W, H);

            Assert.Equal(SKColors.Blue, flat.GetPixel(3, 3));
            Assert.Equal(SKColors.White, flat.GetPixel(8, 8));
        }

        [Fact]
        public void ReleasedClip_CoversEverything()
        {
            using var scene = ClippedScene(out _, out var clipped);
            clipped.IsClipped = false;
            using var flat = SceneCompositor.Flatten(scene, W, H);

            Assert.Equal(SKColors.Blue, flat.GetPixel(8, 8));
        }

        [Fact]
        public void BaseOpacity_AppliesToTheWholeClippingGroup()
        {
            using var scene = ClippedScene(out var baseLayer, out _);
            baseLayer.Opacity = 0.5;
            using var flat = SceneCompositor.Flatten(scene, W, H);

            var c = flat.GetPixel(3, 3);
            Assert.InRange(c.Blue, 250, 255);
            Assert.InRange(c.Red, 120, 135); // blue at 50% over white
        }

        [Fact]
        public void HiddenBase_HidesTheClippedLayer()
        {
            using var scene = ClippedScene(out var baseLayer, out _);
            baseLayer.IsVisible = false;
            using var flat = SceneCompositor.Flatten(scene, W, H);

            Assert.Equal(SKColors.White, flat.GetPixel(3, 3));
        }

        [Fact]
        public void ClippedBlendMode_BlendsWithTheBase()
        {
            using var scene = ClippedScene(out _, out var clipped);
            clipped.Canvas.Clear(new SKColor(128, 128, 128));
            clipped.BlendMode = Shared.Enums.BlendMode.Multiply;
            using var flat = SceneCompositor.Flatten(scene, W, H);

            var c = flat.GetPixel(3, 3);
            Assert.InRange(c.Red, 120, 135); // red x gray
            Assert.InRange(c.Green, 0, 5);
            Assert.Equal(SKColors.White, flat.GetPixel(8, 8));
        }

        [Fact]
        public void MergeDown_KeepsOnlyTheClippedPart()
        {
            using var scene = ClippedScene(out var baseLayer, out var clipped);

            Assert.True(scene.MergeLayerDown(clipped));

            Assert.Equal(SKColors.Blue, baseLayer.Bitmap.GetPixel(3, 3));
            Assert.Equal(0, baseLayer.Bitmap.GetPixel(8, 8).Alpha);
        }

        [Fact]
        public void ClipRange_CoversBaseAndClippedLayers()
        {
            using var scene = ClippedScene(out _, out _);
            var second = scene.AddLayer("Clipped 2", W, H);
            second.IsClipped = true;

            Assert.Equal((1, 3), scene.ClipRange(2));
            Assert.Equal((1, 3), scene.ClipRange(1));
            Assert.Equal((0, 0), scene.ClipRange(0));
            Assert.Same(scene.Layers[1], scene.ClippingBaseOf(second));
        }

        [Fact]
        public void Clipping_SurvivesUndoAndPsd()
        {
            using var scene = ClippedScene(out _, out _);

            scene.RestoreLayers(scene.CaptureLayers(), 0);
            Assert.True(scene.Layers[2].IsClipped);

            using var stream = new MemoryStream();
            PsdCodec.Write(stream, scene, W, H);
            stream.Position = 0;
            var read = PsdCodec.Read(stream);
            using (read.Scene)
            {
                Assert.True(read.Scene.Layers[2].IsClipped);
                Assert.False(read.Scene.Layers[1].IsClipped);
                using var flat = SceneCompositor.Flatten(read.Scene, W, H);
                Assert.Equal(SKColors.White, flat.GetPixel(8, 8));
            }
        }
    }
}
