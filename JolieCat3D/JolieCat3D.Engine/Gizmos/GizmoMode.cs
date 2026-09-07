namespace JolieCat3D.Engine.Gizmos
{
    /// <summary>Which transform tool <see cref="TransformGizmo"/> currently shows for
    /// the selected object - one at a time, matching the conventional single-mode
    /// toolbar every 3D editor uses (Translate/Rotate/Scale as separate, mutually
    /// exclusive tools) rather than one combined widget doing all three at once.</summary>
    public enum GizmoMode
    {
        Translate,
        Rotate,
        Scale,
    }
}
