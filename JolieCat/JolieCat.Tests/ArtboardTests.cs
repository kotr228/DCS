using System.Collections.Generic;
using JolieCat.Core.Documents;
using JolieCat.Core.Serialization;
using SkiaSharp;
using Xunit;

namespace JolieCat.Tests
{
    public class ArtboardTests
    {
        [Fact]
        public void Artboards_AreSaved_AndMoveWithTheCanvas()
        {
            var scene = new Scene("s");
            scene.AddLayer("L", 200, 100);
            scene.Artboards.Add(new Artboard("Icon", SKRectI.Create(10, 20, 30, 40)));
            scene.SavedPaths["Outline"] = "closed|1 2 _ _ 3 4 0;5 6 7 8 _ _ 1";

            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ab-{System.Guid.NewGuid():N}.jolie");
            ProjectSerializer.Save(path, scene, new List<TimelineTrackData>(), 10, 24);
            var loaded = ProjectSerializer.Load(path).Scene;
            System.IO.File.Delete(path);
            Assert.Equal(new Artboard("Icon", SKRectI.Create(10, 20, 30, 40)), Assert.Single(loaded.Artboards));
            Assert.Equal("closed|1 2 _ _ 3 4 0;5 6 7 8 _ _ 1", loaded.SavedPaths["Outline"]);

            scene.RotateLayers(1);                                    // 90° clockwise: (x, y) -> (H - y, x)
            Assert.Equal(SKRectI.Create(100 - 60, 10, 40, 30), scene.Artboards[0].Bounds);

            scene.CropLayers(SKRectI.Create(50, 0, 50, 200), 0);     // cuts the artboard to what's left
            Assert.Equal(SKRectI.Create(0, 10, 30, 30), scene.Artboards[0].Bounds);        // x 40..80 -> 0..30
        }
    }
}
