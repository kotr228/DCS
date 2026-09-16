namespace JolieCat3D.Engine.Gizmos
{
    /// <summary>Which axes <see cref="TransformGizmo"/> (and <c>Editing.ComponentGizmo</c>)
    /// currently shows/drags along - one at a time, the same single-mode-toolbar shape
    /// <see cref="GizmoMode"/> already uses for Translate/Rotate/Scale.</summary>
    public enum TransformSpace
    {
        /// <summary>Gizmo axes always align with the World X/Y/Z axes, regardless of the
        /// selected object's own rotation.</summary>
        Global,

        /// <summary>Gizmo axes rotate to match the selected object's own current world
        /// rotation (<c>Scene.Node.GetWorldRotation</c>) - dragging the "X" handle moves/
        /// rotates the object along/around its OWN local X axis, tilted however the object
        /// itself is currently oriented, not the world's.</summary>
        Local,
    }
}
