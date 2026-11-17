using System.Collections.Generic;
using System.Linq;
using JolieCat.Core.Documents;
using SkiaSharp;
using Xunit;

namespace JolieCat.Tests
{
    public class SpriteSheetCompleteTests
    {
        private static SKBitmap Box(int w, int h, SKColor c)
        {
            var b = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
            b.Erase(c);
            return b;
        }

        private static (SKBitmap Sheet, SpriteSheetGrid Grid) TwoByTwo() =>
            SpriteSheetTools.Pack(new List<SKBitmap> { Box(10, 10, SKColors.Red), Box(10, 10, SKColors.Green), Box(10, 10, SKColors.Blue), Box(6, 6, SKColors.Yellow) }, padding: 0, columns: 2);

        [Fact]
        public void Extrude_RepeatsEdges_AndPowerOfTwo_RoundsUp()
        {
            var (sheet, grid) = TwoByTwo();
            var (atlas, frames) = SpriteSheetTools.PackAtlas(sheet, grid, padding: 2, extrude: 2, powerOfTwo: true);
            using (atlas)
            {
                Assert.True((atlas.Width & (atlas.Width - 1)) == 0 && (atlas.Height & (atlas.Height - 1)) == 0);
                var red = frames.First(f => f.Row == 0 && f.Column == 0);
                Assert.Equal(SKColors.Red, atlas.GetPixel(red.At.X - 1, red.At.Y + 5));     // the left edge, repeated
                Assert.Equal(SKColors.Red, atlas.GetPixel(red.At.X + 5, red.At.Y - 2));     // the top edge
                Assert.Equal(SKColors.Red, atlas.GetPixel(red.At.X - 2, red.At.Y - 2));     // the corner
                // Frames never overlap, extruded edges included.
                var slots = frames.Select(f => SKRectI.Create(f.At.X - 2, f.At.Y - 2, f.Used.Width + 4, f.Used.Height + 4)).ToList();
                for (var i = 0; i < slots.Count; i++)
                    for (var j = i + 1; j < slots.Count; j++)
                        Assert.False(SKRectI.Intersect(slots[i], slots[j]) is { IsEmpty: false });
            }
        }

        [Fact]
        public void Formats_Hash_Array_Css()
        {
            var (sheet, grid) = TwoByTwo();
            grid.RowNames = "top, bottom";
            grid.CellPivots[3] = (10, 20);
            using (var doc = System.Text.Json.JsonDocument.Parse(SpriteSheetTools.ToEngineJson(sheet, grid, "s.png", "s", SpriteSheetTools.DataFormat.JsonArray)))
            {
                var frames = doc.RootElement.GetProperty("frames");
                Assert.Equal(System.Text.Json.JsonValueKind.Array, frames.ValueKind);
                Assert.Equal(4, frames.GetArrayLength());
                Assert.Equal("s_003", frames[3].GetProperty("filename").GetString());
                Assert.Equal(0.1, frames[3].GetProperty("pivot").GetProperty("x").GetDouble(), 3);  // its own pivot
                Assert.Equal(1, frames[0].GetProperty("pivot").GetProperty("y").GetDouble(), 3);    // the sheet's
                Assert.Equal(2, doc.RootElement.GetProperty("animations").GetProperty("bottom").GetArrayLength());
            }
            var css = SpriteSheetTools.ToEngineJson(sheet, grid, "s.png", "s", SpriteSheetTools.DataFormat.Css);
            Assert.Contains("url(\"s.png\")", css);
            Assert.Contains(".s_001 { width: 10px; height: 10px; background-position: -10px 0px; }", css);

            // The array layout reads back in.
            var (atlas, frames2) = SpriteSheetTools.PackAtlas(sheet, grid);
            using (atlas)
            {
                var read = SpriteAtlasReader.Read(SpriteSheetTools.ToAtlasJson(atlas, frames2, grid, "a.png", "a", SpriteSheetTools.DataFormat.JsonArray), atlas);
                Assert.Equal(4, read.Sprites.Count);
            }
        }

        [Fact]
        public void CellOperations()
        {
            var (sheet, grid) = TwoByTwo();
            using var swapped = SpriteSheetTools.CellOperation(sheet, grid, SpriteSheetTools.CellAction.Swap, 0, 3)!;
            Assert.Equal(SKColors.Red, swapped.GetPixel(15, 15));                // red now in the last cell
            Assert.Equal(0, swapped.GetPixel(0, 0).Alpha);                       // the yellow (smaller) came first: its corner is empty
            Assert.Equal(SKColors.Yellow, swapped.GetPixel(5, 5));
            using var copied = SpriteSheetTools.CellOperation(sheet, grid, SpriteSheetTools.CellAction.Copy, 1, 2)!;
            Assert.Equal(SKColors.Green, copied.GetPixel(5, 15));
            using var cleared = SpriteSheetTools.CellOperation(sheet, grid, SpriteSheetTools.CellAction.Clear, 1)!;
            Assert.Equal(0, cleared.GetPixel(15, 5).Alpha);
            Assert.Equal(SKColors.Red, cleared.GetPixel(5, 5));
            Assert.Null(SpriteSheetTools.CellOperation(sheet, grid, SpriteSheetTools.CellAction.Swap, 0, 0));
            Assert.Null(SpriteSheetTools.CellOperation(sheet, grid, SpriteSheetTools.CellAction.Clear, 9));

            using var half = new SKBitmap(20, 10, SKColorType.Rgba8888, SKAlphaType.Premul);
            half.Erase(SKColors.Transparent);
            using (var c = new SKCanvas(half)) using (var p = new SKPaint { Color = SKColors.Red }) c.DrawRect(0, 0, 5, 10, p);
            var oneRow = new SpriteSheetGrid { Columns = 2, Rows = 1 };
            using var flipped = SpriteSheetTools.CellOperation(half, oneRow, SpriteSheetTools.CellAction.FlipHorizontal, 0)!;
            Assert.Equal(0, flipped.GetPixel(1, 5).Alpha);
            Assert.Equal(SKColors.Red, flipped.GetPixel(8, 5));
        }

        [Fact]
        public void CellPivots_AsText()
        {
            var grid = new SpriteSheetGrid();
            grid.CellPivots[2] = (25, 75);
            grid.CellPivots[7] = (50.5, 100);
            var text = grid.CellPivotsText;
            Assert.Equal("2:25,75;7:50.5,100", text);
            var again = new SpriteSheetGrid { CellPivotsText = text + ";junk;9:x,1" };
            Assert.Equal((25.0, 75.0), again.PivotFor(2));
            Assert.Equal((again.PivotX, again.PivotY), again.PivotFor(3));
            Assert.Equal(2, again.CellPivots.Count);
        }
    }
}
