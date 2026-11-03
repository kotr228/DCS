using System.IO;
using JolieCat.Core.Documents;
using JolieCat.Core.Serialization;
using SkiaSharp;

namespace JolieCat.Tests
{
    public class GuideSerializationTests
    {
        private static Scene WithGuides()
        {
            var scene = new Scene("G");
            scene.AddLayer("Bg", 100, 60).Canvas.Clear(SKColors.White);
            scene.Guides.Add(new Guide(true, 10));   // x = 10
            scene.Guides.Add(new Guide(false, 20));  // y = 20
            return scene;
        }

        [Fact]
        public void Guides_FollowTheCanvas_WhenRotatedOrFlipped()
        {
            using (var scene = WithGuides())
            {
                scene.RotateLayers(1); // 90° CW: (x, y) -> (60 - y, x)
                Assert.Equal(new[] { new Guide(false, 10), new Guide(true, 40) }, scene.Guides);
            }
            using (var scene = WithGuides())
            {
                scene.RotateLayers(-1); // 90° CCW: (x, y) -> (y, 100 - x)
                Assert.Equal(new[] { new Guide(false, 90), new Guide(true, 20) }, scene.Guides);
            }
            using (var scene = WithGuides())
            {
                scene.FlipLayers(horizontal: true);
                Assert.Equal(new[] { new Guide(true, 90), new Guide(false, 20) }, scene.Guides);
            }
        }

        [Fact]
        public void Guides_FollowTheCanvas_WhenResizedResampledOrCropped()
        {
            using (var scene = WithGuides())
            {
                scene.ResizeLayers(120, 80, 10, 10);   // content shifted by 10
                Assert.Equal(new[] { new Guide(true, 20), new Guide(false, 30) }, scene.Guides);
            }
            using (var scene = WithGuides())
            {
                scene.ResampleLayers(50, 30);          // half size
                Assert.Equal(new[] { new Guide(true, 5), new Guide(false, 10) }, scene.Guides);
            }
            using (var scene = WithGuides())
            {
                scene.CropLayers(new SKRectI(15, 5, 80, 50), 0); // x = 10 falls outside: dropped
                Assert.Equal(new[] { new Guide(false, 15) }, scene.Guides);
            }
        }

        [Fact]
        public void Guides_AreSavedAndLoadedWithTheProject()
        {
            var path = Path.Combine(Path.GetTempPath(), $"guides_{Guid.NewGuid():N}.jolie");
            try
            {
                using (var scene = new Scene("G"))
                {
                    scene.AddLayer("Bg", 40, 30).Canvas.Clear(SKColors.White);
                    scene.Guides.Add(new Guide(true, 12.5f));
                    scene.Guides.Add(new Guide(false, 20));
                    ProjectSerializer.Save(path, scene, Array.Empty<TimelineTrackData>(), 0, 0);
                }

                var loaded = ProjectSerializer.Load(path);
                using var back = loaded.Scene;
                Assert.Equal(new[] { new Guide(true, 12.5f), new Guide(false, 20) }, back.Guides);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
