using JolieCat.Core.Documents;
using JolieCat.Core.Effects;
using JolieCat.Core.History;
using JolieCat.Core.Rendering;
using JolieCat.Core.Serialization;
using SkiaSharp;

namespace JolieCat.Tests
{
    public class LayerStyleTests
    {
        /// <summary>A 40x40 scene: white background, then a layer with a 10x10 red
        /// square in the middle (15..25).</summary>
        private static Scene SquareScene(out Layer square, LayerStyle style)
        {
            var scene = new Scene("S");
            scene.AddLayer("Bg", 40, 40).Canvas.Clear(SKColors.White);
            square = scene.AddLayer("Square", 40, 40);
            TestHelpers.FillRect(square, new SKRect(15, 15, 25, 25), SKColors.Red);
            square.Style = style;
            return scene;
        }

        [Fact]
        public void Stroke_DrawsOutsideTheShape_AndLeavesItsInsideAlone()
        {
            using var scene = SquareScene(out _, new LayerStyle { Stroke = true, StrokeColor = SKColors.Blue, StrokeSize = 3 });
            using var flat = SceneCompositor.Flatten(scene, 40, 40);

            Assert.Equal(SKColors.Blue, flat.GetPixel(13, 20));   // 2 px outside the left edge
            Assert.Equal(SKColors.Red, flat.GetPixel(20, 20));    // inside
            Assert.Equal(SKColors.White, flat.GetPixel(5, 20));   // far away
        }

        [Fact]
        public void DropShadow_FallsAwayFromTheLight()
        {
            // Light from the top-left (135°) - the shadow goes down-right.
            var style = new LayerStyle { DropShadow = true, ShadowAngle = 135, ShadowDistance = 6, ShadowSize = 0, ShadowOpacity = 100 };
            using var scene = SquareScene(out _, style);
            using var flat = SceneCompositor.Flatten(scene, 40, 40);

            var belowRight = flat.GetPixel(28, 28);
            Assert.True(belowRight.Red < 60, $"expected shadow at (28,28), got {belowRight}");
            Assert.Equal(SKColors.White, flat.GetPixel(12, 12)); // nothing up-left
            Assert.Equal(SKColors.Red, flat.GetPixel(20, 20));   // shadow stays under the content
        }

        [Fact]
        public void ColorOverlay_RecolorsOnlyTheLayersPixels()
        {
            using var scene = SquareScene(out _, new LayerStyle { ColorOverlay = true, OverlayColor = SKColors.Lime });
            using var flat = SceneCompositor.Flatten(scene, 40, 40);

            Assert.Equal(SKColors.Lime, flat.GetPixel(20, 20));
            Assert.Equal(SKColors.White, flat.GetPixel(5, 5));
        }

        [Fact]
        public void OuterGlow_LightsTheSurroundings()
        {
            var style = new LayerStyle { OuterGlow = true, GlowColor = SKColors.Blue, GlowSize = 8, GlowOpacity = 100 };
            using var scene = SquareScene(out _, style);
            using var flat = SceneCompositor.Flatten(scene, 40, 40);

            var near = flat.GetPixel(13, 20);
            Assert.True(near.Blue > near.Red + 40, $"expected bluish glow next to the edge, got {near}");
            Assert.Equal(SKColors.White, flat.GetPixel(1, 1));
        }

        [Fact]
        public void Effects_FollowNewPainting()
        {
            using var scene = SquareScene(out var square, new LayerStyle { Stroke = true, StrokeColor = SKColors.Blue, StrokeSize = 2 });
            TestHelpers.FillRect(square, new SKRect(30, 5, 34, 9), SKColors.Red);

            using var flat = SceneCompositor.Flatten(scene, 40, 40);
            Assert.Equal(SKColors.Blue, flat.GetPixel(29, 7));
        }

        [Fact]
        public void Serialize_RoundTrips()
        {
            var style = new LayerStyle
            {
                DropShadow = true, ShadowColor = new SKColor(10, 20, 30), ShadowAngle = 45.5, ShadowDistance = 12, ShadowSize = 3, ShadowOpacity = 40,
                Stroke = true, StrokeColor = SKColors.Yellow, StrokeSize = 4, StrokeOpacity = 80,
            };

            var back = LayerStyle.Deserialize(style.Serialize());

            Assert.Equal(style, back);
            Assert.Null(LayerStyle.Deserialize(""));
            Assert.Null(LayerStyle.Deserialize("garbage;shadow=#zz,1"));
        }

        [Fact]
        public void Style_SurvivesStructuralUndo_AndProjectSave()
        {
            var style = new LayerStyle { Stroke = true, StrokeColor = SKColors.Blue, StrokeSize = 2 };
            using var scene = SquareScene(out var square, style);

            var snapshot = scene.CaptureLayers();
            scene.RestoreLayers(snapshot, 1);
            Assert.Equal(style, scene.Layers[1].Style);

            var folder = Path.Combine(Path.GetTempPath(), "jc-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var path = Path.Combine(folder, "p.jolie");
                ProjectSerializer.Save(path, scene, Array.Empty<TimelineTrackData>(), 0, 0);
                var loaded = ProjectSerializer.Load(path);
                using (loaded.Scene)
                {
                    Assert.Equal(style, loaded.Scene.Layers[1].Style);
                    Assert.Null(loaded.Scene.Layers[0].Style);
                }
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [Fact]
        public void StyleChange_IsUndoable()
        {
            var style = new LayerStyle { ColorOverlay = true };
            using var scene = SquareScene(out var square, null!);
            var history = new HistoryManager();

            square.Style = style;
            history.Push(new LayerStyleChangeCommand(scene, square, null, style), "Layer Style");

            history.Undo();
            Assert.Null(square.Style);
            history.Redo();
            Assert.Equal(style, square.Style);
        }
    }
}
