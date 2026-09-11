namespace JolieCat3D.Core.Camera
{
    /// <summary>
    /// The 6 axis-aligned "look from this side" view alignments every modeling tool's
    /// own numpad/menu view shortcuts offer - <see cref="ViewPresetMath.GetOrientation"/>
    /// is the actual math (a world-space look direction + up vector) each one resolves
    /// to; this enum is purely which one a caller (a toolbar button, in
    /// <c>JolieCat3D.UI</c>) asked for.
    /// </summary>
    public enum ViewPreset
    {
        Top,
        Bottom,
        Front,
        Back,
        Left,
        Right,
    }
}
