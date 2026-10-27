using JolieCat.Core.Documents;
using JolieCat.Core.Rendering;
using SkiaSharp;

namespace JolieCat.Tests
{
    public class SceneTests
    {
        [Fact]
        public void Compositor_BlackMaskHidesLayer()
        {
            using var scene = new Scene("S");
            var background = scene.AddLayer("Bg", 10, 10);
            background.Canvas.Clear(SKColors.Blue);
            var top = scene.AddLayer("Top", 10, 10);
            top.Canvas.Clear(SKColors.Red);
            var mask = top.AddMask();
            using (var black = new SKPaint { Color = SKColors.Black })
                mask.Canvas.DrawRect(0, 0, 5, 10, black);

            using var flat = SceneCompositor.Flatten(scene, 10, 10);

            Assert.Equal(SKColors.Blue, flat.GetPixel(2, 5)); // masked out
            Assert.Equal(SKColors.Red, flat.GetPixel(8, 5));  // visible
        }

        [Fact]
        public void Compositor_RespectsOpacity()
        {
            using var scene = new Scene("S");
            scene.AddLayer("Bg", 4, 4).Canvas.Clear(SKColors.Black);
            var top = scene.AddLayer("Top", 4, 4);
            top.Canvas.Clear(SKColors.White);
            top.Opacity = 0.5;

            using var flat = SceneCompositor.Flatten(scene, 4, 4);

            Assert.InRange(flat.GetPixel(1, 1).Red, 120, 135);
        }

        [Fact]
        public void DuplicateLayer_CopiesPixelsAboveOriginal()
        {
            using var scene = new Scene("S");
            var original = scene.AddLayer("Art", 8, 8);
            original.Canvas.Clear(SKColors.Green);
            original.BlendMode = Shared.Enums.BlendMode.Screen;
            scene.AddLayer("Top", 8, 8);

            var copy = scene.DuplicateLayer(original);

            Assert.NotNull(copy);
            Assert.Equal(3, scene.Layers.Count);
            Assert.Same(copy, scene.Layers[1]);
            Assert.Same(copy, scene.ActiveLayer);
            Assert.Equal("Art copy", copy!.Name);
            Assert.NotEqual(original.Id, copy.Id);
            Assert.Equal(Shared.Enums.BlendMode.Screen, copy.BlendMode);
            Assert.Equal(SKColors.Green, copy.Bitmap.GetPixel(4, 4));
        }

        [Fact]
        public void RestoreLayers_KeepsLayerIds()
        {
            using var scene = new Scene("S");
            var layer = scene.AddLayer("A", 4, 4);
            var id = layer.Id;
            var snapshot = scene.CaptureLayers();

            scene.RestoreLayers(snapshot, 0);

            Assert.NotSame(layer, scene.Layers[0]);
            Assert.Equal(id, scene.Layers[0].Id);
        }

        [Fact]
        public void ResizeLayers_KeepsLayerIds()
        {
            using var scene = new Scene("S");
            var id = scene.AddLayer("A", 4, 4).Id;

            scene.ResizeLayers(8, 6);

            Assert.Equal(id, scene.Layers[0].Id);
            Assert.Equal(8, scene.Layers[0].Bitmap.Width);
        }

        [Fact]
        public void Selection_SelectAllThenInvert_IsEmpty()
        {
            var selection = new Selection();
            selection.SelectAll(20, 10);
            Assert.True(selection.HasSelection);

            selection.Invert(20, 10);
            Assert.False(selection.HasSelection);
        }

        [Fact]
        public void Selection_Invert_SwapsInsideAndOutside()
        {
            var selection = new Selection();
            selection.SetRect(new SKRectI(0, 0, 10, 10));

            selection.Invert(20, 20);

            Assert.True(selection.HasSelection);
            Assert.False(selection.Region!.Contains(5, 5));
            Assert.True(selection.Region.Contains(15, 15));
        }
    }
}
