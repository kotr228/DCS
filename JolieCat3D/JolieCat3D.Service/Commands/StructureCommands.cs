using System.Numerics;
using JolieCat3D.Core.Scene;
using JolieCat3D.Service.Animation;

namespace JolieCat3D.Service.Commands
{
    /// <summary>Adds a node (as a root, or under <c>parent</c>); undo removes it again.</summary>
    public sealed class AddNodeCommand : IUndoableCommand
    {
        private readonly Scene3D _scene;
        private readonly Node? _parent;
        private readonly Node _node;
        private readonly Action? _onChanged;

        public string Description { get; }

        public Node Node => _node;

        public AddNodeCommand(Scene3D scene, Node? parent, Node node, string description, Action? onChanged = null)
        {
            _scene = scene ?? throw new ArgumentNullException(nameof(scene));
            _parent = parent;
            _node = node ?? throw new ArgumentNullException(nameof(node));
            Description = description ?? throw new ArgumentNullException(nameof(description));
            _onChanged = onChanged;
        }

        public void Execute()
        {
            if (_parent is null) _scene.AddRootNode(_node);
            else _parent.AddChild(_node);
            _onChanged?.Invoke();
        }

        public void Undo()
        {
            if (_parent is null) _scene.RemoveRootNode(_node);
            else _parent.RemoveChild(_node);

            if (_scene.ActiveCamera is { } camera && _node.Traverse().Contains(camera))
                _scene.ActiveCamera = null;

            _onChanged?.Invoke();
        }
    }

    /// <summary>Several commands undone/redone as one step.</summary>
    public sealed class CompositeCommand : IUndoableCommand
    {
        private readonly IReadOnlyList<IUndoableCommand> _commands;

        public string Description { get; }

        public CompositeCommand(string description, IEnumerable<IUndoableCommand> commands)
        {
            Description = description ?? throw new ArgumentNullException(nameof(description));
            _commands = commands?.ToList() ?? throw new ArgumentNullException(nameof(commands));
        }

        public void Execute()
        {
            foreach (var command in _commands) command.Execute();
        }

        public void Undo()
        {
            for (var i = _commands.Count - 1; i >= 0; i--) _commands[i].Undo();
        }
    }

    /// <summary>Moves a node under a new parent (null = root). Undo restores the old
    /// parent and the exact old local transform.</summary>
    public sealed class ReparentNodeCommand : IUndoableCommand
    {
        private readonly Scene3D _scene;
        private readonly Node _node;
        private readonly Node? _oldParent;
        private readonly Node? _newParent;
        private readonly (Vector3 P, Quaternion R, Vector3 S) _oldLocal;
        private (Vector3 P, Quaternion R, Vector3 S)? _newLocal;
        private readonly Action? _onChanged;

        public string Description { get; }

        public ReparentNodeCommand(Scene3D scene, Node node, Node? newParent, string description, Action? onChanged = null)
        {
            _scene = scene ?? throw new ArgumentNullException(nameof(scene));
            _node = node ?? throw new ArgumentNullException(nameof(node));
            _oldParent = node.Parent;
            _newParent = newParent;
            _oldLocal = (node.LocalPosition, node.LocalRotation, node.LocalScale);
            Description = description ?? throw new ArgumentNullException(nameof(description));
            _onChanged = onChanged;
        }

        /// <summary>False when the scene refused the move (cycle, same parent, ...).</summary>
        public bool TryExecute()
        {
            if (!_scene.Reparent(_node, _newParent)) return false;
            _newLocal = (_node.LocalPosition, _node.LocalRotation, _node.LocalScale);
            _onChanged?.Invoke();
            return true;
        }

        public void Execute()
        {
            _scene.Reparent(_node, _newParent);
            if (_newLocal is { } local) Apply(local);
            _onChanged?.Invoke();
        }

        public void Undo()
        {
            _scene.Reparent(_node, _oldParent);
            Apply(_oldLocal);
            _onChanged?.Invoke();
        }

        private void Apply((Vector3 P, Quaternion R, Vector3 S) local)
        {
            _node.LocalPosition = local.P;
            _node.LocalRotation = local.R;
            _node.LocalScale = local.S;
        }
    }

    /// <summary>Swaps one node's whole animation track (all F-curves, handles included)
    /// between two snapshots - covers inserting, deleting, retiming and curve editing.</summary>
    public sealed class KeyframeTrackCommand : IUndoableCommand
    {
        private readonly Func<AnimationTimeline> _timeline;
        private readonly Node _node;
        private readonly AnimationTrack? _before;
        private readonly AnimationTrack? _after;
        private readonly AnimationAction _action;
        private readonly Action? _onChanged;

        public string Description { get; }

        public KeyframeTrackCommand(Func<AnimationTimeline> timeline, Node node, AnimationTrack? before, AnimationTrack? after,
            string description, Action? onChanged = null)
        {
            _timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));
            _node = node ?? throw new ArgumentNullException(nameof(node));
            _before = before;
            _after = after;
            _action = timeline().ActiveAction; // undo always targets the action that was edited
            Description = description ?? throw new ArgumentNullException(nameof(description));
            _onChanged = onChanged;
        }

        /// <summary>A deep copy of the node's current track, or null when it has none.</summary>
        public static AnimationTrack? Snapshot(AnimationTimeline timeline, Node node) =>
            timeline.TryGetTrack(node, out var track) && track is not null && !track.IsEmpty ? track.Clone() : null;

        public static bool SameContent(AnimationTrack? a, AnimationTrack? b) =>
            a is null ? b is null : a.ContentEquals(b);

        public void Execute() => Apply(_after);

        public void Undo() => Apply(_before);

        private void Apply(AnimationTrack? snapshot)
        {
            if (snapshot is null) _action.RemoveTrack(_node);
            else _action.SetTrack(snapshot.Clone());
            _onChanged?.Invoke();
        }
    }

    /// <summary>Adds or removes an animation action (clip). Adding makes it active; undo
    /// reverses it and restores the previously active action.</summary>
    public sealed class ActionListCommand : IUndoableCommand
    {
        private readonly Func<AnimationTimeline> _timeline;
        private readonly AnimationAction _action;
        private readonly bool _isAdd;
        private readonly int _index;
        private readonly AnimationAction _previousActive;
        private readonly Action? _onChanged;

        public ActionListCommand(Func<AnimationTimeline> timeline, AnimationAction action, bool isAdd, Action? onChanged = null)
        {
            _timeline = timeline;
            _action = action;
            _isAdd = isAdd;
            var t = timeline();
            _index = isAdd ? t.Actions.Count : Math.Max(0, IndexOf(t, action));
            _previousActive = t.ActiveAction;
            _onChanged = onChanged;
        }

        public string Description => (_isAdd ? "Add Action " : "Delete Action ") + _action.Name;

        public void Execute() { if (_isAdd) Add(); else Remove(); _onChanged?.Invoke(); }

        public void Undo()
        {
            if (_isAdd) Remove(); else Add();
            var t = _timeline();
            if (t.Actions.Contains(_previousActive)) t.ActiveAction = _previousActive;
            _onChanged?.Invoke();
        }

        private void Add()
        {
            var t = _timeline();
            t.InsertAction(_index, _action);
            t.ActiveAction = _action;
        }

        private void Remove() => _timeline().RemoveAction(_action);

        private static int IndexOf(AnimationTimeline t, AnimationAction a)
        {
            for (var i = 0; i < t.Actions.Count; i++) if (ReferenceEquals(t.Actions[i], a)) return i;
            return -1;
        }
    }

    /// <summary>Renames an action.</summary>
    public sealed class RenameActionCommand : IUndoableCommand
    {
        private readonly AnimationAction _action;
        private readonly string _before, _after;
        private readonly Action? _onChanged;

        public RenameActionCommand(AnimationAction action, string before, string after, Action? onChanged = null)
        {
            _action = action; _before = before; _after = after; _onChanged = onChanged;
        }

        public string Description => "Rename Action";
        public void Execute() { _action.Name = _after; _onChanged?.Invoke(); }
        public void Undo() { _action.Name = _before; _onChanged?.Invoke(); }
    }

    /// <summary>Merges another timeline's actions into a timeline (e.g. animations of an
    /// imported model): tracks go into the action with the same name, created if missing.
    /// Undo removes exactly what was added.</summary>
    public sealed class MergeActionsCommand : IUndoableCommand
    {
        private readonly Func<AnimationTimeline> _timeline;
        private readonly List<(string Name, List<AnimationTrack> Tracks)> _incoming;
        private readonly List<AnimationAction> _created = new();
        private readonly List<(AnimationAction Action, AnimationTrack? Previous, AnimationTrack? Added)> _applied = new();
        private readonly Action? _onChanged;

        public MergeActionsCommand(Func<AnimationTimeline> timeline, AnimationTimeline source, Action? onChanged = null)
        {
            _timeline = timeline;
            _incoming = source.Actions.Select(a => (a.Name, a.Tracks.Select(t => t.Clone()).ToList())).ToList();
            _onChanged = onChanged;
        }

        public string Description => "Import Animation";

        public void Execute()
        {
            var t = _timeline();
            _created.Clear();
            _applied.Clear();
            foreach (var (name, tracks) in _incoming)
            {
                if (tracks.Count == 0) continue;
                var action = t.Actions.FirstOrDefault(a => a.Name == name);
                if (action is null)
                {
                    // An untouched default "Action" is reused rather than leaving it empty.
                    if (t.Actions.Count == 1 && !t.Actions[0].Tracks.Any() && _created.Count == 0 && t.Actions[0].Name == "Action")
                    {
                        action = t.Actions[0];
                        _applied.Add((action, null, null)); // marker: rename back on undo
                        action.Name = name;
                    }
                    else
                    {
                        action = new AnimationAction(name);
                        t.InsertAction(t.Actions.Count, action);
                        _created.Add(action);
                    }
                }
                foreach (var track in tracks)
                {
                    action.TryGetTrack(track.Target, out var previous);
                    var added = track.Clone();
                    action.SetTrack(added);
                    _applied.Add((action, previous, added));
                }
            }
            _onChanged?.Invoke();
        }

        public void Undo()
        {
            var t = _timeline();
            for (var i = _applied.Count - 1; i >= 0; i--)
            {
                var (action, previous, added) = _applied[i];
                if (added is null) { action.Name = "Action"; continue; }
                if (previous is null) action.RemoveTrack(added.Target);
                else action.SetTrack(previous);
            }
            foreach (var action in _created) t.RemoveAction(action);
            _onChanged?.Invoke();
        }
    }

    /// <summary>Hides/unhides objects in the viewport (H / Alt+H / Shift+H).</summary>
    public sealed class SetHiddenCommand : IUndoableCommand
    {
        private readonly List<(Node Node, bool Before)> _nodes;
        private readonly bool _hidden;
        private readonly Action? _onChanged;

        public SetHiddenCommand(IEnumerable<Node> nodes, bool hidden, Action? onChanged = null)
        {
            _nodes = nodes.Select(n => (n, n.IsHidden)).ToList();
            _hidden = hidden;
            _onChanged = onChanged;
        }

        public string Description => _hidden ? "Hide" : "Unhide";
        public int Count => _nodes.Count;

        public void Execute()
        {
            foreach (var (node, _) in _nodes) node.IsHidden = _hidden;
            _onChanged?.Invoke();
        }

        public void Undo()
        {
            foreach (var (node, before) in _nodes) node.IsHidden = before;
            _onChanged?.Invoke();
        }
    }
}
