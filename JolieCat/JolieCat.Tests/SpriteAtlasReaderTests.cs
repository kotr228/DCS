using System.Collections.Generic;
using System.Linq;
using JolieCat.Core.Documents;
using SkiaSharp;
using Xunit;

namespace JolieCat.Tests
{
    public class SpriteAtlasReaderTests
    {
        private static SKBitmap Box(int w, int h, SKColor c, int pad = 0)
        {
            var b = new SKBitmap(w + 2 * pad, h + 2 * pad, SKColorType.Rgba8888, SKAlphaType.Premul);
            b.Erase(SKColors.Transparent);
            using var canvas = new SKCanvas(b);
            using var paint = new SKPaint { Color = c };
            canvas.DrawRect(pad, pad, w, h, paint);
            return b;
        }

        [Fact]
        public void OurPackedAtlas_ComesBack_WithItsAnimations()
        {
            var rows = new List<(string?, List<SKBitmap>)>
            {
                ("walk", new List<SKBitmap> { Box(10, 10, SKColors.Red, 5), Box(10, 10, SKColors.Red, 5), Box(10, 10, SKColors.Red, 5) }),
                ("jump", new List<SKBitmap> { Box(10, 10, SKColors.Blue, 5) }),
            };
            var (sheet, grid) = SpriteSheetTools.PackRows(rows, padding: 0);
            Assert.Equal("walk, jump", grid.RowNames);
            Assert.Equal(3, grid.Columns);
            var (atlas, frames) = SpriteSheetTools.PackAtlas(sheet, grid);
            var json = SpriteSheetTools.ToAtlasJson(atlas, frames, grid, "a.png", "a");

            var read = SpriteAtlasReader.Read(json, atlas);
            Assert.Equal("a.png", read.ImageName);
            Assert.Equal(4, read.Sprites.Count);
            Assert.All(read.Sprites, s => Assert.Equal(20, s.Picture.Width));           // the full cell again, untrimmed
            Assert.Equal(SKColors.Blue, read.Sprites[3].Picture.GetPixel(10, 10));
            Assert.Equal(0, read.Sprites[3].Picture.GetPixel(1, 1).Alpha);
            var back = SpriteAtlasReader.Rows(read);
            Assert.Equal(new[] { "walk", "jump" }, back.Select(r => r.Name));
            Assert.Equal(3, back[0].Sprites.Count);
        }

        [Fact]
        public void ArrayLayout_RotatedFrames_AndAsepriteTags()
        {
            // A 6 x 4 sprite stored turned clockwise: 4 x 6 in the atlas. Its left column is red.
            using var atlas = new SKBitmap(10, 10, SKColorType.Rgba8888, SKAlphaType.Premul);
            atlas.Erase(SKColors.Transparent);
            using (var c = new SKCanvas(atlas))
            using (var green = new SKPaint { Color = SKColors.Green })
            using (var red = new SKPaint { Color = SKColors.Red })
            {
                c.DrawRect(0, 0, 4, 6, green);
                c.DrawRect(0, 0, 4, 1, red);     // the sprite's left column, turned clockwise, is the top row
            }
            var json = @"{ ""frames"": [
                { ""filename"": ""a"", ""frame"": { ""x"": 0, ""y"": 0, ""w"": 6, ""h"": 4 }, ""rotated"": true,
                  ""spriteSourceSize"": { ""x"": 0, ""y"": 0, ""w"": 6, ""h"": 4 }, ""sourceSize"": { ""w"": 6, ""h"": 4 } },
                { ""filename"": ""b"", ""frame"": { ""x"": 0, ""y"": 0, ""w"": 2, ""h"": 2 } } ],
              ""meta"": { ""image"": ""x.png"", ""frameTags"": [ { ""name"": ""spin"", ""from"": 0, ""to"": 1 } ] } }";
            var read = SpriteAtlasReader.Read(json, atlas);
            var a = read.Sprites[0].Picture;
            Assert.Equal(6, a.Width);
            Assert.Equal(4, a.Height);
            Assert.Equal(SKColors.Red, a.GetPixel(0, 2));
            Assert.Equal(SKColors.Green, a.GetPixel(5, 2));
            Assert.Equal("spin", Assert.Single(read.Animations).Name);
            Assert.Equal(new[] { "a", "b" }, read.Animations[0].Frames);
        }
    }
}
