using System.Numerics;
using JolieCat3D.Core.Constraints;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.Service.Animation
{
    /// <summary>
    /// Bake Action (Blender, "Visual Keying"): plays the active action frame by frame,
    /// runs every constraint (IK, Track To, ...) and records the resulting transforms of
    /// every node a constraint moved as keyframes. Game engines and glTF don't evaluate
    /// this app's constraints, so baking is what makes IK animation survive export.
    /// Optionally removes the constraints afterwards (they would fight the keys).
    /// </summary>
    public static class ConstraintBaker
    {
        public sealed record Result(IReadOnlyList<Node> BakedNodes, int Frames);

        /// <summary>
        /// Bakes frames <paramref name="startFrame"/>..<paramref name="endFrame"/> (inclusive,
        /// every <paramref name="step"/> frames) into the active action. Returns the nodes
        /// that received keys. The scene is left posed at the timeline's original time.
        /// </summary>
        public static Result Bake(Scene3D scene, AnimationTimeline timeline, int startFrame, int endFrame, int step = 1, bool removeConstraints = true)
        {
            ArgumentNullException.ThrowIfNull(scene);
            ArgumentNullException.ThrowIfNull(timeline);
            step = Math.Max(1, step);
            if (endFrame < startFrame) (startFrame, endFrame) = (endFrame, startFrame);
            var originalTime = timeline.CurrentTime;
            var nodes = scene.Traverse().ToList();

            // 1. Sample: pose before and after the solver at every frame.
            var samples = new List<(double Time, Dictionary<Node, (Vector3 P, Quaternion R, Vector3 S)> Pose)>();
            var affected = new HashSet<Node>();
            for (var frame = startFrame; frame <= endFrame; frame += step)
            {
                var time = frame / timeline.FrameRate;
                timeline.SetTime(time);
                timeline.Apply();
                var before = nodes.ToDictionary(n => n, n => (n.LocalPosition, n.LocalRotation, n.LocalScale));
                ConstraintSolver.Apply(scene);
                var pose = new Dictionary<Node, (Vector3, Quaternion, Vector3)>();
                foreach (var n in nodes)
                {
                    var after = (n.LocalPosition, n.LocalRotation, n.LocalScale);
                    pose[n] = after;
                    var b = before[n];
                    if (Vector3.DistanceSquared(b.LocalPosition, after.LocalPosition) > 1e-12f
                        || MathF.Abs(Quaternion.Dot(b.LocalRotation, after.LocalRotation)) < 0.9999999f
                        || Vector3.DistanceSquared(b.LocalScale, after.LocalScale) > 1e-12f)
                        affected.Add(n);
                }
                samples.Add((time, pose));
            }

            // 2. Write keys (after sampling, so new keys don't change what the solver saw).
            foreach (var node in affected)
            {
                var track = timeline.GetOrCreateTrack(node);
                foreach (var (time, pose) in samples)
                {
                    var (p, r, s) = pose[node];
                    track.AddKeyframe(time, p, r, s, InterpolationMode.Linear);
                }
            }

            if (removeConstraints)
                foreach (var node in nodes) node.Constraints.Clear();

            timeline.SetTime(originalTime);
            timeline.Apply();
            if (!removeConstraints) ConstraintSolver.Apply(scene);
            return new Result(affected.ToList(), samples.Count);
        }
    }
}
