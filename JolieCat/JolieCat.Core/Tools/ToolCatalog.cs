using System.Collections.Generic;
using JolieCat.Shared.Enums;

namespace JolieCat.Core.Tools
{
    /// <summary>
    /// The fixed catalog of every tool the editor offers, in the order the Tools panel
    /// presents them. A single shared, immutable list - the UI binds to it rather than
    /// re-declaring the toolbox.
    /// </summary>
    public static class ToolCatalog
    {
        public static IReadOnlyList<ToolDefinition> All { get; } = new List<ToolDefinition>
        {
            // Selection
            new(ToolType.RectangularMarquee, ToolCategory.Selection, "Rectangular Marquee", "RM", "M"),
            new(ToolType.EllipticalMarquee, ToolCategory.Selection, "Elliptical Marquee", "EM", "M"),
            new(ToolType.Lasso, ToolCategory.Selection, "Lasso", "LS", "L"),
            new(ToolType.PolygonalLasso, ToolCategory.Selection, "Polygonal Lasso", "PL", "L"),
            new(ToolType.MagneticLasso, ToolCategory.Selection, "Magnetic Lasso", "ML", "L"),
            new(ToolType.QuickSelection, ToolCategory.Selection, "Quick Selection", "QS", "W"),
            new(ToolType.MagicWand, ToolCategory.Selection, "Magic Wand", "MW", "W"),

            // Navigation
            new(ToolType.Hand, ToolCategory.Navigation, "Hand (Pan)", "HD", "H"),
            new(ToolType.Zoom, ToolCategory.Navigation, "Zoom", "ZM", "Z"),
            new(ToolType.CanvasRotate, ToolCategory.Navigation, "Canvas Rotate", "CR", "R"),

            // Painting & Editing
            new(ToolType.Brush, ToolCategory.Painting, "Brush", "BR", "B"),
            new(ToolType.Pencil, ToolCategory.Painting, "Pencil", "PN", "N"),
            new(ToolType.Eraser, ToolCategory.Painting, "Eraser", "ER", "E"),
            new(ToolType.PaintBucket, ToolCategory.Painting, "Paint Bucket", "PB", "G"),
            new(ToolType.Gradient, ToolCategory.Painting, "Gradient", "GR", "G"),

            // Retouching
            new(ToolType.CloneStamp, ToolCategory.Retouching, "Clone Stamp", "CS", "S"),
            new(ToolType.HealingBrush, ToolCategory.Retouching, "Healing Brush", "HB", "J"),
            new(ToolType.Blur, ToolCategory.Retouching, "Blur", "BL", "R"),
            new(ToolType.Sharpen, ToolCategory.Retouching, "Sharpen", "SH", "R"),
            new(ToolType.Sponge, ToolCategory.Retouching, "Sponge", "SG", "O"),
            new(ToolType.Dodge, ToolCategory.Retouching, "Dodge", "DG", "O"),
            new(ToolType.Burn, ToolCategory.Retouching, "Burn", "BN", "O"),

            // Vector & Text
            new(ToolType.Pen, ToolCategory.VectorText, "Pen (Path)", "PE", "P"),
            new(ToolType.Shape, ToolCategory.VectorText, "Shape", "SP", "U"),
            new(ToolType.TextHorizontal, ToolCategory.VectorText, "Horizontal Text", "TH", "T"),
            new(ToolType.TextVertical, ToolCategory.VectorText, "Vertical Text", "TV", "T"),
        };
    }
}
