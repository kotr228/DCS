using JolieCat3D.Core.Scene;

namespace JolieCat3D.Service.Animation
{
    /// <summary>
    /// One named animation clip (Blender's "Action"): a set of per-node tracks. A scene can
    /// have several (Idle, Run, Jump, ...); the timeline plays and edits the active one, and
    /// glTF export writes each as its own animation.
    /// </summary>
    public sealed class AnimationAction
    {
        public AnimationAction(string name) => Name = name;

        public string Name { get; set; }

        internal Dictionary<Node, AnimationTrack> TrackMap { get; } = new();

        public IReadOnlyCollection<AnimationTrack> Tracks => TrackMap.Values;

        public bool TryGetTrack(Node node, out AnimationTrack? track) => TrackMap.TryGetValue(node, out track);

        public void SetTrack(AnimationTrack track)
        {
            ArgumentNullException.ThrowIfNull(track);
            if (track.IsEmpty) TrackMap.Remove(track.Target);
            else TrackMap[track.Target] = track;
        }

        public void RemoveTrack(Node node) => TrackMap.Remove(node);

        /// <summary>Deep copy under a new name.</summary>
        public AnimationAction Clone(string name)
        {
            var copy = new AnimationAction(name);
            foreach (var (node, track) in TrackMap) copy.TrackMap[node] = track.Clone();
            return copy;
        }
    }
}
