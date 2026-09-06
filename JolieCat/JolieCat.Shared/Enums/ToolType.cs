namespace JolieCat.Shared.Enums
{
    /// <summary>
    /// Every tool the editor offers, grouped (via comment headers, and via
    /// <c>JolieCat.Core.Tools.ToolCatalog</c>'s <see cref="ToolCategory"/> metadata) into
    /// the same sections a Photoshop-style toolbox uses.
    /// </summary>
    public enum ToolType
    {
        // Selection
        RectangularMarquee,
        EllipticalMarquee,
        Lasso,
        PolygonalLasso,
        MagneticLasso,
        QuickSelection,
        MagicWand,

        // Navigation
        Hand,
        Zoom,
        CanvasRotate,

        // Painting & Editing
        Brush,
        Pencil,
        Eraser,
        PaintBucket,
        Gradient,
        Eyedropper,

        // Retouching
        CloneStamp,
        HealingBrush,
        Blur,
        Sharpen,
        Sponge,
        Dodge,
        Burn,

        // Vector & Text
        Pen,
        PathSelection,
        DirectSelection,
        Shape,
        TextHorizontal,
        TextVertical,

        // Transform
        Crop,
        FreeTransform,
        Warp
    }
}
