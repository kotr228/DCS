using System.Numerics;
using JolieCat3D.Core.Materials;
using JolieCat3D.Core.Numerics;

namespace JolieCat3D.Tests
{
    public sealed class TexturePaintBrushExtraTests
    {
        /// <summary>Left half black, right half white.</summary>
        private static TextureBuffer Split()
        {
            var b = new TextureBuffer(32, 32, new Color4(0, 0, 0, 1));
            for (var y = 0; y < 32; y++) for (var x = 16; x < 32; x++) b.SetPixel(x, y, new Color4(1, 1, 1, 1));
            return b;
        }

        [Fact]
        public void SoftenBlursAHardEdge()
        {
            var b = Split();
            for (var i = 0; i < 5; i++) TexturePaintBrushExtras.Soften(b, new Vector2(0.5f, 0.5f), 8, 1f);
            var left = b.GetPixel(15, 16).R;
            var right = b.GetPixel(16, 16).R;
            Assert.True(left > 0.1f && right < 0.9f, $"{left} {right}");
            Assert.Equal(0f, b.GetPixel(0, 0).R);   // outside the brush
        }

        [Fact]
        public void SmearDragsColourAlongTheStroke()
        {
            var b = Split();
            // Drag from the white side towards the black side: white is pulled left.
            TexturePaintBrushExtras.Smear(b, new Vector2(14 / 32f, 0.5f), new Vector2(20 / 32f, 0.5f), 6, 1f);
            Assert.True(b.GetPixel(14, 16).R > 0.5f);
        }

        [Fact]
        public void FillFloodsTheTexture()
        {
            var b = Split();
            TexturePaintBrushExtras.Fill(b, new Color4(0, 1, 0, 1), 1f);
            Assert.All(b.Pixels, p => Assert.Equal(new Color4(0, 1, 0, 1), p));
        }
    }
}
