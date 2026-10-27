using JolieCat.Core.Documents;
using JolieCat.Core.Effects;
using JolieCat.Core.Rendering;
using SkiaSharp;

namespace JolieCat.Tests
{
    /// <summary>Inner Shadow, Inner Glow, Gradient Overlay and Bevel &amp; Emboss.</summary>
    public class InnerStyleTests
    {
        /// <summary>A 60x60 scene: white background, then a 30x30 red square (15..45).</summary>
        private static SKBitmap Render(LayerStyle style)
        {
            using var scene = new Scene("S");
            scene.AddLayer("Bg", 60, 60).Canvas.Clear(SKColors.White);
            var square = scene.AddLayer("Square", 60, 60);
            TestHelpers.FillRect(square, new SKRect(15, 15, 45, 45), SKColors.Red);
            square.Style = style;
            return SceneCompositor.Flatten(scene, 60, 60);
        }

        [Fact]
        public void InnerShadow_DarkensTheEdgeFacingTheLight_AndNothingOutside()
        {
            // Light from the top-left: the inner shadow sits along the top-left edges.
            using var flat = Render(new LayerStyle { InnerShadow = true, InnerShadowAngle = 135, InnerShadowDistance = 5, InnerShadowSize = 0, InnerShadowOpacity = 100 });

            Assert.True(flat.GetPixel(16, 30).Red < 60, $"left edge {flat.GetPixel(16, 30)}");
            Assert.Equal(SKColors.Red, flat.GetPixel(43, 30));   // right edge untouched
            Assert.Equal(SKColors.Red, flat.GetPixel(30, 30));   // middle untouched
            Assert.Equal(SKColors.White, flat.GetPixel(10, 30)); // never outside the shape
        }

        [Fact]
        public void InnerGlow_TintsTheEdges_NotTheMiddle()
        {
            using var flat = Render(new LayerStyle { InnerGlow = true, InnerGlowColor = SKColors.Blue, InnerGlowSize = 4, InnerGlowOpacity = 100 });

            var edge = flat.GetPixel(15, 30);
            Assert.True(edge.Blue > 100, $"edge {edge}");
            Assert.Equal(SKColors.Red, flat.GetPixel(30, 30));
            Assert.Equal(SKColors.White, flat.GetPixel(5, 30));
        }

        [Fact]
        public void GradientOverlay_RunsAcrossTheContent_NotTheCanvas()
        {
            // 0° = left to right: black at the square's left edge, white at its right.
            using var flat = Render(new LayerStyle { GradientOverlay = true, GradientStart = SKColors.Black, GradientEnd = SKColors.White, GradientAngle = 0 });

            Assert.True(flat.GetPixel(15, 30).Red < 20, $"left {flat.GetPixel(15, 30)}");
            Assert.True(flat.GetPixel(44, 30).Red > 235, $"right {flat.GetPixel(44, 30)}");
            var middle = flat.GetPixel(30, 30);
            Assert.InRange(middle.Red, 100, 155);
            Assert.Equal(middle.Red, middle.Blue); // pure gray: the red is fully covered
            Assert.Equal(SKColors.White, flat.GetPixel(5, 5));
        }

        [Fact]
        public void Bevel_LightsTheTopLeft_AndShadesTheBottomRight()
        {
            using var flat = Render(new LayerStyle { Bevel = true, BevelSize = 4, BevelAngle = 135, BevelHighlightOpacity = 100, BevelShadowOpacity = 100 });

            var lit = flat.GetPixel(16, 30);
            var dark = flat.GetPixel(43, 30);
            Assert.True(lit.Green > 120, $"lit {lit}");    // white mixed into red
            Assert.True(dark.Red < 150, $"dark {dark}");   // black mixed into red
            Assert.Equal(SKColors.Red, flat.GetPixel(30, 30));
        }

        [Fact]
        public void InnerEffects_StayOffTheDropShadow()
        {
            var style = new LayerStyle
            {
                DropShadow = true, ShadowAngle = 135, ShadowDistance = 8, ShadowSize = 0, ShadowOpacity = 100, ShadowColor = SKColors.Black,
                ColorOverlay = true, OverlayColor = SKColors.Lime,
            };
            using var flat = Render(style);

            Assert.Equal(SKColors.Black, flat.GetPixel(50, 50)); // shadow keeps its color
            Assert.Equal(SKColors.Lime, flat.GetPixel(30, 30));
        }

        [Fact]
        public void NewEffects_SurviveSerialization()
        {
            var style = new LayerStyle
            {
                InnerShadow = true, InnerShadowColor = new SKColor(10, 20, 30), InnerShadowOpacity = 40, InnerShadowAngle = 45, InnerShadowDistance = 3, InnerShadowSize = 7,
                InnerGlow = true, InnerGlowColor = SKColors.Yellow, InnerGlowOpacity = 55, InnerGlowSize = 12,
                GradientOverlay = true, GradientStart = SKColors.Red, GradientEnd = SKColors.Blue, GradientOpacity = 80, GradientAngle = 30,
                Bevel = true, BevelSize = 6, BevelDepth = 150, BevelAngle = 100, BevelHighlight = SKColors.White, BevelHighlightOpacity = 60, BevelShadow = SKColors.Navy, BevelShadowOpacity = 70,
            };

            Assert.Equal(style, LayerStyle.Deserialize(style.Serialize()));
        }

        [Fact]
        public void ContentBounds_FindsThePaintedArea()
        {
            using var bitmap = new SKBitmap(50, 40, SKColorType.Rgba8888, SKAlphaType.Premul);
            bitmap.Erase(SKColors.Transparent);
            Assert.True(SceneCompositor.ContentBounds(bitmap).IsEmpty);

            bitmap.SetPixel(7, 9, SKColors.Red);
            bitmap.SetPixel(30, 22, SKColors.Red);
            Assert.Equal(new SKRect(7, 9, 31, 23), SceneCompositor.ContentBounds(bitmap));
        }
    }
}
