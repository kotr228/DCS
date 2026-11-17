using System.Numerics;
using JolieCat3D.Core.Scene;
using JolieCat3D.Service.Animation;
using JolieCat3D.Service.Commands;

namespace JolieCat3D.Tests
{
    public sealed class NlaTests
    {
        /// <summary>An action moving <paramref name="node"/> along X from <paramref name="from"/> to <paramref name="to"/> over 0..1 s.</summary>
        private static AnimationAction SlideX(AnimationTimeline timeline, Node node, string name, float from, float to)
        {
            var action = timeline.AddAction(name);
            var track = new AnimationTrack(node);
            track.Curve(AnimationChannel.LocationX).Insert(0, from, InterpolationMode.Linear);
            track.Curve(AnimationChannel.LocationX).Insert(1, to, InterpolationMode.Linear);
            action.SetTrack(track);
            return action;
        }

        private static float XAt(AnimationTimeline timeline, Node node, double time)
        {
            timeline.SetTime(time);
            timeline.Apply();
            return node.LocalPosition.X;
        }

        [Fact]
        public void StripTimingRepeatsSpeedsAndHolds()
        {
            var node = new Node("Box");
            var timeline = new AnimationTimeline { TotalFrames = 240 };
            var slide = SlideX(timeline, node, "Slide", 0, 10);
            var strip = new NlaStrip(slide) { Start = 2, Repeat = 2 };
            var track = new NlaTrack("Base");
            track.Strips.Add(strip);
            timeline.NlaTracks.Add(track);

            Assert.Equal(2.0, strip.Length, 6);
            node.LocalPosition = new Vector3(-7, 0, 0);
            Assert.Equal(-7f, XAt(timeline, node, 1), 3);      // before the strip: untouched (HoldForward)
            Assert.Equal(5f, XAt(timeline, node, 2.5), 3);
            Assert.Equal(5f, XAt(timeline, node, 3.5), 3);     // second repeat
            Assert.Equal(10f, XAt(timeline, node, 6), 3);      // held after the end

            strip.Speed = 2;                                   // twice as fast: 2 repeats in 1 s
            Assert.Equal(1.0, strip.Length, 6);
            Assert.Equal(5f, XAt(timeline, node, 2.25), 3);

            strip.Extrapolation = NlaExtrapolation.Hold;       // also holds the first pose before
            Assert.Equal(0f, XAt(timeline, node, 0.5), 3);
        }

        [Fact]
        public void LayersBlendByInfluenceAndAddMode()
        {
            var node = new Node("Box");
            var timeline = new AnimationTimeline();
            var walk = SlideX(timeline, node, "Walk", 10, 10);
            var lean = SlideX(timeline, node, "Lean", 2, 2);
            var scaleAction = timeline.AddAction("Pulse");
            var scaleTrack = new AnimationTrack(node);
            scaleTrack.Curve(AnimationChannel.ScaleY).Insert(0, 2, InterpolationMode.Linear);
            scaleTrack.Curve(AnimationChannel.ScaleY).Insert(1, 2, InterpolationMode.Linear);
            scaleAction.SetTrack(scaleTrack);

            var bottom = new NlaTrack("Walk"); bottom.Strips.Add(new NlaStrip(walk));
            var middle = new NlaTrack("Lean"); middle.Strips.Add(new NlaStrip(lean) { BlendMode = NlaBlendMode.Add, Influence = 0.5f });
            var top = new NlaTrack("Pulse"); top.Strips.Add(new NlaStrip(scaleAction) { BlendMode = NlaBlendMode.Add, Influence = 0.5f });
            timeline.NlaTracks.AddRange(new[] { bottom, middle, top });

            Assert.Equal(11f, XAt(timeline, node, 0.5), 3);    // 10 + 2 * 0.5
            Assert.Equal(1.5f, node.LocalScale.Y, 3);          // scale multiplies: 1 * (1 + (2 - 1) * 0.5)
            Assert.Equal(1f, node.LocalScale.X, 3);

            middle.Strips[0].BlendMode = NlaBlendMode.Replace; // halfway between 10 and 2
            Assert.Equal(6f, XAt(timeline, node, 0.5), 3);

            middle.Muted = true;
            Assert.Equal(10f, XAt(timeline, node, 0.5), 3);

            // Blend in: no effect at the strip's very start, full at the end of the ramp.
            middle.Muted = false;
            middle.Strips[0].BlendIn = 0.5;
            Assert.Equal(10f, XAt(timeline, node, 0), 3);
            Assert.Equal(8f, XAt(timeline, node, 0.25), 3);
        }

        [Fact]
        public void PushDownKeepsThePoseAndActiveActionPlaysOnTop()
        {
            var node = new Node("Box");
            var timeline = new AnimationTimeline();
            var track = timeline.GetOrCreateTrack(node);
            track.Curve(AnimationChannel.LocationX).Insert(0.5, 1, InterpolationMode.Linear);
            track.Curve(AnimationChannel.LocationX).Insert(1.5, 3, InterpolationMode.Linear);
            var original = timeline.ActiveAction;
            var before = XAt(timeline, node, 1);

            var command = NlaStateCommand.Capture(() => timeline, "Push Down", t => t.PushDownActiveAction() is not null);
            Assert.NotNull(command);
            Assert.NotSame(original, timeline.ActiveAction);
            Assert.Single(timeline.NlaTracks);
            Assert.Equal(0.5, timeline.NlaTracks[0].Strips[0].Start, 6);
            Assert.Equal(before, XAt(timeline, node, 1), 3);

            // A key in the new active action overrides that channel on top of the NLA.
            timeline.GetOrCreateTrack(node).Curve(AnimationChannel.LocationX).Insert(0, 42, InterpolationMode.Linear);
            Assert.Equal(42f, XAt(timeline, node, 1), 3);

            command!.Undo();
            Assert.Empty(timeline.NlaTracks);
            Assert.Same(original, timeline.ActiveAction);
            Assert.DoesNotContain(timeline.Actions, a => a.Tracks.Any(t => t.Curve(AnimationChannel.LocationX).Keys.Any(k => k.Value == 42)));
            command.Execute();
            Assert.Single(timeline.NlaTracks);
        }

        [Fact]
        public void BakeMatchesEvaluationAndSurvivesSaving()
        {
            var node = new Node("Box");
            var scene = new Scene3D();
            scene.AddRootNode(node);
            var timeline = new AnimationTimeline { FrameRate = 10, TotalFrames = 30 };
            var slide = SlideX(timeline, node, "Slide", 0, 4);
            var nla = new NlaTrack("Base");
            nla.Strips.Add(new NlaStrip(slide) { Start = 0.5, Repeat = 2, Speed = 0.5, Extrapolation = NlaExtrapolation.Hold, BlendOut = 0.2, BlendMode = NlaBlendMode.Add, Influence = 0.75f });
            timeline.NlaTracks.Add(nla);

            var baked = timeline.BakeNla("Baked");
            Assert.True(baked.TryGetTrack(node, out var bakedTrack));
            for (var frame = 0; frame <= 30; frame += 7)
            {
                var expected = XAt(timeline, node, frame / 10.0);
                Assert.Equal(expected, bakedTrack!.Curve(AnimationChannel.LocationX).Evaluate(frame / 10.0)!.Value, 3);
            }

            var data = AnimationExporter.BuildExportData(timeline);
            var loaded = new AnimationTimeline();
            AnimationExporter.ApplyExportData(data, loaded, scene);
            var strip = Assert.Single(Assert.Single(loaded.NlaTracks).Strips);
            Assert.Equal("Slide", strip.Action.Name);
            Assert.Contains(strip.Action, loaded.Actions);
            Assert.Equal((0.5, 2.0, 0.5, 0.2, 0.75f), (strip.Start, strip.Repeat, strip.Speed, strip.BlendOut, strip.Influence));
            Assert.Equal((NlaBlendMode.Add, NlaExtrapolation.Hold), (strip.BlendMode, strip.Extrapolation));
        }
    }
}
