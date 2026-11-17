using System.Collections.Generic;
using JolieCat.Core.Documents;
using JolieCat.Core.Serialization;
using SkiaSharp;
using Xunit;

namespace JolieCat.Tests
{
    public class TypeOnPathTests
    {
        private static TextContent OnPath(string path, TextAlignment align = TextAlignment.Left) =>
            new("HHHH", "Arial", 30, false, false, SKColors.Black, new SKPoint(0, 0), false, align, PathData: path);

        [Fact]
        public void Letters_StandOnTheCurve()
        {
            // A vertical line down from (100, 20): the letters run downwards, so they are tall and thin as a column.
            var down = OnPath("M100 20 L100 300");
            var b = down.Bounds;
            Assert.True(b.Height > b.Width * 2, $"{b}");
            Assert.InRange(b.Top, 15, 40);

            // A horizontal line: an ordinary line of text sitting on y = 100.
            var flat = OnPath("M10 100 L400 100").Bounds;
            Assert.True(flat.Width > flat.Height);
            Assert.InRange(flat.Bottom, 95, 105);

            // Center alignment puts it in the middle of the curve.
            var centered = OnPath("M10 100 L400 100", TextAlignment.Center).Bounds;
            Assert.InRange(centered.MidX, 195, 215);

            using var picture = new SKBitmap(420, 320, SKColorType.Rgba8888, SKAlphaType.Premul);
            picture.Erase(SKColors.Transparent);
            using (var canvas = new SKCanvas(picture)) down.Render(canvas);
            Assert.Equal(0, picture.GetPixel(300, 160).Alpha);
            var ink = 0;
            for (var y = 20; y < 300; y++) for (var x = 85; x < 115; x++) if (picture.GetPixel(x, y).Alpha > 0) ink++;
            Assert.True(ink > 200);
        }

        [Fact]
        public void MovingAndSaving_KeepTheCurve()
        {
            var text = OnPath("M10 100 L400 100");
            var moved = text.MovedBy(5, 50);
            Assert.InRange(moved.Bounds.Bottom, 145, 155);

            var scene = new Scene("s");
            var layer = new Layer("T", 420, 320, Shared.Enums.LayerType.Text) { Text = moved };
            scene.AddLayer(layer);
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"top-{System.Guid.NewGuid():N}.jolie");
            ProjectSerializer.Save(path, scene, new List<TimelineTrackData>(), 10, 24);
            var loaded = ProjectSerializer.Load(path).Scene.Layers[0].Text;
            System.IO.File.Delete(path);
            Assert.Equal(moved.PathData, loaded!.PathData);
        }
    }
}
