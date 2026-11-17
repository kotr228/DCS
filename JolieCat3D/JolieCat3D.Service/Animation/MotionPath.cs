using System.Numerics;
using JolieCat3D.Core.Constraints;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.Service.Animation
{
    /// <summary>
    /// Motion Paths (Blender): the world-space position of a node at every frame of the
    /// active action, plus which frames carry its keyframes. The scene is evaluated frame
    /// by frame (parents, constraints included) and every node's transform is restored
    /// afterwards, so un-keyed edits are never lost.
    /// </summary>
    public static class MotionPath
    {
        public sealed record Result(IReadOnlyList<Vector3> Positions, int StartFrame, IReadOnlyList<int> KeyFrames);

        public static Result Compute(Scene3D scene, AnimationTimeline timeline, Node node, int startFrame, int endFrame)
        {
            ArgumentNullException.ThrowIfNull(scene);
            ArgumentNullException.ThrowIfNull(timeline);
            ArgumentNullException.ThrowIfNull(node);
            if (endFrame < startFrame) (startFrame, endFrame) = (endFrame, startFrame);

            var nodes = scene.Traverse().ToList();
            var saved = nodes.Select(n => (n, n.LocalPosition, n.LocalRotation, n.LocalScale)).ToList();
            var originalTime = timeline.CurrentTime;
            var positions = new List<Vector3>(endFrame - startFrame + 1);
            try
            {
                for (var frame = startFrame; frame <= endFrame; frame++)
                {
                    timeline.SetTime(frame / timeline.FrameRate);
                    timeline.Apply();
                    try { ConstraintSolver.Apply(scene); } catch (Exception) { }
                    positions.Add(node.GetWorldPosition());
                }
            }
            finally
            {
                // Animated properties (shape keys, textures) back to the current frame, then
                // transforms exactly as they were (keeps un-keyed edits).
                timeline.SetTime(originalTime);
                timeline.Apply();
                foreach (var (n, p, r, s) in saved) { n.LocalPosition = p; n.LocalRotation = r; n.LocalScale = s; }
            }

            var keyFrames = new List<int>();
            if (timeline.TryGetTrack(node, out var track) && track is not null)
                foreach (var key in track.Keyframes)
                {
                    var frame = (int)Math.Round(key.Time * timeline.FrameRate);
                    if (frame >= startFrame && frame <= endFrame) keyFrames.Add(frame);
                }
            return new Result(positions, startFrame, keyFrames.Distinct().ToList());
        }
    }
}
