using System.Collections.Generic;
using JolieCat.Core.Documents;
using JolieCat.Core.Effects;
using JolieCat.Core.Rendering;
using JolieCat.Core.Serialization;
using SkiaSharp;
using Xunit;

namespace JolieCat.Tests
{
    public class FillOpacityTests
    {
        private static Scene Make(double fill, bool shadow)
        {
            var scene = new Scene("f");
            scene.AddLayer("Bottom", 100, 100).Canvas.Clear(SKColors.White);
            var layer = scene.AddLayer("Top", 100, 100);
            using (var red = new SKPaint { Color = SKColors.Red }) layer.Canvas.DrawRect(20, 20, 40, 40, red);
            layer.FillOpacity = fill;
            if (shadow) layer.Style = new LayerStyle { DropShadow = true, ShadowOpacity = 100, ShadowAngle = 180, ShadowDistance = 30, ShadowSize = 0 };
            return scene;
        }

        [Fact]
        public void ZeroFill_KeepsTheShadow_HidesThePixels()
        {
            using var flat = SceneCompositor.Flatten(Make(0, shadow: true), 100, 100);
            Assert.Equal(SKColors.White, flat.GetPixel(30, 40));            // the red square is gone
            var shadowPixel = flat.GetPixel(75, 40);                        // the shadow, 30 px to the right
            Assert.True(shadowPixel.Red < 40, shadowPixel.ToString());
        }

        [Fact]
        public void WithoutEffects_FillActsLikeOpacity_AndIsSaved()
        {
            var scene = Make(0.5, shadow: false);
            using (var flat = SceneCompositor.Flatten(scene, 100, 100))
                Assert.InRange((int)flat.GetPixel(30, 40).Green, 125, 130);     // half red over white

            var copy = new Scene("c");
            copy.RestoreLayers(scene.CaptureLayers(), 1);
            Assert.Equal(0.5, copy.Layers[1].FillOpacity);

            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"fill-{System.Guid.NewGuid():N}.jolie");
            ProjectSerializer.Save(path, scene, new List<TimelineTrackData>(), 10, 24);
            var loaded = ProjectSerializer.Load(path).Scene;
            System.IO.File.Delete(path);
            Assert.Equal(0.5, loaded.Layers[1].FillOpacity, 3);
        }
    }
}
