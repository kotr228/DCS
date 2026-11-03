namespace JolieCat.Core.Documents
{
    /// <summary>A guide line (View > Guides) at <see cref="Position"/> document pixels.
    /// Vertical guides mark an x position, horizontal ones a y position.</summary>
    public sealed record Guide(bool IsVertical, float Position);
}
