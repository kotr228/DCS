using JolieCat.Core.Rendering;
using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class TimelineUndoTests
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

        private static DocumentViewModel NewAnimation(ToolboxViewModel? toolbox = null) =>
            new(toolbox ?? new ToolboxViewModel(), "Anim", ProjectType.ClipbarAnimation, 80, 60, null);

        [Fact]
        public void NewFrame_IsOneUndoStep_ForTheLayerAndTheClip() => Sta(() =>
        {
            var doc = NewAnimation();
            var history = doc.Layers.History;
            doc.Timeline.NewFrameCommand.Execute(null);
            doc.Timeline.NewFrameCommand.Execute(null);
            Assert.Equal(2, doc.Timeline.FrameClips.Count);
            Assert.Equal(3, doc.Layers.Scene.Layers.Count);           // background + 2 frames

            history.Undo();
            Assert.Single(doc.Timeline.FrameClips);                   // back to how it was
            Assert.Equal(2, doc.Layers.Scene.Layers.Count);

            history.Redo();
            Assert.Equal(2, doc.Timeline.FrameClips.Count);
            Assert.Equal(3, doc.Layers.Scene.Layers.Count);
            Assert.All(doc.Timeline.FrameClips, c => Assert.Contains(c.TargetLayer, doc.Layers.Scene.Layers));
        });

        [Fact]
        public void FramesKeepWorking_AfterUndoingAnUnrelatedLayerChange() => Sta(() =>
        {
            var doc = NewAnimation();
            doc.Timeline.NewFrameCommand.Execute(null);
            doc.Timeline.NewFrameCommand.Execute(null);
            doc.Layers.AddLayerCommand.Execute(null);                 // something else
            doc.Layers.History.Undo();                                // rebuilds every layer object

            var frames = doc.Timeline.FrameClips;
            Assert.All(frames, c => Assert.Contains(c.TargetLayer, doc.Layers.Scene.Layers));
            doc.Timeline.CurrentFrame = 0;
            Assert.True(frames[0].TargetLayer!.IsVisible);
            Assert.False(frames[1].TargetLayer!.IsVisible);
        });

        [Fact]
        public void DraggingAKeyframeSlider_IsOneUndoStep() => Sta(() =>
        {
            var doc = NewAnimation();
            var timeline = doc.Timeline;
            timeline.AnimateActiveLayerCommand.Execute(null);
            timeline.CurrentFrame = 8;
            var steps = doc.Layers.History.UndoNames.Count;
            for (var x = 1; x <= 20; x++) timeline.PoseX = x;         // a slider drag
            Assert.Equal(steps + 1, doc.Layers.History.UndoNames.Count);

            doc.Layers.History.Undo();
            Assert.Single(timeline.Tracks[0].Keyframes);              // the key at frame 8 is gone again
        });

        [Fact]
        public void MovingAClip_CanBeUndone() => Sta(() =>
        {
            var doc = NewAnimation();
            var timeline = doc.Timeline;
            timeline.NewFrameCommand.Execute(null);
            timeline.NewFrameCommand.Execute(null);
            var clip = timeline.FrameClips[1];
            clip.BeginGesture();
            clip.ResizeEndBy(timeline.PixelsPerFrame * 2.4);         // drag the edge ~2 frames
            clip.EndGesture("Trim Clip");
            Assert.Equal(3, timeline.FrameClips[1].LengthFrames);     // snapped to whole frames

            doc.Layers.History.Undo();
            Assert.Equal(1, timeline.FrameClips[1].LengthFrames);
        });

        [Fact]
        public void DraggingAKeyframe_MovesItInTime_AndCanBeUndone() => Sta(() =>
        {
            var doc = NewAnimation();
            var timeline = doc.Timeline;
            timeline.AnimateActiveLayerCommand.Execute(null);
            timeline.CurrentFrame = 10;
            timeline.PoseX = 50;
            var key = timeline.Tracks[0].Keyframes.Single(k => k.Frame == 10);

            timeline.BeginKeyDrag(key);
            timeline.DragKeyBy(timeline.PixelsPerFrame * 5.2);      // ~5 frames later
            timeline.EndKeyDrag();
            Assert.Equal(15, key.Frame);
            timeline.CurrentFrame = 15;
            Assert.Equal(50, timeline.PoseX, 1);

            var steps = doc.Layers.History.UndoNames.Count;
            timeline.BeginKeyDrag(key);                            // a click without moving
            timeline.EndKeyDrag();
            Assert.Equal(steps, doc.Layers.History.UndoNames.Count);

            doc.Layers.History.Undo();
            Assert.Contains(timeline.Tracks[0].Keyframes, k => k.Frame == 10);
        });

        [Fact]
        public void EasingModes_AndCopyPastePose() => Sta(() =>
        {
            var doc = NewAnimation();
            var timeline = doc.Timeline;
            timeline.AnimateActiveLayerCommand.Execute(null);
            timeline.PoseEasing = Core.Animation.KeyEasing.Hold;
            timeline.CurrentFrame = 10;
            timeline.PoseX = 80;
            timeline.CurrentFrame = 9;
            Assert.Equal(0, timeline.PoseX);                        // Hold: no in-between
            timeline.CurrentFrame = 10;
            timeline.CopyPoseCommand.Execute(null);

            timeline.CurrentFrame = 20;
            timeline.PastePoseCommand.Execute(null);
            Assert.Equal(3, timeline.Tracks[0].Keyframes.Count);
            Assert.Equal(80, timeline.PoseX);
        });

        [Fact]
        public void AGroupMoves_AndTheMoveToolSetsItsPosition() => Sta(() =>
        {
            var toolbox = new ToolboxViewModel();
            var doc = NewAnimation(toolbox);
            var layer = doc.Layers.ActiveLayer!.Model;
            layer.Canvas.Clear(SKColors.Transparent);
            using (var red = new SKPaint { Color = SKColors.Red })
                layer.Canvas.DrawRect(0, 0, 10, 10, red);
            doc.Layers.GroupLayersCommand.Execute(null);
            var group = doc.Layers.ActiveLayer!.Model;
            Assert.True(group.IsGroup);

            var timeline = doc.Timeline;
            timeline.AnimateActiveLayerCommand.Execute(null);
            Assert.Same(group, timeline.Tracks[0].TargetLayer);
            timeline.CurrentFrame = 5;

            // The Move tool on the layer inside the group drags the group's position.
            doc.Layers.ActivateFrameLayer(layer);
            toolbox.SelectTool(ToolType.Move);
            var canvas = doc.Canvas;
            canvas.SetViewportSize(800, 600);
            SKPoint Device(float x, float y) => new((float)(x * canvas.Zoom + canvas.PanX), (float)(y * canvas.Zoom + canvas.PanY));
            canvas.OnPointerPressed(Device(5, 5));
            canvas.OnPointerMoved(Device(45, 25));
            canvas.OnPointerReleased(Device(45, 25));

            Assert.Equal(40, timeline.PoseX, 0);
            Assert.Equal(20, timeline.PoseY, 0);
            Assert.Equal(255, layer.Bitmap.GetPixel(5, 5).Alpha);    // pixels not moved
            using var flat = SceneCompositor.Flatten(doc.Layers.Scene, 80, 60);
            Assert.Equal(255, flat.GetPixel(45, 25).Alpha);          // drawn moved
            Assert.Equal(0, flat.GetPixel(5, 5).Alpha);
        });
    }
}
