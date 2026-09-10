namespace JolieCat3D.Core.Modifiers
{
    /// <summary>Which local axis <see cref="MirrorModifier"/> reflects across (the plane
    /// through the mesh's own local origin perpendicular to this axis) - one at a time,
    /// matching every other single-mode-at-once enum in this project
    /// (<c>Engine.Gizmos.GizmoMode</c>, <c>Engine.Editing.ComponentType</c>).</summary>
    public enum MirrorAxis
    {
        X,
        Y,
        Z,
    }
}
