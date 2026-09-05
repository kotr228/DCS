namespace JolieCat.Core.History
{
    /// <summary>
    /// One undoable/redoable edit. Commands in this codebase are constructed *after* the
    /// edit they describe already happened (as a normal, direct side effect of painting,
    /// filling, or a layer operation) - they capture enough before/after state to reverse
    /// or replay it, rather than routing every mutation through an <c>Execute</c> step of
    /// their own. That fits how <see cref="Documents.Layer"/> is actually painted on
    /// (direct <c>SKCanvas</c> calls on every mouse-move sample) far better than replaying
    /// an exact sequence of draw calls would.
    /// </summary>
    public interface IEditCommand
    {
        void Undo();
        void Redo();
    }
}
