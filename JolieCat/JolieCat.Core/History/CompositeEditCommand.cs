using System.Collections.Generic;
using System.Linq;

namespace JolieCat.Core.History
{
    /// <summary>Several edits recorded as one undo step - e.g. moving every layer of a
    /// group at once. Undone in reverse order.</summary>
    public sealed class CompositeEditCommand : IEditCommand, ISizedEditCommand
    {
        private readonly List<IEditCommand> _commands;

        public CompositeEditCommand(IEnumerable<IEditCommand> commands)
        {
            _commands = commands.Where(c => c is not LayerPixelsCommand { IsEmpty: true }).ToList();
        }

        public bool IsEmpty => _commands.Count == 0;

        public long ByteSize => _commands.Sum(c => c is ISizedEditCommand sized ? sized.ByteSize : 0);

        public void Undo()
        {
            for (var i = _commands.Count - 1; i >= 0; i--) _commands[i].Undo();
        }

        public void Redo()
        {
            foreach (var command in _commands) command.Redo();
        }
    }
}
