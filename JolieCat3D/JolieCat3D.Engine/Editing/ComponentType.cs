namespace JolieCat3D.Engine.Editing
{
    /// <summary>Which kind of mesh component Edit Mode is currently selecting/manipulating
    /// - one at a time, the same single-mode-toolbar shape <c>Gizmos.GizmoMode</c> uses
    /// for Translate/Rotate/Scale. <see cref="MeshEditSession"/> stores selection as a
    /// single set of vertex indices regardless of which mode is active (see its own
    /// remarks on why) - this only changes how a viewport click is interpreted
    /// (<see cref="ComponentHitTester"/>) and which marker visual highlights as
    /// "selected".</summary>
    public enum ComponentType
    {
        Vertex,
        Edge,
        Face,
    }
}
