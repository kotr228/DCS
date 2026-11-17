using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class MultiLayerFrameTests
    {
        private static void Sta(Action test)
        {
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try { test(); }
                catch (Exception ex) { error = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error is not null) throw new Exception(error.Message, error);
        }

        [Fact]
        public void FramesBecomeGroups_WithSeveralLayersEach() => Sta(() =>
        {
            var doc = new DocumentViewModel(new ToolboxViewModel(), "A", ProjectType.ClipbarAnimation, 40, 30, null);
            var t = doc.Timeline;
            t.NewFrameCommand.Execute(null);
            using (var red = new SKPaint { Color = SKColors.Red }) doc.Layers.ActiveLayer!.Model.Canvas.DrawRect(0, 0, 10, 10, red);
            t.NewFrameCommand.Execute(null);
            t.CurrentFrame = 0;

            t.MakeFramesMultiLayerCommand.Execute(null);
            var groups = t.FrameClips.Select(c => c.TargetLayer!).ToList();
            Assert.All(groups, g => Assert.True(g.IsGroup));
            Assert.Equal("Frame 001", groups[0].Name);
            Assert.Equal("Drawing", doc.Layers.ActiveLayer!.Model.Name);   // drawing inside frame 1

            // A second layer inside frame 1 (colors under the lines).
            doc.Layers.AddLayerCommand.Execute(null);
            var color = doc.Layers.ActiveLayer!.Model;
            color.Name = "Color";
            Assert.Equal(groups[0].Id, color.ParentId);
            using (var blue = new SKPaint { Color = SKColors.Blue }) color.Canvas.DrawRect(20, 20, 10, 10, blue);

            // Frame 2 hides all of frame 1; New Frame on a group frame makes a group.
            t.CurrentFrame = 1;
            Assert.False(doc.Layers.Scene.IsEffectivelyVisible(color));
            t.NewFrameCommand.Execute(null);
            Assert.True(t.FrameClips[2].TargetLayer!.IsGroup);

            // Duplicating frame 1 copies both of its layers, and stays on "Color".
            t.CurrentFrame = 0;
            doc.Layers.ActivateFrameLayer(color);
            t.DuplicateFrameCommand.Execute(null);
            var copy = t.FrameClips[1].TargetLayer!;
            var inside = t.LayersOfFrame(copy).ToList();
            Assert.Equal(new[] { "Drawing", "Color" }.OrderBy(n => n), inside.Select(l => l.Name).OrderBy(n => n));
            Assert.Equal("Color", doc.Layers.ActiveLayer!.Model.Name);
            Assert.Equal(SKColors.Blue, inside.Single(l => l.Name == "Color").Bitmap.GetPixel(25, 25));

            // The frame picture shows both layers; onion skin lists both.
            t.RefreshFrameStrip();
            Assert.NotNull(t.FrameStrip[1].Thumbnail);
            t.OnionSkin = true;
            t.CurrentFrame = 2;
            Assert.Equal(2, t.OnionSkinLayers().Count(o => o.Before));

            // Undo goes back step by step.
            doc.Layers.History.Undo(); doc.Layers.History.Undo();
            Assert.Equal(2, t.FrameClips.Count);
        });
    }
}
