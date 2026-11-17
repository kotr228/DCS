using System.Collections.Generic;
using JolieCat.Core.Documents;
using JolieCat.Core.Serialization;
using SkiaSharp;
using Xunit;

namespace JolieCat.Tests
{
    public class ParagraphTextTests
    {
        private static TextContent Para(string text, float box, TextAlignment align = TextAlignment.Left) =>
            new(text, "Arial", 20, false, false, SKColors.Black, new SKPoint(10, 10), false, align, BoxWidth: box);

        [Fact]
        public void Words_WrapToTheBox()
        {
            var text = "the quick brown fox jumps over the lazy dog again and again";
            var point = Para(text, 0).Bounds;
            var boxed = Para(text, 150).Bounds;
            Assert.True(point.Width > 400);
            Assert.InRange(boxed.Width, 149, 151);                // as wide as the box
            Assert.True(boxed.Height > point.Height * 3);          // several lines
            var veryLong = Para("Supercalifragilisticexpialidocious", 60).Bounds;
            Assert.InRange(veryLong.Width, 59, 61);               // a too-long word is broken
        }

        [Fact]
        public void Alignment_IsWithinTheBox_AndItIsSaved()
        {
            var right = Para("hi", 300, TextAlignment.Right);
            using var bitmap = new SKBitmap(400, 60, SKColorType.Rgba8888, SKAlphaType.Premul);
            bitmap.Erase(SKColors.Transparent);
            using (var canvas = new SKCanvas(bitmap)) right.Render(canvas);
            var ink = 0;
            for (var y = 0; y < 60; y++) for (var x = 270; x < 312; x++) if (bitmap.GetPixel(x, y).Alpha > 0) ink++;
            Assert.True(ink > 20);                                  // drawn at the box's right edge
            Assert.Equal(0, bitmap.GetPixel(30, 25).Alpha);

            var scene = new Scene("s");
            scene.AddLayer(new Layer("T", 400, 60, Shared.Enums.LayerType.Text) { Text = right });
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"para-{System.Guid.NewGuid():N}.jolie");
            ProjectSerializer.Save(path, scene, new List<TimelineTrackData>(), 10, 24);
            var loaded = ProjectSerializer.Load(path).Scene.Layers[0].Text;
            System.IO.File.Delete(path);
            Assert.Equal(300, loaded!.BoxWidth);
        }
    }
}
