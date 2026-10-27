using JolieCat.Core.Adjustments;
using JolieCat.Core.Documents;
using JolieCat.Core.Formats;
using JolieCat.Core.Rendering;
using SkiaSharp;

namespace JolieCat.Tests
{
    public class AdjustmentLayerTests
    {
        private static Scene RedSceneWithInvert(out Layer adjustment)
        {
            var scene = new Scene("S");
            scene.AddLayer("Bg", 10, 10).Canvas.Clear(SKColors.Red);
            adjustment = new Layer("Invert", 10, 10, Shared.Enums.LayerType.Adjustment)
            {
                Adjustment = AdjustmentSettings.Default(AdjustmentKind.Invert),
            };
            scene.AddLayer(adjustment);
            return scene;
        }

        private static void AssertColor(SKColor expected, SKColor actual, int tolerance = 2)
        {
            Assert.InRange(actual.Red, expected.Red - tolerance, expected.Red + tolerance);
            Assert.InRange(actual.Green, expected.Green - tolerance, expected.Green + tolerance);
            Assert.InRange(actual.Blue, expected.Blue - tolerance, expected.Blue + tolerance);
            Assert.InRange(actual.Alpha, expected.Alpha - tolerance, expected.Alpha + tolerance);
        }

        [Fact]
        public void InvertAdjustment_ChangesEverythingBelow()
        {
            using var scene = RedSceneWithInvert(out _);
            using var flat = SceneCompositor.Flatten(scene, 10, 10);

            AssertColor(SKColors.Cyan, flat.GetPixel(5, 5));
        }

        [Fact]
        public void AdjustmentPixelsAreUntouched_HidingItRestoresTheImage()
        {
            using var scene = RedSceneWithInvert(out var adjustment);
            adjustment.IsVisible = false;

            using var flat = SceneCompositor.Flatten(scene, 10, 10);

            AssertColor(SKColors.Red, flat.GetPixel(5, 5));
            AssertColor(SKColors.Red, scene.Layers[0].Bitmap.GetPixel(5, 5));
        }

        [Fact]
        public void Mask_LimitsTheAdjustment()
        {
            using var scene = RedSceneWithInvert(out var adjustment);
            var mask = adjustment.AddMask();
            using (var black = new SKPaint { Color = SKColors.Black })
                mask.Canvas.DrawRect(0, 0, 5, 10, black);

            using var flat = SceneCompositor.Flatten(scene, 10, 10);

            AssertColor(SKColors.Red, flat.GetPixel(2, 5));
            AssertColor(SKColors.Cyan, flat.GetPixel(8, 5));
        }

        [Fact]
        public void Opacity_BlendsBetweenOriginalAndAdjusted()
        {
            using var scene = RedSceneWithInvert(out var adjustment);
            adjustment.Opacity = 0.5;

            using var flat = SceneCompositor.Flatten(scene, 10, 10);

            AssertColor(new SKColor(128, 128, 128), flat.GetPixel(5, 5), 3);
        }

        [Fact]
        public void SemiTransparentPixels_KeepTheirAlpha()
        {
            var scene = new Scene("S");
            scene.AddLayer("Bg", 4, 4).Canvas.Clear(SKColors.Red.WithAlpha(128));
            scene.AddLayer(new Layer("Adj", 4, 4, Shared.Enums.LayerType.Adjustment)
            {
                Adjustment = AdjustmentSettings.Default(AdjustmentKind.Invert),
            });

            using var flat = SceneCompositor.Flatten(scene, 4, 4);

            var p = flat.GetPixel(1, 1);
            Assert.InRange(p.Alpha, 126, 130);
            Assert.True(p.Green > 240 && p.Red < 15);
            scene.Dispose();
        }

        [Fact]
        public void LayersAboveAreNotAffected()
        {
            using var scene = RedSceneWithInvert(out _);
            var top = scene.AddLayer("Top", 10, 10);
            TestHelpers.FillRect(top, new SKRect(0, 0, 3, 3), SKColors.Blue);

            using var flat = SceneCompositor.Flatten(scene, 10, 10);

            AssertColor(SKColors.Blue, flat.GetPixel(1, 1));
            AssertColor(SKColors.Cyan, flat.GetPixel(6, 6));
        }

        [Fact]
        public void MergeDown_BakesTheAdjustmentIntoTheLayerBelow()
        {
            using var scene = RedSceneWithInvert(out var adjustment);

            Assert.True(scene.MergeLayerDown(adjustment));

            Assert.Single(scene.Layers);
            AssertColor(SKColors.Cyan, scene.Layers[0].Bitmap.GetPixel(5, 5));
        }

        [Fact]
        public void CaptureRestore_KeepsTheAdjustment()
        {
            using var scene = RedSceneWithInvert(out _);
            var snapshot = scene.CaptureLayers();

            scene.RestoreLayers(snapshot, 1);

            Assert.Equal(AdjustmentKind.Invert, scene.Layers[1].Adjustment?.Kind);
        }

        [Fact]
        public void Settings_SerializeRoundTrip()
        {
            var settings = new AdjustmentSettings(AdjustmentKind.Levels, new[] { 10.0, 240.0, 1.25, 0.0, 255.0 });

            var copy = AdjustmentSettings.Deserialize(settings.Serialize());

            Assert.NotNull(copy);
            Assert.Equal(settings.Kind, copy!.Kind);
            Assert.Equal(settings.Values, copy.Values);
        }

        [Fact]
        public void HueSaturation_FullDesaturationMakesGray()
        {
            var scene = new Scene("S");
            scene.AddLayer("Bg", 4, 4).Canvas.Clear(new SKColor(200, 60, 60));
            scene.AddLayer(new Layer("Adj", 4, 4, Shared.Enums.LayerType.Adjustment)
            {
                Adjustment = new AdjustmentSettings(AdjustmentKind.HueSaturation, new[] { 0.0, -100.0, 0.0 }),
            });

            using var flat = SceneCompositor.Flatten(scene, 4, 4);

            var p = flat.GetPixel(1, 1);
            Assert.InRange(p.Red - p.Blue, -2, 2);
            Assert.InRange(p.Green - p.Blue, -2, 2);
            scene.Dispose();
        }

        [Fact]
        public void PsdExport_RasterizesTheAdjustment()
        {
            using var scene = RedSceneWithInvert(out _);
            using var stream = new MemoryStream();
            PsdCodec.Write(stream, scene, 10, 10);
            stream.Position = 0;

            var document = PsdCodec.Read(stream);
            using (document.Scene)
            {
                Assert.Equal(2, document.Scene.Layers.Count);
                AssertColor(SKColors.Cyan, document.Scene.Layers[1].Bitmap.GetPixel(5, 5));
            }
        }
    }
}
