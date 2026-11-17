using JolieCat.Core.Rendering;
using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class CameraTests
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
        public void KeyedColors_FadeALayer_AndAGroup_ToBlack() => Sta(() =>
        {
            var doc = new DocumentViewModel(new ToolboxViewModel(), "K", ProjectType.ClipbarAnimation, 60, 40, null);
            var layers = doc.Layers;
            var layer = layers.ActiveLayer!.Model;
            layer.Canvas.Clear(SKColors.Red);
            var t = doc.Timeline;
            t.AnimateActiveLayerCommand.Execute(null);
            t.CurrentFrame = 0; t.PoseBrightness = 1; t.PoseBrightness = 0;
            t.CurrentFrame = 10; t.PoseBrightness = -100;
            Assert.NotNull(layer.AnimationColor);
            using (var view = SceneCompositor.Flatten(layers.Scene, 60, 40))
                Assert.Equal(SKColors.Black, view.GetPixel(30, 20));
            t.CurrentFrame = 5;
            using (var view = SceneCompositor.Flatten(layers.Scene, 60, 40))
                Assert.InRange((int)view.GetPixel(30, 20).Red, 90, 170);
            t.CurrentFrame = 0;
            Assert.Null(layer.AnimationColor);

            // The same on a group (Saturation 0: gray).
            layers.GroupLayersCommand.Execute(null);
            var group = layers.Scene.ActiveLayer!;
            t.AnimateActiveLayerCommand.Execute(null);
            t.CurrentFrame = 10;
            t.PoseSaturation = 0;
            Assert.NotNull(group.AnimationColor);
            using (var view = SceneCompositor.Flatten(layers.Scene, 60, 40))
            {
                var c = view.GetPixel(30, 20);
                Assert.Equal(c.Red, c.Blue);
            }
        });

        [Fact]
        public void MotionBlur_WorksOnAGroup() => Sta(() =>
        {
            var doc = new DocumentViewModel(new ToolboxViewModel(), "MG", ProjectType.ClipbarAnimation, 300, 100, null);
            var layers = doc.Layers;
            layers.Scene.Layers[0].IsVisible = false;
            layers.AddLayerCommand.Execute(null);
            using (var red = new SKPaint { Color = SKColors.Red }) layers.ActiveLayer!.Model.Canvas.DrawRect(40, 40, 20, 20, red);
            layers.GroupLayersCommand.Execute(null);
            var group = layers.Scene.ActiveLayer!;
            var t = doc.Timeline;
            t.AnimateActiveLayerCommand.Execute(null);
            t.CurrentFrame = 0; t.PoseX = 0.01; t.PoseX = 0;
            t.CurrentFrame = 10; t.PoseX = 200;
            t.CurrentFrame = 5;
            t.SelectedMotionBlur = true;
            Assert.NotNull(group.AnimationMotion);
            using var view = SceneCompositor.Flatten(layers.Scene, 300, 100);
            var x = (int)group.AnimationMatrix!.Value.MapPoint(new SKPoint(50, 50)).X;
            Assert.Equal(255, view.GetPixel(x, 50).Alpha);
            Assert.InRange((int)view.GetPixel(x - 15, 50).Alpha, 10, 245);
        });

        [Fact]
        public void MotionBlur_SmearsAFastMove_AndKeepsStillPartsSolid() => Sta(() =>
        {
            var doc = new DocumentViewModel(new ToolboxViewModel(), "M", ProjectType.ClipbarAnimation, 300, 100, null);
            var layer = doc.Layers.ActiveLayer!.Model;
            layer.Canvas.Clear(SKColors.Transparent);
            using (var red = new SKPaint { Color = SKColors.Red }) layer.Canvas.DrawRect(40, 40, 20, 20, red);
            var t = doc.Timeline;
            t.AnimateActiveLayerCommand.Execute(null);
            t.CurrentFrame = 0;
            t.PoseX = 0.01; t.PoseX = 0;                         // a key at 0
            t.CurrentFrame = 10;
            t.PoseX = 200;                                       // 20 px a frame
            t.CurrentFrame = 5;
            Assert.Null(layer.AnimationMotion);                  // off by default

            t.SelectedMotionBlur = true;
            Assert.NotNull(layer.AnimationMotion);
            using (var view = SceneCompositor.Flatten(doc.Layers.Scene, 300, 100))
            {
                var x = (int)(layer.AnimationMatrix!.Value.MapPoint(new SKPoint(50, 50)).X);
                Assert.Equal(255, view.GetPixel(x, 50).Alpha);              // the middle: solid
                var tail = view.GetPixel(x - 15, 50).Alpha;                  // just past the square's edge
                Assert.InRange((int)tail, 10, 245);                          // smeared, see-through
            }

            doc.Layers.History.Undo();
            Assert.False(t.SelectedMotionBlur);
            Assert.Null(layer.AnimationMotion);
        });

        [Fact]
        public void Bend_WorksOnAGroup() => Sta(() =>
        {
            var doc = new DocumentViewModel(new ToolboxViewModel(), "G", ProjectType.ClipbarAnimation, 200, 200, null);
            var layers = doc.Layers;
            layers.Scene.Layers[0].IsVisible = false;
            layers.AddLayerCommand.Execute(null);
            using (var red = new SKPaint { Color = SKColors.Red }) layers.ActiveLayer!.Model.Canvas.DrawRect(20, 90, 70, 20, red);    // upper arm
            layers.AddLayerCommand.Execute(null);
            using (var blue = new SKPaint { Color = SKColors.Blue }) layers.ActiveLayer!.Model.Canvas.DrawRect(90, 90, 70, 20, blue);  // forearm
            layers.SetSelectedLayers(layers.Scene.Layers.Skip(1).ToList());
            layers.GroupLayersCommand.Execute(null);
            var group = layers.Scene.ActiveLayer!;
            Assert.True(group.IsGroup);

            var t = doc.Timeline;
            t.AnimateActiveLayerCommand.Execute(null);
            t.PivotX = 20; t.PivotY = 100;
            t.BendX = 90; t.BendY = 100;
            t.CurrentFrame = 10;
            t.PoseBend = 90;
            Assert.NotNull(group.AnimationBend);
            using (var view = SceneCompositor.Flatten(layers.Scene, 200, 200))
            {
                Assert.Equal(SKColors.Red, view.GetPixel(40, 100));        // the upper arm stays
                Assert.True(view.GetPixel(150, 100).Alpha < 10);           // the forearm turned...
                Assert.Equal(SKColors.Blue, view.GetPixel(90, 150));       // ...to point down
            }
            t.CurrentFrame = 0;
            Assert.Null(group.AnimationBend);
        });

        [Fact]
        public void KeyedBlur_WorksOnAGroupToo() => Sta(() =>
        {
            var doc = new DocumentViewModel(new ToolboxViewModel(), "G", ProjectType.ClipbarAnimation, 100, 80, null);
            var layers = doc.Layers;
            layers.Scene.Layers[0].IsVisible = false;
            layers.AddLayerCommand.Execute(null);
            var inner = layers.ActiveLayer!.Model;
            using (var red = new SKPaint { Color = SKColors.Red }) inner.Canvas.DrawRect(0, 0, 50, 80, red);
            layers.GroupLayersCommand.Execute(null);                  // Ctrl+G: the group becomes active
            var group = layers.Scene.ActiveLayer!;
            Assert.True(group.IsGroup);
            var t = doc.Timeline;
            t.AnimateActiveLayerCommand.Execute(null);
            t.CurrentFrame = 10;
            t.PoseBlur = 20;
            Assert.Same(group, t.SelectedTrack?.TargetLayer);
            Assert.Equal(20, t.PoseBlur);
            Assert.True(group.AnimationBlur > 15);
            using var view = SceneCompositor.Flatten(layers.Scene, 100, 80);
            Assert.True(view.GetPixel(53, 40).Alpha > 10);      // the red spills past its edge
            Assert.True(view.GetPixel(20, 40).Alpha > 200);
        });

        [Fact]
        public void Bend_BendsOneDrawingAtItsBendPoint_AndUndoes() => Sta(() =>
        {
            var doc = new DocumentViewModel(new ToolboxViewModel(), "Arm", ProjectType.ClipbarAnimation, 200, 200, null);
            var layer = doc.Layers.ActiveLayer!.Model;
            layer.Canvas.Clear(SKColors.Transparent);
            using (var red = new SKPaint { Color = SKColors.Red }) layer.Canvas.DrawRect(20, 90, 140, 20, red);  // an arm, x 20..160
            var t = doc.Timeline;
            t.AnimateActiveLayerCommand.Execute(null);
            t.PivotX = 20; t.PivotY = 100;                  // shoulder
            t.BendX = 90; t.BendY = 100;                    // elbow
            t.CurrentFrame = 10;
            t.PoseBend = 90;
            Assert.NotNull(layer.AnimationBitmap);
            using (var view = SceneCompositor.Flatten(doc.Layers.Scene, 200, 200))
            {
                Assert.Equal(255, view.GetPixel(40, 100).Alpha);     // upper arm stays
                Assert.True(view.GetPixel(150, 100).Alpha < 10);     // the hand moved...
                Assert.Equal(255, view.GetPixel(90, 150).Alpha);     // ...down from the elbow
            }
            Assert.NotNull(t.BendMarker());

            t.CurrentFrame = 0;                              // the first key: straight
            Assert.Null(layer.AnimationBitmap);

            t.CurrentFrame = 10;
            doc.Layers.History.Undo();                       // the bend key
            Assert.Null(layer.AnimationBitmap);
        });

        [Fact]
        public void KeyedBlur_SoftensTheLayer_InBetweenKeys() => Sta(() =>
        {
            var doc = new DocumentViewModel(new ToolboxViewModel(), "B", ProjectType.ClipbarAnimation, 100, 80, null);
            var layer = doc.Layers.ActiveLayer!.Model;
            layer.Canvas.Clear(SKColors.Transparent);
            using (var red = new SKPaint { Color = SKColors.Red }) layer.Canvas.DrawRect(0, 0, 50, 80, red);
            var t = doc.Timeline;
            t.AnimateActiveLayerCommand.Execute(null);
            t.CurrentFrame = 0;
            t.PoseOpacity = 99.9;                    // a key at 0, sharp
            t.PoseOpacity = 100;
            t.CurrentFrame = 10;
            t.PoseBlur = 20;                         // a key at 10, blurred
            t.CurrentFrame = 5;
            Assert.InRange(layer.AnimationBlur, 5, 15);
            Assert.InRange(t.PoseBlur, 5, 15);
            using (var view = SceneCompositor.Flatten(doc.Layers.Scene, 100, 80))
            {
                var edge = view.GetPixel(52, 40);    // just outside the red: blurred red spills over
                Assert.True(edge.Alpha > 10);
            }
            t.CurrentFrame = 0;
            Assert.Equal(0, layer.AnimationBlur, 3);
        });

        [Fact]
        public void Camera_PansAndZoomsTheWholePicture_OnlyWhenShown() => Sta(() =>
        {
            var doc = new DocumentViewModel(new ToolboxViewModel(), "A", ProjectType.ClipbarAnimation, 100, 80, null);
            var layer = doc.Layers.ActiveLayer!.Model;
            using (var red = new SKPaint { Color = SKColors.Red })
            using (var blue = new SKPaint { Color = SKColors.Blue })
            {
                layer.Canvas.DrawRect(0, 0, 50, 80, red);
                layer.Canvas.DrawRect(50, 0, 50, 80, blue);
            }
            var t = doc.Timeline;
            t.AddCameraCommand.Execute(null);
            var camera = Assert.Single(t.Tracks);
            Assert.True(camera.IsCamera);
            Assert.True(t.CanEditPose);

            t.CurrentFrame = 10;
            t.PoseX = 25;                                   // look 25 px to the right
            t.PoseScale = 200;                              // and zoom in 2x
            Assert.Equal(11, t.PlaybackEnd);
            Assert.NotNull(doc.Layers.Scene.CameraMatrix);  // the camera track is selected: shown

            using (var view = SceneCompositor.Flatten(doc.Layers.Scene, 100, 80))
            {
                Assert.Equal(SKColors.Blue, view.GetPixel(50, 40));   // the middle shows x = 75
                Assert.Equal(SKColors.Blue, view.GetPixel(2, 40));    // 2x zoom: screen x 2 is picture x 51

            }

            // Opacity fades the whole picture.
            t.PoseOpacity = 50;
            using (var faded = SceneCompositor.Flatten(doc.Layers.Scene, 100, 80))
                Assert.InRange(faded.GetPixel(50, 40).Alpha, 120, 135);
            t.PoseOpacity = 100;

            // The Move tool with the camera selected pans the camera.
            Assert.True(t.BeginAnimatedMove(layer));
            t.UpdateAnimatedMove(10, 4);
            Assert.Equal(15, t.PoseX, 1);
            Assert.Equal(-4, t.PoseY, 1);

            // Drawing on something else: the canvas shows the picture as it is.
            t.SelectedTrack = null;
            Assert.Null(doc.Layers.Scene.CameraMatrix);

            doc.Layers.History.Undo();                      // the keyframe (pan and zoom edits of one key are one step)
            Assert.Single(t.Tracks[0].Keyframes);
            doc.Layers.History.Undo();                      // the camera itself
            Assert.Empty(t.Tracks);
        });
    }
}
