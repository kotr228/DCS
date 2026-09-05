using System;
using System.Collections.Generic;

namespace JolieCat.Core.History
{
    /// <summary>
    /// The undo/redo stack for one document - two bounded lists of <see cref="IEditCommand"/>,
    /// used as stacks (operations at the end). Framework-agnostic: raises <see cref="Changed"/>
    /// after every push/undo/redo/clear so a UI layer can refresh whatever it binds to
    /// CanUndo/CanRedo and repaint, without this class knowing anything about WPF.
    /// </summary>
    public sealed class HistoryManager
    {
        /// <summary>Cap on undo depth. Every entry can hold a full per-layer pixel
        /// snapshot (see <see cref="LayerPixelsCommand"/>) or a whole-scene one (see
        /// <see cref="SceneStructuralCommand"/>), so this bounds memory rather than
        /// letting an unbounded editing session grow the stack forever - a deliberate,
        /// simple trade-off (see this class's remarks on <see cref="PushStructural"/>
        /// for the other one) rather than a smarter diff-based history.</summary>
        private const int MaxDepth = 30;

        private readonly List<IEditCommand> _undoStack = new();
        private readonly List<IEditCommand> _redoStack = new();

        public bool CanUndo => _undoStack.Count > 0;

        public bool CanRedo => _redoStack.Count > 0;

        /// <summary>Raised after every push, undo, redo, or clear - the UI's one hook to
        /// refresh CanUndo/CanRedo-bound commands, rebuild whatever list shows the layer
        /// stack, and repaint the canvas.</summary>
        public event EventHandler? Changed;

        /// <summary>Records a pixel-level edit (a paint stroke, a fill, a text commit) -
        /// these never replace Layer objects, so they coexist safely with each other
        /// across pushes.</summary>
        public void Push(IEditCommand command)
        {
            ArgumentNullException.ThrowIfNull(command);

            _undoStack.Add(command);
            if (_undoStack.Count > MaxDepth)
                _undoStack.RemoveAt(0);

            _redoStack.Clear();
            RaiseChanged();
        }

        /// <summary>
        /// Records a structural edit (a layer add/remove/reorder/merge) - and, unlike
        /// <see cref="Push"/>, discards all existing history first. Structural undo/redo
        /// always reconstructs brand new <see cref="Documents.Layer"/> instances rather
        /// than mutating the ones already in the scene (see <c>Scene.RestoreLayers</c>),
        /// since a deleted layer's bitmap is already disposed by the time an undo could
        /// run. That means an older pixel-level command sitting in history and still
        /// referencing a Layer object a structural undo/redo has since replaced could
        /// otherwise be replayed against an object that no longer belongs to the scene.
        /// Rather than track exactly which older commands a given structural change would
        /// invalidate, this simply starts a fresh history from here: the structural
        /// change itself, and everything after it, stays fully undoable/redoable - only
        /// history from strictly before it is given up. This is a deliberate, documented
        /// simplification (plenty of real editors clear undo on similarly disruptive
        /// operations, e.g. a canvas resize).
        /// </summary>
        public void PushStructural(IEditCommand command)
        {
            _undoStack.Clear();
            _redoStack.Clear();
            Push(command);
        }

        public void Undo()
        {
            if (_undoStack.Count == 0) return;

            var command = _undoStack[^1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            command.Undo();
            _redoStack.Add(command);
            RaiseChanged();
        }

        public void Redo()
        {
            if (_redoStack.Count == 0) return;

            var command = _redoStack[^1];
            _redoStack.RemoveAt(_redoStack.Count - 1);
            command.Redo();
            _undoStack.Add(command);
            RaiseChanged();
        }

        /// <summary>Discards all history without undoing anything - used when a whole
        /// new document is loaded, since none of the old commands make sense against it.</summary>
        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
            RaiseChanged();
        }

        private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }
}
