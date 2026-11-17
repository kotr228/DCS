using JolieCat.Core.Rendering;
using JolieCat.Core.Serialization;
using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class BonesTests
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
        public void AnAttachedLayer_FollowsItsParent_AroundTheParentsJoint() => Sta(() =>
        {
            var doc = new DocumentViewModel(new ToolboxViewModel(), "Arm", ProjectType.ClipbarAnimation, 400, 400, null);
            var layers = doc.Layers;
            layers.Scene.Layers[0].IsVisible = false;
            layers.AddLayerCommand.Execute(null);
            var upper = layers.ActiveLayer!.Model; upper.Name = "Upper";
            using (var red = new SKPaint { Color = SKColors.Red }) upper.Canvas.DrawRect(100, 90, 100, 20, red);
            layers.AddLayerCommand.Execute(null);
            var fore = layers.ActiveLayer!.Model; fore.Name = "Fore";
            using (var blue = new SKPaint { Color = SKColors.Blue }) fore.Canvas.DrawRect(200, 90, 100, 20, blue);

            var t = doc.Timeline;
            layers.ActivateFrameLayer(fore);
            t.AnimateActiveLayerCommand.Execute(null);                 // the forearm: no motion of its own
            var foreTrack = t.SelectedTrack!;
            layers.ActivateFrameLayer(upper);
            t.AnimateActiveLayerCommand.Execute(null);
            var upperTrack = t.SelectedTrack!;
            t.PivotX = 100; t.PivotY = 100;                           // the shoulder
            t.CurrentFrame = 10;
            t.PoseRotation = 90;                                       // swing the upper arm down

            t.SelectedTrack = foreTrack;
            Assert.Contains("Upper", t.ParentChoices);
            t.SelectedParent = "Upper";                                // attach the forearm
            Assert.Equal("Upper", t.SelectedParent);

            using (var flat = SceneCompositor.Flatten(layers.Scene, 400, 400))
            {
                Assert.Equal(SKColors.Red, flat.GetPixel(100, 150));   // the upper arm points down
                Assert.Equal(SKColors.Blue, flat.GetPixel(100, 250));  // and the forearm came with it
                Assert.Equal(0, flat.GetPixel(250, 100).Alpha);        // not left where it was
            }

            // No loops: the upper arm can't be attached to its own forearm.
            t.SelectedTrack = upperTrack;
            Assert.DoesNotContain("Fore", t.ParentChoices);

            // Saved and loaded (by layer name), joint included.
            var data = new[]
            {
                new TimelineTrackData { Name = "Upper", LayerName = "Upper", PivotX = 100, PivotY = 100,
                    Keys = new() { new KeyframeData { Frame = 0 }, new KeyframeData { Frame = 10, Rotation = 90 } } },
                new TimelineTrackData { Name = "Fore", LayerName = "Fore", ParentLayerName = "Upper", Keys = new() { new KeyframeData { Frame = 0 } } },
            };
            t.LoadTracks(data, 240, 24);
            t.RewireFrameLayers(layers.Scene);
            t.CurrentFrame = 10;
            using (var again = SceneCompositor.Flatten(layers.Scene, 400, 400))
                Assert.Equal(SKColors.Blue, again.GetPixel(100, 250));
        });

        [Fact]
        public void Attaching_CanBeUndone() => Sta(() =>
        {
            var doc = new DocumentViewModel(new ToolboxViewModel(), "A", ProjectType.ClipbarAnimation, 100, 100, null);
            doc.Layers.AddLayerCommand.Execute(null);
            var a = doc.Layers.ActiveLayer!.Model; a.Name = "A";
            doc.Timeline.AnimateActiveLayerCommand.Execute(null);
            doc.Layers.AddLayerCommand.Execute(null);
            doc.Layers.ActiveLayer!.Model.Name = "B";
            doc.Timeline.AnimateActiveLayerCommand.Execute(null);
            doc.Timeline.SelectedParent = "A";
            Assert.Equal("A", doc.Timeline.SelectedParent);
            doc.Layers.History.Undo();
            Assert.Equal("(none)", doc.Timeline.SelectedParent);
        });
    }
}
