namespace JolieCat.UI.ViewModels
{
    /// <summary>Something that can take over a Move-tool drag of a layer: the Timeline,
    /// which turns it into the layer's animated position at the current frame.</summary>
    public interface IAnimatedMove
    {
        /// <summary>Starts a drag of <paramref name="layer"/>; false to let the Move tool move its pixels.</summary>
        bool BeginAnimatedMove(Core.Documents.Layer layer);

        /// <summary>The pointer has moved this far (document pixels) since the drag started.</summary>
        void UpdateAnimatedMove(float dx, float dy);
    }
}
