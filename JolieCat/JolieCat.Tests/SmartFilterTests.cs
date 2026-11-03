using JolieCat.Core.Actions;
using JolieCat.Core.Documents;
using JolieCat.Core.Effects;
using JolieCat.Core.History;
using SkiaSharp;

namespace JolieCat.Tests
{
    /// <summary>Smart Filters in the core, with a tiny test engine: "Invert" and
    /// "AddRed" (+N red).</summary>
    [Collection("SmartFilterEngine")]
    public class SmartFilterTests : IDisposable
    {
        private readonly Func<ActionStep, SKBitmap, SKBitmap?>? _previous = SmartFilterEngine.Apply;

        public SmartFilterTests()
        {
            SmartFilterEngine.Apply = (step, source) =>
            {
                var pixels = source.Pixels;
                for (var i = 0; i < pixels.Length; i++)
                {
                    var c = pixels[i];
                    pixels[i] = step.Command switch
                    {
                        "Invert" => new SKColor((byte)(255 - c.Red), (byte)(255 - c.Green), (byte)(255 - c.Blue), c.Alpha),
                        "AddRed" => new SKColor((byte)Math.Min(255, c.Red + step.Number("N")), c.Green, c.Blue, c.Alpha),
                        _ => c,
                    };
                }
                if (step.Command is not ("Invert" or "AddRed")) return null;
                var result = new SKBitmap(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Premul) { Pixels = pixels };
                return result;
            };
        }

        public void Dispose() => SmartFilterEngine.Apply = _previous;

        private static SmartFilter Filter(string command, double n = 0) =>
            new(new ActionStep { Kind = "Test", Command = command, Title = command }.With("N", n));

        private static Layer SmartLayer(Scene scene, params SmartFilter[] filters)
        {
            var layer = scene.AddLayer("L", 20, 10);
            layer.Canvas.Clear(new SKColor(100, 100, 100));
            layer.SetSmartFilters(layer.Bitmap.Copy(), filters);
            return layer;
        }

        [Fact]
        public void FiltersRunInOrder_OnTheUntouchedOriginal()
        {
            using var scene = new Scene("S");
            var layer = SmartLayer(scene, Filter("AddRed", 50), Filter("Invert"));
            Assert.Equal(new SKColor(105, 155, 155), layer.Bitmap.GetPixel(3, 3)); // (100+50) inverted

            // Hiding the first filter re-runs only the second, on the original.
            layer.ReplaceSmartFilters(new[] { Filter("AddRed", 50) with { Enabled = false }, Filter("Invert") });
            Assert.Equal(new SKColor(155, 155, 155), layer.Bitmap.GetPixel(3, 3));
            Assert.Equal(new SKColor(100, 100, 100), layer.SmartFilterSource!.GetPixel(3, 3));
        }

        [Fact]
        public void ChangeCommand_UndoesAndRedoes()
        {
            using var scene = new Scene("S");
            var layer = SmartLayer(scene);
            var before = layer.SmartFilters;
            var after = new[] { Filter("Invert") };
            layer.ReplaceSmartFilters(after);
            var command = new SmartFilterChangeCommand(scene, layer, before, after);

            command.Undo();
            Assert.Equal(new SKColor(100, 100, 100), layer.Bitmap.GetPixel(0, 0));
            command.Redo();
            Assert.Equal(new SKColor(155, 155, 155), layer.Bitmap.GetPixel(0, 0));
        }

        [Fact]
        public void SnapshotsAndDuplicates_KeepTheOriginalAndTheList()
        {
            using var scene = new Scene("S");
            var layer = SmartLayer(scene, Filter("Invert"));

            var copy = scene.DuplicateLayer(layer)!;
            Assert.True(copy.HasSmartFilters);
            Assert.Equal(new SKColor(155, 155, 155), copy.Bitmap.GetPixel(0, 0));

            var snapshot = scene.CaptureLayers();
            scene.RestoreLayers(snapshot, 0);
            var restored = scene.Layers[0];
            Assert.True(restored.HasSmartFilters);
            Assert.Single(restored.SmartFilters);
            Assert.Equal(new SKColor(100, 100, 100), restored.SmartFilterSource!.GetPixel(0, 0));
        }

        [Fact]
        public void CanvasChanges_TransformTheOriginalToo()
        {
            using var scene = new Scene("S");
            var layer = SmartLayer(scene, Filter("AddRed", 50));
            layer.SmartFilterSource!.SetPixel(0, 0, SKColors.Black);   // a marker in the original
            layer.RenderSmartFilters();

            scene.FlipLayers(horizontal: true);
            var flipped = scene.Layers[0];
            Assert.Equal(SKColors.Black, flipped.SmartFilterSource!.GetPixel(19, 0));
            Assert.Equal(new SKColor(50, 0, 0), flipped.Bitmap.GetPixel(19, 0));   // filters ran again

            scene.ResizeLayers(30, 10, 5, 0);
            Assert.Equal(30, scene.Layers[0].SmartFilterSource!.Width);
            Assert.Equal(SKColors.Black, scene.Layers[0].SmartFilterSource!.GetPixel(24, 0));
        }

        [Fact]
        public void SmartFilterLayers_AreSavedAndLoaded()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sf_{Guid.NewGuid():N}.jolie");
            try
            {
                using (var scene = new Scene("S"))
                {
                    SmartLayer(scene, Filter("AddRed", 50), Filter("Invert") with { Enabled = false });
                    Core.Serialization.ProjectSerializer.Save(path, scene, Array.Empty<Core.Serialization.TimelineTrackData>(), 0, 0);
                }

                var loaded = Core.Serialization.ProjectSerializer.Load(path);
                using var back = loaded.Scene;
                var layer = back.Layers[0];
                Assert.True(layer.HasSmartFilters);
                Assert.Equal(2, layer.SmartFilters.Count);
                Assert.False(layer.SmartFilters[1].Enabled);
                Assert.Equal(new SKColor(100, 100, 100), layer.SmartFilterSource!.GetPixel(1, 1));
                Assert.Equal(new SKColor(150, 100, 100), layer.Bitmap.GetPixel(1, 1));
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }

        [Fact]
        public void FilterLists_RoundTripThroughJson()
        {
            var filters = new[] { Filter("AddRed", 7), Filter("Invert") with { Enabled = false } };
            var back = SmartFilter.Deserialize(SmartFilter.Serialize(filters));
            Assert.Equal(2, back.Count);
            Assert.Equal(7, back[0].Step.Number("N"));
            Assert.False(back[1].Enabled);
            Assert.Empty(SmartFilter.Deserialize("garbage"));
        }
    }
}
