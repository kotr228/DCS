using System.Collections.Generic;
using System.Linq;
using JolieCat.Core.Documents;
using SkiaSharp;
using Xunit;

namespace JolieCat.Tests
{
    public class SpriteSheetToolsTests
    {
        private static SKBitmap Sprite(int w, int h, SKColor color)
        {
            var b = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
            b.Erase(color);
            return b;
        }

        [Fact]
        public void Pack_PutsPicturesInANearSquareGrid()
        {
            var pictures = new List<SKBitmap>();
            for (var i = 0; i < 5; i++) pictures.Add(Sprite(10, 8, SKColors.Red));
            var (sheet, grid) = SpriteSheetTools.Pack(pictures, padding: 2);
            Assert.Equal(3, grid.Columns);
            Assert.Equal(2, grid.Rows);
            Assert.Equal(3 * 10 + 2 * 2, sheet.Width);
            Assert.Equal(2 * 8 + 2, sheet.Height);
            var cell = grid.GetCellRect(1, 1, sheet.Width, sheet.Height);
            Assert.Equal(10, cell.Width, 3);
            Assert.Equal(SKColors.Red, sheet.GetPixel((int)cell.MidX, (int)cell.MidY));
            Assert.Equal(0, sheet.GetPixel(sheet.Width - 1, sheet.Height - 1).Alpha); // 6th cell empty
        }

        [Fact]
        public void DetectGrid_FindsThePackedGrid()
        {
            var pictures = new List<SKBitmap>();
            for (var i = 0; i < 12; i++) pictures.Add(Sprite(16, 12, SKColors.Blue));
            var (sheet, _) = SpriteSheetTools.Pack(pictures, padding: 4, columns: 4);
            var grid = SpriteSheetTools.DetectGrid(sheet);
            Assert.NotNull(grid);
            Assert.Equal(4, grid!.Columns);
            Assert.Equal(3, grid.Rows);
            Assert.Equal(4, grid.PaddingX);
            Assert.Equal(4, grid.PaddingY);
            Assert.Equal(16, grid.GetCellSize(sheet.Width, sheet.Height).Width, 3);
        }

        [Fact]
        public void DetectGrid_WorksOnAnOpaqueBackground()
        {
            using var sheet = new SKBitmap(100, 50);
            using (var c = new SKCanvas(sheet))
            {
                c.Clear(SKColors.White);
                using var paint = new SKPaint { Color = SKColors.Black };
                for (var i = 0; i < 4; i++) c.DrawRect(5 + i * 25, 5, 15, 40, paint);
            }
            var grid = SpriteSheetTools.DetectGrid(sheet);
            Assert.NotNull(grid);
            Assert.Equal(4, grid!.Columns);
            Assert.Equal(1, grid.Rows);
        }

        [Fact]
        public void EngineJson_ListsFilledCells_WithTrimmedBounds()
        {
            var pictures = new List<SKBitmap> { Sprite(10, 10, SKColors.Red), Sprite(4, 6, SKColors.Red), Sprite(10, 10, SKColors.Red) };
            var (sheet, grid) = SpriteSheetTools.Pack(pictures, padding: 0, columns: 2); // 4 cells, 3 filled
            var json = SpriteSheetTools.ToEngineJson(sheet, grid, "walk.png", "walk");
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var frames = doc.RootElement.GetProperty("frames");
            Assert.Equal(3, frames.EnumerateObject().Count());
            var small = frames.GetProperty("walk_001");
            Assert.True(small.GetProperty("trimmed").GetBoolean());
            Assert.Equal(4, small.GetProperty("spriteSourceSize").GetProperty("w").GetInt32());
            Assert.Equal(3, small.GetProperty("spriteSourceSize").GetProperty("x").GetInt32());
            Assert.Equal("walk.png", doc.RootElement.GetProperty("meta").GetProperty("image").GetString());
        }

        [Fact]
        public void PackAtlas_TrimsSprites_AndKeepsTheirPlaceInTheCell()
        {
            // 4 cells of 40 x 40, each with a 10 x 10 sprite at a different spot; one empty.
            using var sheet = new SKBitmap(80, 80, SKColorType.Rgba8888, SKAlphaType.Premul);
            sheet.Erase(SKColors.Transparent);
            using (var c = new SKCanvas(sheet))
            using (var paint = new SKPaint { Color = SKColors.Red })
            {
                c.DrawRect(5, 5, 10, 10, paint);
                c.DrawRect(40 + 20, 20, 10, 10, paint);
                c.DrawRect(30, 40 + 30, 10, 10, paint);
            }
            var grid = new SpriteSheetGrid { Columns = 2, Rows = 2, PivotX = 50, PivotY = 50 };
            var (atlas, frames) = SpriteSheetTools.PackAtlas(sheet, grid, padding: 1);
            using (atlas)
            {
                Assert.Equal(3, frames.Count);
                Assert.True(atlas.Width * atlas.Height < 80 * 80 / 4);
                foreach (var f in frames)
                {
                    Assert.Equal(10, f.Used.Width);
                    Assert.Equal(SKColors.Red, atlas.GetPixel(f.At.X + 5, f.At.Y + 5));
                }
                var json = SpriteSheetTools.ToAtlasJson(atlas, frames, grid, "a.png", "a");
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var second = doc.RootElement.GetProperty("frames").GetProperty("a_001");
                Assert.Equal(20, second.GetProperty("spriteSourceSize").GetProperty("x").GetInt32());
                Assert.Equal(40, second.GetProperty("sourceSize").GetProperty("w").GetInt32());
                Assert.Equal(0.5, second.GetProperty("pivot").GetProperty("y").GetDouble());
            }
        }

        [Fact]
        public void Pivot_IsSavedWithTheProject()
        {
            var scene = new Scene("s");
            scene.AddLayer("L", 8, 8);
            var grid = new SpriteSheetGrid { PivotX = 25, PivotY = 75 };
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"pivot-{System.Guid.NewGuid():N}.jolie");
            JolieCat.Core.Serialization.ProjectSerializer.Save(path, scene, new List<JolieCat.Core.Serialization.TimelineTrackData>(), 10, 24,
                JolieCat.Shared.Enums.ProjectType.SpriteSheet, grid);
            var loaded = JolieCat.Core.Serialization.ProjectSerializer.Load(path);
            System.IO.File.Delete(path);
            Assert.Equal(25, loaded.SpriteSheetGrid.PivotX);
            Assert.Equal(75, loaded.SpriteSheetGrid.PivotY);
        }

        [Fact]
        public void NamedRows_BecomeAnimations_InBothExports()
        {
            var pictures = new List<SKBitmap>();
            for (var i = 0; i < 5; i++) pictures.Add(Sprite(8, 8, SKColors.Green));
            var (sheet, grid) = SpriteSheetTools.Pack(pictures, padding: 0, columns: 3);   // rows: 3 + 2
            grid.RowNames = "walk,  jump ";
            Assert.Equal("jump", grid.RowName(1));
            Assert.Null(grid.RowName(2));

            using (var doc = System.Text.Json.JsonDocument.Parse(SpriteSheetTools.ToEngineJson(sheet, grid, "s.png", "s")))
            {
                var anims = doc.RootElement.GetProperty("animations");
                Assert.Equal(new[] { "s_000", "s_001", "s_002" }, anims.GetProperty("walk").EnumerateArray().Select(e => e.GetString()));
                Assert.Equal(new[] { "s_003", "s_004" }, anims.GetProperty("jump").EnumerateArray().Select(e => e.GetString()));
            }
            var (atlas, frames) = SpriteSheetTools.PackAtlas(sheet, grid);
            using (atlas)
            using (var doc = System.Text.Json.JsonDocument.Parse(SpriteSheetTools.ToAtlasJson(atlas, frames, grid, "a.png", "a")))
                Assert.Equal(2, doc.RootElement.GetProperty("animations").GetProperty("jump").GetArrayLength());

            grid.RowNames = string.Empty;                                // no names: no block
            using (var doc = System.Text.Json.JsonDocument.Parse(SpriteSheetTools.ToEngineJson(sheet, grid, "s.png", "s")))
                Assert.False(doc.RootElement.TryGetProperty("animations", out _));

            var scene = new Scene("s");
            scene.AddLayer("L", 8, 8);
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rows-{System.Guid.NewGuid():N}.jolie");
            JolieCat.Core.Serialization.ProjectSerializer.Save(path, scene, new List<JolieCat.Core.Serialization.TimelineTrackData>(), 10, 24,
                JolieCat.Shared.Enums.ProjectType.SpriteSheet, new SpriteSheetGrid { RowNames = "idle, run" });
            var loaded = JolieCat.Core.Serialization.ProjectSerializer.Load(path);
            System.IO.File.Delete(path);
            Assert.Equal("idle, run", loaded.SpriteSheetGrid.RowNames);
        }

        [Fact]
        public void DetectGrid_EmptySheetIsNull()
        {
            using var sheet = new SKBitmap(20, 20, SKColorType.Rgba8888, SKAlphaType.Premul);
            sheet.Erase(SKColors.Transparent);
            Assert.Null(SpriteSheetTools.DetectGrid(sheet));
        }
    }
}
