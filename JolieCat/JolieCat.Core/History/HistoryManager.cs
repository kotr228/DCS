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
        /// <summary>Cap on undo depth. Pixel edits only keep their changed tiles (see
        /// <see cref="LayerPixelsCommand"/>), so this can be generous; the memory budget
        /// below is what actually protects large documents.</summary>
        public const int MaxDepth = 200;

        /// <summary>Upper bound on memory held by sized history entries. The oldest
        /// entries are dropped first once this is exceeded.</summary>
        public const long MaxBytes = 1L * 1024 * 1024 * 1024;

        private long _undoBytes;

        private readonly List<IEditCommand> _undoStack = new();
        private readonly List<IEditCommand> _redoStack = new();
        private readonly List<string> _undoNames = new();
        private readonly List<string> _redoNames = new();

        /// <summary>Names an entry pushed without an explicit name - the UI sets this
        /// to e.g. the active tool's name. Null falls back to a generic name per kind.</summary>
        public Func<IEditCommand, string?>? DefaultName { get; set; }

        /// <summary>Names of the undoable steps, oldest first.</summary>
        public IReadOnlyList<string> UndoNames => _undoNames;

        /// <summary>Names of the redoable steps, the next redo first.</summary>
        public IReadOnlyList<string> RedoNames
        {
            get
            {
                var names = new List<string>(_redoNames);
                names.Reverse();
                return names;
            }
        }

        public bool CanUndo => _undoStack.Count > 0;

        public bool CanRedo => _redoStack.Count > 0;

        /// <summary>Raised after every push, undo, redo, or clear - the UI's one hook to
        /// refresh CanUndo/CanRedo-bound commands, rebuild whatever list shows the layer
        /// stack, and repaint the canvas.</summary>
        public event EventHandler? Changed;

        /// <summary>Records a pixel-level edit (a paint stroke, a fill, a text commit) -
        /// these never replace Layer objects, so they coexist safely with each other
        /// across pushes.</summary>
        public void Push(IEditCommand command, string? name = null)
        {
            ArgumentNullException.ThrowIfNull(command);

            // An edit that changed no pixels (a click outside the selection, a fill on
            // an already-filled area) isn't worth an undo step.
            if (command is LayerPixelsCommand { IsEmpty: true }) return;

            _undoStack.Add(command);
            _undoNames.Add(name ?? DefaultName?.Invoke(command) ?? GenericName(command));
            _undoBytes += SizeOf(command);

            while (_undoStack.Count > 1 && (_undoStack.Count > MaxDepth || _undoBytes > MaxBytes))
            {
                _undoBytes -= SizeOf(_undoStack[0]);
                _undoStack.RemoveAt(0);
                _undoNames.RemoveAt(0);
                DroppedCount++;
            }

            _redoStack.Clear();
            _redoNames.Clear();
            RaiseChanged();
        }

        public int UndoCount => _undoStack.Count;

        // ----- Saved state -----
        // The newest undo entry at the moment the document was saved (null = the
        // empty history). The document is unsaved whenever the newest entry differs:
        // after new edits, but also after undoing past the save point.
        private IEditCommand? _savedTop;
        private bool _modifiedOutsideHistory;

        /// <summary>True if the document differs from its last saved (or freshly
        /// created/opened) state.</summary>
        public bool IsDirty => _modifiedOutsideHistory || !ReferenceEquals(Top, _savedTop);

        private IEditCommand? Top => _undoStack.Count > 0 ? _undoStack[^1] : null;

        /// <summary>Call after a successful save: the current state is the saved one.</summary>
        public void MarkSaved()
        {
            _savedTop = Top;
            _modifiedOutsideHistory = false;
            DirtyChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>For changes that aren't undo steps (layer visibility, opacity...):
        /// the document counts as unsaved until the next save.</summary>
        public void MarkModified()
        {
            ChangeCounter++;
            if (_modifiedOutsideHistory) return;
            _modifiedOutsideHistory = true;
            DirtyChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Increases on every change (edit, undo, redo, outside-history
        /// modification) - lets autosave tell whether anything happened since its last run.</summary>
        public long ChangeCounter { get; private set; }

        /// <summary>Raised when <see cref="IsDirty"/> may have changed.</summary>
        public event EventHandler? DirtyChanged;

        /// <summary>True when the latest <see cref="Changed"/> came from Undo/Redo (which
        /// can touch any layer) rather than a new edit (which touches the active one).</summary>
        public bool LastChangeWasUndoOrRedo { get; private set; }

        private static long SizeOf(IEditCommand command) => command is ISizedEditCommand sized ? sized.ByteSize : 0;

        /// <summary>
        /// Records a structural edit (a layer add/remove/reorder/merge/resize). It used
        /// to wipe all earlier history, because structural undo rebuilds Layer objects
        /// and older pixel entries pointed at the old ones. Pixel entries now find their
        /// layer by its stable Id (see <see cref="LayerPixelsCommand"/>), and rebuilt
        /// layers keep that Id, so the full history is kept. Its snapshots count toward
        /// the memory budget like any other sized entry.
        /// </summary>
        public void PushStructural(IEditCommand command, string? name = null) => Push(command, name);

        /// <summary>How many of the oldest steps were dropped to stay within the
        /// depth/memory limits - the History panel's first row is then no longer
        /// the original document.</summary>
        public int DroppedCount { get; private set; }

        public void Undo()
        {
            if (!StepBack()) return;
            RaiseChanged(undoOrRedo: true);
        }

        public void Redo()
        {
            if (!StepForward()) return;
            RaiseChanged(undoOrRedo: true);
        }

        /// <summary>Undoes or redoes until exactly <paramref name="undoCount"/> steps
        /// are applied - what clicking a row in the History panel does. Raises
        /// <see cref="Changed"/> once at the end, not per step.</summary>
        public void JumpTo(int undoCount)
        {
            undoCount = Math.Clamp(undoCount, 0, _undoStack.Count + _redoStack.Count);
            if (undoCount == _undoStack.Count) return;

            while (_undoStack.Count > undoCount && StepBack()) { }
            while (_undoStack.Count < undoCount && StepForward()) { }
            RaiseChanged(undoOrRedo: true);
        }

        private bool StepBack()
        {
            if (_undoStack.Count == 0) return false;

            var command = _undoStack[^1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            _redoNames.Add(_undoNames[^1]);
            _undoNames.RemoveAt(_undoNames.Count - 1);
            _undoBytes -= SizeOf(command);
            command.Undo();
            _redoStack.Add(command);
            return true;
        }

        private bool StepForward()
        {
            if (_redoStack.Count == 0) return false;

            var command = _redoStack[^1];
            _redoStack.RemoveAt(_redoStack.Count - 1);
            _undoNames.Add(_redoNames[^1]);
            _redoNames.RemoveAt(_redoNames.Count - 1);
            command.Redo();
            _undoStack.Add(command);
            _undoBytes += SizeOf(command);
            return true;
        }

        private static string GenericName(IEditCommand command) => command switch
        {
            LayerPixelsCommand => "Edit Pixels",
            SceneStructuralCommand => "Layer Change",
            AdjustmentChangeCommand => "Adjustment Settings",
            SmartObjectTransformCommand => "Transform",
            _ => "Edit",
        };

        /// <summary>Discards all history without undoing anything - used when a whole
        /// new document is loaded, since none of the old commands make sense against it.</summary>
        public void Clear()
        {
            _undoBytes = 0;
            _undoStack.Clear();
            _redoStack.Clear();
            _undoNames.Clear();
            _redoNames.Clear();
            DroppedCount = 0;
            _savedTop = null;
            _modifiedOutsideHistory = false;
            RaiseChanged(undoOrRedo: true);
        }

        private void RaiseChanged(bool undoOrRedo = false)
        {
            LastChangeWasUndoOrRedo = undoOrRedo;
            ChangeCounter++;
            Changed?.Invoke(this, EventArgs.Empty);
            DirtyChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
