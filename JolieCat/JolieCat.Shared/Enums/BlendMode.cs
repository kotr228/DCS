namespace JolieCat.Shared.Enums
{
    /// <summary>
    /// Compositing mode used when a layer is blended with the layers beneath it.
    /// </summary>
    public enum BlendMode
    {
        Normal,
        Multiply,
        Screen,
        Overlay,
        Darken,
        Lighten,
        ColorDodge,
        ColorBurn,
        HardLight,
        SoftLight,
        Difference,
        Exclusion,

        // Component modes - appended so the numeric values of the modes above (which
        // saved projects may store) stay unchanged.
        Hue,
        Saturation,
        Color,
        Luminosity
    }
}
