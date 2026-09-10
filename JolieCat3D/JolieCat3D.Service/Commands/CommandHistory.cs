namespace JolieCat3D.Service.Commands
{
    /// <summary>
    /// The Undo/Redo stack manager: one linear history of already-applied
    /// <see cref="IUndoableCommand"/>s, with <see cref="Undo"/>/<see cref="Redo"/>
    /// walking it backward/forward - the single object <c>JolieCat3D.UI</c> wires its
    /// Ctrl+Z/Ctrl+Y (via WPF's own <c>ApplicationCommands.Undo</c>/<c>Redo</c>, whose
    /// default gestures already ARE Ctrl+Z/Ctrl+Y) key bindings to, and the one place
    /// every mesh-edit/transform command actually gets recorded.
    ///
    /// Bounded by <see cref="Capacity"/> (a real, if unglamorous, bug of any UNBOUNDED
    /// undo stack: <c>MeshEditCommand</c> holds two full <c>Core.Geometry.Mesh</c>
    /// snapshots, proportional to that mesh's own vertex/face/polygon count - an
    /// editing session with no cap at all would grow memory without limit for as long as
    /// it kept editing a large mesh). The OLDEST entry is discarded once
    /// <see cref="Capacity"/> is exceeded - discarded, not merely unreachable: nothing in
    /// this class keeps a reference to it afterward, so its own (potentially large) mesh
    /// snapshots become immediately garbage-collectible, the same way any bounded cache
    /// eviction should behave.
    /// </summary>
    public sealed class CommandHistory
    {
        private readonly List<IUndoableCommand> _undoHistory = new();
        private readonly Stack<IUndoableCommand> _redoStack = new();

        /// <summary>The maximum number of commands <see cref="_undoHistory"/> ever
        /// holds at once - see this class's own remarks on why this exists at all.
        /// 50 is generous for an ordinary editing session (Undo commonly only ever
        /// walks back a handful of steps in practice) while still bounding a large
        /// mesh's own per-command snapshot cost to a fixed, known multiple rather than
        /// an open-ended one.</summary>
        public int Capacity { get; }

        /// <summary>Raised after ANY call that changes what Undo/Redo would do next -
        /// <see cref="Execute"/>, <see cref="Record"/>, <see cref="Undo"/>,
        /// <see cref="Redo"/>, or <see cref="Clear"/> - <c>JolieCat3D.UI</c>'s cue to
        /// re-evaluate <see cref="CanUndo"/>/<see cref="CanRedo"/> (e.g. for a bound
        /// <c>ApplicationCommands.Undo</c>/<c>Redo</c> menu item's enabled state via
        /// <c>CommandManager.InvalidateRequerySuggested</c>), not to re-render the scene
        /// itself - each command's own callback (see e.g. <c>TransformNodeCommand</c>'s
        /// own <c>onChanged</c>) already does that as part of <see cref="IUndoableCommand.Execute"/>/
        /// <see cref="IUndoableCommand.Undo"/> running.</summary>
        public event EventHandler? Changed;

        public bool CanUndo => _undoHistory.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        public CommandHistory(int capacity = 50)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity), "CommandHistory needs a capacity of at least 1.");
            Capacity = capacity;
        }

        /// <summary>Runs <paramref name="command"/>'s own <see cref="IUndoableCommand.Execute"/>
        /// for the first time, then records it - the entry point for a command that
        /// hasn't already been applied elsewhere (unlike <see cref="Record"/>, which
        /// assumes it has).</summary>
        public void Execute(IUndoableCommand command)
        {
            ArgumentNullException.ThrowIfNull(command);
            command.Execute();
            Push(command);
        }

        /// <summary>Records <paramref name="command"/> WITHOUT calling
        /// <see cref="IUndoableCommand.Execute"/> - for a command whose "after" state is
        /// already live (a <c>TransformNodeCommand</c> built from a gizmo drag that
        /// already moved the node tick by tick, or a <c>MeshEditCommand</c> built from an
        /// Extrude/Subdivide button that already mutated the mesh) - calling
        /// <see cref="IUndoableCommand.Execute"/> again here would at best be a
        /// redundant no-op reapplication, and at worst (a command whose <see cref="IUndoableCommand.Execute"/>
        /// isn't perfectly idempotent) a real double-application bug; this entry point
        /// simply never risks it.</summary>
        public void Record(IUndoableCommand command)
        {
            ArgumentNullException.ThrowIfNull(command);
            Push(command);
        }

        private void Push(IUndoableCommand command)
        {
            _undoHistory.Add(command);
            if (_undoHistory.Count > Capacity) _undoHistory.RemoveAt(0);

            // A fresh edit invalidates whatever "future" Redo used to be able to reach -
            // the standard Undo/Redo convention every editor follows (Photoshop, an IDE,
            // a word processor): redoing past the point where a NEW, different edit was
            // made would silently discard that new edit, which is far more surprising
            // than simply losing a stale Redo branch nobody asked to keep.
            _redoStack.Clear();

            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Undoes the most recently applied command, moving it onto the Redo
        /// side - false (a no-op) if <see cref="CanUndo"/> is already false.</summary>
        public bool Undo()
        {
            if (_undoHistory.Count == 0) return false;

            var command = _undoHistory[^1];
            _undoHistory.RemoveAt(_undoHistory.Count - 1);

            command.Undo();
            _redoStack.Push(command);

            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        /// <summary>Re-applies the most recently undone command, moving it back onto the
        /// Undo side - false (a no-op) if <see cref="CanRedo"/> is already false.</summary>
        public bool Redo()
        {
            if (_redoStack.Count == 0) return false;

            var command = _redoStack.Pop();
            command.Execute();

            _undoHistory.Add(command);
            if (_undoHistory.Count > Capacity) _undoHistory.RemoveAt(0);

            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        /// <summary>Discards the ENTIRE history, both directions - what
        /// <c>JolieCat3D.UI</c> calls when a brand new/different scene is loaded (New,
        /// Open, Import replacing the whole scene): every recorded command references
        /// <c>Core.Scene.Node</c>/<c>Core.Geometry.Mesh</c> instances from whichever
        /// scene was active when it was recorded, so undoing one against an entirely
        /// different, freshly loaded scene would be meaningless (its Node might not even
        /// be part of the displayed scene graph anymore) - clearing avoids that
        /// confusion outright rather than leaving a history that quietly no longer makes
        /// sense.</summary>
        public void Clear()
        {
            _undoHistory.Clear();
            _redoStack.Clear();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
