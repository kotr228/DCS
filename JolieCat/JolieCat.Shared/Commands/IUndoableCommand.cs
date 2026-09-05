namespace JolieCat.Shared.Commands
{
    /// <summary>
    /// A reversible unit of work executed against a document. Implementations back the
    /// Undo/Redo stack maintained by <c>JolieCat.Core.Commands.CommandManager</c>.
    /// </summary>
    /// <remarks>
    /// Deliberately not named <c>ICommand</c> to avoid colliding with
    /// <see cref="System.Windows.Input.ICommand"/>, which the WPF client also depends on.
    /// </remarks>
    public interface IUndoableCommand
    {
        /// <summary>
        /// Short, human-readable description of the action (e.g. "Move Layer", "Add Layer").
        /// Suitable for display in an Edit menu ("Undo Move Layer").
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Applies the change.
        /// </summary>
        void Execute();

        /// <summary>
        /// Reverts the change previously applied by <see cref="Execute"/>.
        /// </summary>
        void Undo();
    }
}
