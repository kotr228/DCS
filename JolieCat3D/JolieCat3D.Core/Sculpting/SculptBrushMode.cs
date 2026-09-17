namespace JolieCat3D.Core.Sculpting
{
    /// <summary>Which of <see cref="SculptBrush"/>'s own 3 algorithms Sculpt Mode's
    /// brush currently applies - <c>Engine.Editing.SculptSession</c>'s own mode
    /// selector.</summary>
    public enum SculptBrushMode
    {
        Draw,
        Smooth,
        Grab,
    }
}
