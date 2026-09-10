namespace JolieCat3D.Service.Commands
{
    /// <summary>
    /// One reversible edit to the scene - the Command pattern's own unit, wrapping a
    /// mesh modification (<c>MeshEditCommand</c>, Extrude/Subdivide) or an object
    /// transformation (<c>TransformNodeCommand</c>, Translate/Rotate/Scale) so
    /// <see cref="CommandHistory"/> never needs to know which kind of edit it's holding -
    /// only that it can be <see cref="Execute"/>d and <see cref="Undo"/>ne. A command is
    /// typically constructed AFTER its edit has already happened live (a gizmo drag
    /// already moved the node; an Extrude button already extruded the mesh) - see each
    /// concrete command's own remarks - so <see cref="Execute"/> only ever needs to be
    /// idempotent with that already-applied state, not the first thing that actually
    /// performs the edit.
    /// </summary>
    public interface IUndoableCommand
    {
        /// <summary>A short, human-facing label ("Translate Cube", "Extrude Face") -
        /// not read by <see cref="CommandHistory"/> itself, but available for any future
        /// "Undo Extrude Face" menu item/status text that wants one, the same way
        /// <c>Core.Modifiers.Modifier.Name</c> exists for a Modifiers panel that hasn't
        /// necessarily been built yet either.</summary>
        string Description { get; }

        /// <summary>Applies this command's own "after" state - called once when the
        /// command is first constructed and executed (for a command built via
        /// <c>CommandHistory.Execute</c>, as opposed to one already live-applied and
        /// merely <c>Record</c>ed), and again every time <see cref="CommandHistory.Redo"/>
        /// re-applies it.</summary>
        void Execute();

        /// <summary>Restores this command's own "before" state - <see cref="Execute"/>'s
        /// exact inverse.</summary>
        void Undo();
    }
}
