using JolieCat3D.Core.Scene;
using JolieCat3D.Service.Animation;

namespace JolieCat3D.Service.Commands
{
    /// <summary>
    /// Undoes/redoes deleting one whole <see cref="Node"/> (and its entire subtree) from
    /// the scene graph - Object Mode's own "Delete", <see cref="DuplicateNodeCommand"/>'s
    /// mirror image. Removes <paramref name="target"/> from wherever it currently lives
    /// (a scene root, or a child of some other node) on <see cref="Execute"/>, and puts it
    /// straight back on <see cref="Undo"/> - the node instance itself (and its own Mesh/
    /// Modifiers/children) is never rebuilt or cloned, only detached/reattached, so nothing
    /// about it is lost across an Undo/Redo round-trip.
    ///
    /// Two things a plain <c>Scene3D.RemoveRootNode</c>/<c>Node.RemoveChild</c> call alone
    /// would silently get wrong, both fixed here:
    /// <list type="bullet">
    /// <item><description><see cref="Scene3D.ActiveCamera"/> - <c>RemoveRootNode</c> only
    /// clears it when the REMOVED node itself (not a descendant) is the active camera, and
    /// <c>Node.RemoveChild</c> (a plain <c>Node</c> method, with no reference back to
    /// whichever <see cref="Scene3D"/> it happens to live in) can't touch it at ALL - so
    /// deleting a camera nested a few levels deep as a child (not a scene root) would
    /// otherwise leave <see cref="Scene3D.ActiveCamera"/> pointing at an orphaned,
    /// unreachable node forever (a real "zombie object": still referenced, so never
    /// collected, but no longer part of the scene anything else can see). Checked/cleared
    /// here regardless of root-vs-child or how deep the camera actually sits in
    /// <paramref name="target"/>'s own subtree, and restored on Undo if it was active.</description></item>
    /// <item><description>Every keyframe track (on <paramref name="timeline"/>) belonging
    /// to <paramref name="target"/> OR ANY of its descendants - left behind, a deleted
    /// node's own track would otherwise keep the WHOLE node graph it references reachable
    /// forever through <c>AnimationTimeline</c>'s own dictionary, the same "supposedly
    /// deleted but actually still rooted somewhere" leak <see cref="Scene3D.ActiveCamera"/>
    /// risks above, just through a different reference. Captured (as plain keyframe
    /// values, not the live <c>AnimationTrack</c> instance) before removal so Undo can
    /// restore each one exactly, the same "keyframes copied by value" approach
    /// <see cref="DuplicateNodeCommand"/> already uses.</description></item>
    /// </list>
    /// </summary>
    public sealed class DeleteNodeCommand : IUndoableCommand
    {
        private readonly Scene3D _scene;
        private readonly Node? _parent;
        private readonly Node _target;
        private readonly AnimationTimeline? _timeline;
        private readonly IReadOnlyList<(Node Node, IReadOnlyList<Keyframe> Keyframes)> _capturedTracks;
        private readonly Node? _activeCameraInSubtree;
        private readonly Action? _onChanged;

        public string Description { get; }

        /// <summary>The node this command removes/reinserts - exposed so a caller
        /// (<c>JolieCat3D.UI.MainWindow.DeleteSelectedNode</c>) can clean up its own
        /// UI-only bookkeeping for the whole deleted subtree (e.g. <c>_textureSources</c>'
        /// tracked JolieCat-workspace-watch entries) via <see cref="Node.Traverse"/>,
        /// without this command needing to know that UI-layer concern exists at all.</summary>
        public Node Target => _target;

        public DeleteNodeCommand(
            Scene3D scene,
            Node? parent,
            Node target,
            AnimationTimeline? timeline,
            IReadOnlyList<(Node Node, IReadOnlyList<Keyframe> Keyframes)> capturedTracks,
            Node? activeCameraInSubtree,
            string description,
            Action? onChanged = null)
        {
            _scene = scene ?? throw new ArgumentNullException(nameof(scene));
            _parent = parent;
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _timeline = timeline;
            _capturedTracks = capturedTracks ?? Array.Empty<(Node, IReadOnlyList<Keyframe>)>();
            _activeCameraInSubtree = activeCameraInSubtree;
            Description = description ?? throw new ArgumentNullException(nameof(description));
            _onChanged = onChanged;
        }

        public void Execute()
        {
            if (_timeline is not null)
                foreach (var (node, _) in _capturedTracks)
                    _timeline.RemoveTrack(node);

            if (_parent is null) _scene.RemoveRootNode(_target);
            else _parent.RemoveChild(_target);

            // RemoveRootNode already does this for a root-level removal, but re-asserting
            // it here (a no-op in that case - clearing an already-null/already-cleared
            // field twice is harmless) is what actually covers the case it can't: a camera
            // nested as a CHILD, or several levels deep in the deleted subtree.
            if (_activeCameraInSubtree is not null && _scene.ActiveCamera == _activeCameraInSubtree)
                _scene.ActiveCamera = null;

            _onChanged?.Invoke();
        }

        public void Undo()
        {
            if (_parent is null) _scene.AddRootNode(_target);
            else _parent.AddChild(_target);

            if (_timeline is not null)
            {
                foreach (var (node, keyframes) in _capturedTracks)
                {
                    var track = _timeline.GetOrCreateTrack(node);
                    foreach (var keyframe in keyframes)
                        track.AddKeyframe(keyframe.Time, keyframe.Position, keyframe.Rotation, keyframe.Scale, keyframe.Interpolation);
                }
            }

            if (_activeCameraInSubtree is not null) _scene.ActiveCamera = _activeCameraInSubtree;

            _onChanged?.Invoke();
        }
    }

    /// <summary>
    /// Captures everything <see cref="DeleteNodeCommand"/> needs BEFORE actually removing
    /// <paramref name="target"/> - its current parent (null for a scene root), every
    /// keyframe on every track belonging to it or a descendant, and whether
    /// <paramref name="scene"/>'s own <see cref="Scene3D.ActiveCamera"/> is <paramref name="target"/>
    /// or lives somewhere inside its subtree - all read via <see cref="Node.Traverse"/>
    /// while the subtree is still fully attached and every reference still resolves.
    /// </summary>
    public static class DeleteNodeCommandFactory
    {
        public static DeleteNodeCommand Create(Scene3D scene, Node target, AnimationTimeline? timeline, Action? onChanged = null)
        {
            ArgumentNullException.ThrowIfNull(scene);
            ArgumentNullException.ThrowIfNull(target);

            var capturedTracks = new List<(Node, IReadOnlyList<Keyframe>)>();
            Node? activeCameraInSubtree = null;

            foreach (var node in target.Traverse())
            {
                if (timeline is not null && timeline.TryGetTrack(node, out var track) && track is not null)
                    capturedTracks.Add((node, track.Keyframes.ToList()));

                if (node == scene.ActiveCamera) activeCameraInSubtree = node;
            }

            return new DeleteNodeCommand(scene, target.Parent, target, timeline, capturedTracks, activeCameraInSubtree, "Delete Object", onChanged);
        }
    }
}
