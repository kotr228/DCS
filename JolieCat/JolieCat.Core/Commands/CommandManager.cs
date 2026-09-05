using System;
using System.Collections.Generic;
using JolieCat.Shared.Commands;

namespace JolieCat.Core.Commands
{
    /// <summary>
    /// Maintains the Undo/Redo stacks for a single document. Executing a new command
    /// always clears the redo stack, matching standard editor behavior.
    /// </summary>
    public sealed class CommandManager
    {
        private readonly Stack<IUndoableCommand> _undoStack = new();
        private readonly Stack<IUndoableCommand> _redoStack = new();

        public event EventHandler? StackChanged;

        public bool CanUndo => _undoStack.Count > 0;

        public bool CanRedo => _redoStack.Count > 0;

        /// <summary>Runs <paramref name="command"/> and pushes it onto the undo stack.</summary>
        public void Execute(IUndoableCommand command)
        {
            ArgumentNullException.ThrowIfNull(command);

            command.Execute();
            _undoStack.Push(command);
            _redoStack.Clear();
            OnStackChanged();
        }

        public void Undo()
        {
            if (!CanUndo) return;

            var command = _undoStack.Pop();
            command.Undo();
            _redoStack.Push(command);
            OnStackChanged();
        }

        public void Redo()
        {
            if (!CanRedo) return;

            var command = _redoStack.Pop();
            command.Execute();
            _undoStack.Push(command);
            OnStackChanged();
        }

        /// <summary>Drops all history without undoing anything (e.g. after a project load).</summary>
        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
            OnStackChanged();
        }

        private void OnStackChanged() => StackChanged?.Invoke(this, EventArgs.Empty);
    }
}
