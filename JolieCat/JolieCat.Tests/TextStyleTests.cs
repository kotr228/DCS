using JolieCat.Core.Documents;
using SkiaSharp;

namespace JolieCat.Tests
{
    public class TextStyleTests
    {
        private static TextContent Text(string text = "Hello\nWide line", float size = 40) =>
            new(text, "Arial", size, false, false, SKColors.Black, new SKPoint(20, 20), false);

        private static SKBitmap Draw(TextContent text, int w = 600, int h = 300)
        {
            var bitmap = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
            bitmap.Erase(SKColors.Transparent);
            using var canvas = new SKCanvas(bitmap);
            text.Render(canvas);
            return bitmap;
        }

        /// <summary>Leftmost painted column in rows y0..y1.</summary>
        private static int LeftEdge(SKBitmap b, int y0, int y1)
        {
            for (var x = 0; x < b.Width; x++)
                for (var y = y0; y < y1; y++)
                    if (b.GetPixel(x, y).Alpha > 100) return x;
            return -1;
        }

        [Fact]
        public void Alignment_ShiftsShortLines_WithinTheBlock()
        {
            var left = Text();
            using var a = Draw(left);
            using var c = Draw(left with { Alignment = TextAlignment.Center });
            using var r = Draw(left with { Alignment = TextAlignment.Right });

            // First line ("Hello") is the short one: rows 20..60.
            var l = LeftEdge(a, 20, 60);
            var m = LeftEdge(c, 20, 60);
            var rr = LeftEdge(r, 20, 60);
            Assert.True(m > l + 10, $"{l} {m}");
            Assert.True(rr > m + 10, $"{m} {rr}");
            Assert.Equal(left.Bounds, (left with { Alignment = TextAlignment.Right }).Bounds); // same block
        }

        [Fact]
        public void LeadingAndTracking_GrowTheBlock()
        {
            var t = Text();
            Assert.True((t with { Leading = 200 }).Bounds.Height > t.Bounds.Height * 1.4f);
            Assert.True((t with { Tracking = 500 }).Bounds.Width > t.Bounds.Width * 1.3f);
        }

        [Fact]
        public void Underline_DrawsABarUnderTheText()
        {
            var t = Text("____", 40) with { Text = "mmmm" };
            using var plain = Draw(t);
            using var underlined = Draw(t with { Underline = true });
            var rowsPlain = 0;
            var rowsUnder = 0;
            for (var y = 0; y < 300; y++)
            {
                if (plain.GetPixel(40, y).Alpha > 100) rowsPlain++;
                if (underlined.GetPixel(40, y).Alpha > 100) rowsUnder++;
            }
            Assert.True(rowsUnder > rowsPlain, $"{rowsPlain} vs {rowsUnder}");
        }

        [Fact]
        public void ArcWarp_RaisesTheMiddle_AboveTheEnds()
        {
            var t = Text("ARCHED TEXT HERE", 40);
            var warped = t with { Warp = 60 };
            Assert.True(warped.Bounds.Height > t.Bounds.Height * 1.4f);

            using var b = Draw(warped, 900, 400);
            int Top(int x0, int x1)
            {
                for (var y = 0; y < b.Height; y++)
                    for (var x = x0; x < x1; x++)
                        if (b.GetPixel(x, y).Alpha > 100) return y;
                return int.MaxValue;
            }
            var bounds = warped.Bounds;
            var mid = (int)bounds.MidX;
            Assert.True(Top(mid - 20, mid + 20) < Top((int)bounds.Left, (int)bounds.Left + 40) - 10);
        }

        [Theory]
        [InlineData(TextWarpStyle.Arch)]
        [InlineData(TextWarpStyle.Bulge)]
        [InlineData(TextWarpStyle.Wave)]
        [InlineData(TextWarpStyle.Flag)]
        [InlineData(TextWarpStyle.Rise)]
        [InlineData(TextWarpStyle.Fish)]
        public void EveryWarpStyle_ChangesTheShape(TextWarpStyle style)
        {
            var t = Text("WARPED WORDS", 40);
            var warped = t with { Warp = 50, WarpStyle = style };
            using var plain = Draw(t, 900, 400);
            using var bent = Draw(warped, 900, 400);
            Assert.NotEqual(plain.Pixels, bent.Pixels);

            // The painted letters span more rows once bent (Bulge swells, the rest move up/down).
            static int Rows(SKBitmap b)
            {
                int top = -1, bottom = -1;
                for (var y = 0; y < b.Height; y++)
                    for (var x = 0; x < b.Width; x++)
                        if (b.GetPixel(x, y).Alpha > 100) { if (top < 0) top = y; bottom = y; break; }
                return bottom - top;
            }
            Assert.True(Rows(bent) > Rows(plain) + 3, $"{style}: {Rows(bent)} vs {Rows(plain)}");
        }

        [Fact]
        public void TextSettings_SurviveSaving()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"text_{Guid.NewGuid():N}.jolie");
            try
            {
                var styled = Text() with { Alignment = TextAlignment.Center, Leading = 150, Tracking = 80, Underline = true, Strikethrough = true, Warp = -30, WarpStyle = TextWarpStyle.Flag };
                using (var scene = new Scene("T"))
                {
                    var layer = scene.AddLayer("Text", 300, 200);
                    layer.Text = styled;
                    layer.RenderText();
                    Core.Serialization.ProjectSerializer.Save(path, scene, Array.Empty<Core.Serialization.TimelineTrackData>(), 0, 0);
                }
                using var back = Core.Serialization.ProjectSerializer.Load(path).Scene;
                Assert.Equal(styled, back.Layers[0].Text);
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }
    }
}
