using System;
using JolieCat.Core.Documents;
using JolieCat.Core.Rendering;
using JolieCat.Shared.Enums;
using SkiaSharp;
using Xunit;

namespace JolieCat.Tests
{
    public class BlendModesTests
    {
        /// <summary>A layer of <paramref name="top"/> in <paramref name="mode"/> over <paramref name="bottom"/>.</summary>
        private static SKColor Blend(BlendMode mode, SKColor bottom, SKColor top)
        {
            var scene = new Scene("b");
            scene.AddLayer("Bottom", 4, 4).Canvas.Clear(bottom);
            var layer = scene.AddLayer("Top", 4, 4);
            layer.Canvas.Clear(top);
            layer.BlendMode = mode;
            using var flat = SceneCompositor.Flatten(scene, 4, 4);
            return flat.GetPixel(1, 1);
        }

        private static void Near(int expected, int actual) => Assert.InRange(actual, expected - 3, expected + 3);

        [Theory]
        // bottom 200,100,60 ; top 120,180,40 (0..255) - expected per Photoshop's formulas
        [InlineData(BlendMode.LinearBurn, 65, 25, 0)]          // s + d - 255
        [InlineData(BlendMode.LinearDodge, 255, 255, 100)]     // s + d
        [InlineData(BlendMode.Subtract, 80, 0, 20)]            // d - s
        [InlineData(BlendMode.Divide, 255, 142, 255)]          // d / s
        [InlineData(BlendMode.LinearLight, 185, 205, 0)]       // d + 2s - 255
        [InlineData(BlendMode.PinLight, 200, 105, 60)]         // s<128: min(d, 2s); else max(d, 2s-255)
        [InlineData(BlendMode.HardMix, 255, 255, 0)]           // s + d >= 255
        [InlineData(BlendMode.DarkerColor, 200, 100, 60)]      // the darker of the two whole colors
        [InlineData(BlendMode.LighterColor, 120, 180, 40)]
        public void NewModes_MatchPhotoshopsFormulas(BlendMode mode, int r, int g, int b)
        {
            var c = Blend(mode, new SKColor(200, 100, 60), new SKColor(120, 180, 40));
            Near(r, c.Red);
            Near(g, c.Green);
            Near(b, c.Blue);
            Assert.True(BlendModes.IsCustom(mode));
        }

        [Fact]
        public void VividLight_BurnsDarkAndDodgesLight()
        {
            var dark = Blend(BlendMode.VividLight, new SKColor(128, 128, 128), new SKColor(64, 64, 64));    // burn by 2*64
            Near(0, dark.Red);                                                                               // 1-(1-.5)/.5 = 0
            var light = Blend(BlendMode.VividLight, new SKColor(128, 128, 128), new SKColor(191, 191, 191)); // dodge by 2*(191-128)
            Assert.True(light.Red > 230);
        }

        [Fact]
        public void HalfTransparentLayer_MixesEvenly()
        {
            var c = Blend(BlendMode.LinearDodge, new SKColor(100, 100, 100), new SKColor(100, 100, 100, 128));
            Near(150, c.Red);                                                                                  // halfway to 200
        }

        [Fact]
        public void Dissolve_ScattersFullyOpaquePixels()
        {
            var scene = new Scene("d");
            scene.AddLayer("Bottom", 64, 64).Canvas.Clear(SKColors.White);
            var layer = scene.AddLayer("Top", 64, 64);
            layer.Canvas.Clear(SKColors.Black);
            layer.BlendMode = BlendMode.Dissolve;
            layer.Opacity = 0.5;
            using var flat = SceneCompositor.Flatten(scene, 64, 64);
            int black = 0, other = 0;
            foreach (var p in flat.Pixels) if (p.Red == 0) black++; else if (p.Red == 255) { } else other++;
            Assert.Equal(0, other);                                     // never a gray mix
            Assert.InRange(black / (64.0 * 64), 0.4, 0.6);              // about half
        }
    }
}
