namespace JolieCat3D.Core.Constraints
{
    /// <summary>Which of a constrained node's own LOCAL axes <see cref="TrackToConstraint"/>
    /// points at its <see cref="TrackToConstraint.Target"/> - the task's own "local
    /// Z-axis (or specified forward axis)". <see cref="PlusZ"/> matches
    /// <see cref="Scene.Node.GetWorldForward"/>'s own existing +Z-is-forward convention
    /// (already what a <see cref="Scene.CameraData"/>/directional <see cref="Scene.LightData"/>
    /// both use), so it's the natural default for a camera/light target-tracking rig;
    /// the other five exist for a node whose own authored geometry considers a
    /// different axis "forward".</summary>
    public enum ConstraintAxis
    {
        PlusX,
        MinusX,
        PlusY,
        MinusY,
        PlusZ,
        MinusZ,
    }
}
