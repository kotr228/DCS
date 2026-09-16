using JolieCat3D.Core.Scene;
using JolieCat3D.Service.Animation;

namespace JolieCat3D.Service.Commands
{
    /// <summary>
    /// Undoes/redoes duplicating one whole <see cref="Node"/> (Object Mode's own
    /// "Duplicate" command, see <see cref="DuplicateNodeCommandFactory"/>) - inserting the
    /// already-built clone subtree into the scene graph (as a new root node, or a new
    /// child of whatever the original's own <see cref="Node.Parent"/> was) and, for every
    /// source/clone node pair that had a keyframe track on <paramref name="timeline"/>,
    /// copying that track's keyframes onto the clone's own track too. Unlike
    /// <c>TransformNodeCommand</c>/<c>VertexTranslateCommand</c> (built AFTER their own
    /// edit already happened live), <see cref="Execute"/> here performs the actual "insert
    /// into the scene"/"copy the animation tracks" mutation itself, since duplicating a
    /// node is a discrete, one-shot action with no live drag-tick to have already applied
    /// it - <see cref="DuplicateNodeCommandFactory.Create"/> only builds the clone (and
    /// pairs it up with the original), it never inserts it anywhere, so this is normally
    /// handed to <see cref="CommandHistory.Execute"/>, not <see cref="CommandHistory.Record"/>.
    /// </summary>
    public sealed class DuplicateNodeCommand : IUndoableCommand
    {
        private readonly Scene3D _scene;
        private readonly Node? _parent;
        private readonly Node _clone;
        private readonly AnimationTimeline? _timeline;
        private readonly IReadOnlyList<(Node Source, Node Clone)> _animatedPairs;
        private readonly Action? _onChanged;

        public string Description { get; }

        /// <summary>The already-built clone this command inserts/removes - exposed so a
        /// caller (<c>JolieCat3D.UI.MainWindow.DuplicateSelectedNode</c>) can select it
        /// immediately after <see cref="CommandHistory.Execute"/> actually inserts it, the
        /// standard "duplicate leaves the COPY selected" convention.</summary>
        public Node Clone => _clone;

        public DuplicateNodeCommand(
            Scene3D scene,
            Node? parent,
            Node clone,
            AnimationTimeline? timeline,
            IReadOnlyList<(Node Source, Node Clone)> animatedPairs,
            string description,
            Action? onChanged = null)
        {
            _scene = scene ?? throw new ArgumentNullException(nameof(scene));
            _parent = parent;
            _clone = clone ?? throw new ArgumentNullException(nameof(clone));
            _timeline = timeline;
            _animatedPairs = animatedPairs ?? Array.Empty<(Node, Node)>();
            Description = description ?? throw new ArgumentNullException(nameof(description));
            _onChanged = onChanged;
        }

        /// <summary>Inserts <see cref="_clone"/> into the scene graph (alongside the
        /// original, under the same parent - or as a new root node, if the original had
        /// none) and copies every source track's keyframes onto the matching clone track -
        /// re-copying fresh from the SOURCE's own (untouched by duplication) track every
        /// time, rather than trying to preserve the clone's own track object across an
        /// Undo/Redo round-trip, so this is exactly as correct the second time (a Redo) as
        /// the first.</summary>
        public void Execute()
        {
            if (_parent is null) _scene.AddRootNode(_clone);
            else _parent.AddChild(_clone);

            if (_timeline is not null)
            {
                foreach (var (source, clone) in _animatedPairs)
                {
                    if (!_timeline.TryGetTrack(source, out var sourceTrack) || sourceTrack is null) continue;

                    var cloneTrack = _timeline.GetOrCreateTrack(clone);
                    foreach (var keyframe in sourceTrack.Keyframes)
                        cloneTrack.AddKeyframe(keyframe.Time, keyframe.Position, keyframe.Rotation, keyframe.Scale, keyframe.Interpolation);
                }
            }

            _onChanged?.Invoke();
        }

        /// <summary>Removes <see cref="_clone"/> (and, if any, its own copied tracks) from
        /// the scene/timeline entirely - <see cref="_clone"/> itself is never discarded
        /// (only detached), so a later <see cref="Execute"/> (a Redo) can re-insert the
        /// exact same instance.</summary>
        public void Undo()
        {
            if (_timeline is not null)
                foreach (var (_, clone) in _animatedPairs)
                    _timeline.RemoveTrack(clone);

            if (_parent is null) _scene.RemoveRootNode(_clone);
            else _parent.RemoveChild(_clone);

            _onChanged?.Invoke();
        }
    }

    /// <summary>
    /// Builds (but does not insert - see <see cref="DuplicateNodeCommand.Execute"/>) a deep
    /// clone of <paramref name="original"/> via <see cref="Node.Clone"/>, and pairs up every
    /// node in the two parallel subtrees (source and clone) that has its own keyframe track
    /// on <paramref name="timeline"/>, so <see cref="DuplicateNodeCommand"/> can copy each
    /// one's keyframes onto the matching clone's own track once actually executed. The
    /// pairing walks both subtrees via <see cref="Node.Traverse"/> in lockstep - safe
    /// because <see cref="Node.Clone"/> clones every child in the exact same order it
    /// appears in <see cref="Node.Children"/>, so the two traversals visit corresponding
    /// nodes at the same position every time.
    /// </summary>
    public static class DuplicateNodeCommandFactory
    {
        public static DuplicateNodeCommand Create(Scene3D scene, Node original, AnimationTimeline? timeline, Action? onChanged = null)
        {
            ArgumentNullException.ThrowIfNull(scene);
            ArgumentNullException.ThrowIfNull(original);

            var clone = original.Clone();
            clone.Name = original.Name + " Copy";

            var pairs = new List<(Node Source, Node Clone)>();
            using (var sourceNodes = original.Traverse().GetEnumerator())
            using (var cloneNodes = clone.Traverse().GetEnumerator())
            {
                while (sourceNodes.MoveNext() && cloneNodes.MoveNext())
                    pairs.Add((sourceNodes.Current, cloneNodes.Current));
            }

            return new DuplicateNodeCommand(scene, original.Parent, clone, timeline, pairs, "Duplicate Object", onChanged);
        }
    }
}
