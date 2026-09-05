using System.Collections.Generic;
using JolieCat.Shared.Enums;

namespace JolieCat.Core.Tools
{
    /// <summary>
    /// The fixed catalog of every tool the editor offers, in the order the Tools panel
    /// presents them. A single shared, immutable list - the UI binds to it rather than
    /// re-declaring the toolbox. Each entry's icon is hand-drawn path geometry (see
    /// <see cref="ToolDefinition.IconData"/>) rather than a font glyph or bitmap asset.
    /// </summary>
    public static class ToolCatalog
    {
        public static IReadOnlyList<ToolDefinition> All { get; } = new List<ToolDefinition>
        {
            // Selection
            new(ToolType.RectangularMarquee, ToolCategory.Selection, "Rectangular Marquee",
                "M2,2 H22 V22 H2 Z M5,5 V19 H19 V5 Z", "M"),

            new(ToolType.EllipticalMarquee, ToolCategory.Selection, "Elliptical Marquee",
                "M2,12 A10,10 0 1 1 22,12 A10,10 0 1 1 2,12 Z M5,12 A7,7 0 1 1 19,12 A7,7 0 1 1 5,12 Z", "M"),

            new(ToolType.Lasso, ToolCategory.Selection, "Lasso",
                "M4,14 C4,8 9,4 14,5 C19,6 21,11 18,15 C16,18 10,19 7,17 C5,16 4,15 4,14 Z", "L"),

            new(ToolType.PolygonalLasso, ToolCategory.Selection, "Polygonal Lasso",
                "M4,16 L8,6 L14,4 L20,10 L18,18 L10,20 Z", "L"),

            new(ToolType.MagneticLasso, ToolCategory.Selection, "Magnetic Lasso",
                "M4,16 L8,6 L14,4 L20,10 L18,18 L10,20 Z M7,15 L9,8 L14,7 L17,11 L16,16 L11,17 Z " +
                "M12.5,4 A1.5,1.5 0 1 1 15.5,4 A1.5,1.5 0 1 1 12.5,4 Z", "L"),

            new(ToolType.QuickSelection, ToolCategory.Selection, "Quick Selection",
                "M4,9 A5,5 0 1 1 14,9 A5,5 0 1 1 4,9 Z M10,9 A5,5 0 1 1 20,9 A5,5 0 1 1 10,9 Z " +
                "M7,14 A5,5 0 1 1 17,14 A5,5 0 1 1 7,14 Z", "W"),

            new(ToolType.MagicWand, ToolCategory.Selection, "Magic Wand",
                "M12,2 L14,10 L22,12 L14,14 L12,22 L10,14 L2,12 L10,10 Z", "W"),

            // Navigation
            new(ToolType.Hand, ToolCategory.Navigation, "Hand (Pan)",
                "M12,2 L15,7 L13,7 L13,10 L16,10 L16,8 L21,12 L16,16 L16,14 L13,14 L13,17 L15,17 L12,22 " +
                "L9,17 L11,17 L11,14 L8,14 L8,16 L3,12 L8,8 L8,10 L11,10 L11,7 L9,7 Z", "H"),

            new(ToolType.Zoom, ToolCategory.Navigation, "Zoom",
                "M3,10 A7,7 0 1 1 17,10 A7,7 0 1 1 3,10 Z M5.5,10 A4.5,4.5 0 1 1 14.5,10 A4.5,4.5 0 1 1 5.5,10 Z " +
                "M14.2,15.8 L15.8,14.2 L21.8,20.2 L20.2,21.8 Z", "Z"),

            new(ToolType.CanvasRotate, ToolCategory.Navigation, "Canvas Rotate",
                "M5,12 A7,7 0 1 1 19,12 A7,7 0 1 1 5,12 Z M7,12 A5,5 0 1 1 17,12 A5,5 0 1 1 7,12 Z M20,6 L16,5 L17,9 Z", "R"),

            // Painting & Editing
            new(ToolType.Brush, ToolCategory.Painting, "Brush",
                "M19.6,2.2 L20.8,3.4 L11.8,12.4 L10.6,11.2 Z M12,11 L9,20 L6,17 L10,12 Z", "B"),

            new(ToolType.Pencil, ToolCategory.Painting, "Pencil",
                "M18,4 L20,6 L9,17 L7,15 Z M7,15 L9,17 L6,20 Z", "N"),

            new(ToolType.Eraser, ToolCategory.Painting, "Eraser",
                "M6,14 L14,6 L20,12 L12,20 Z M12.5,7.5 L15,6 L16,9 Z", "E"),

            new(ToolType.PaintBucket, ToolCategory.Painting, "Paint Bucket",
                "M6,10 L18,10 L16,20 L8,20 Z M5.2,22 A1.8,1.8 0 1 1 8.8,22 A1.8,1.8 0 1 1 5.2,22 Z", "G"),

            new(ToolType.Gradient, ToolCategory.Painting, "Gradient",
                "M6,18 L8,20 L20,8 L18,6 Z M3.5,18 A2.5,2.5 0 1 1 8.5,18 A2.5,2.5 0 1 1 3.5,18 Z M17,4 H21 V8 H17 Z", "G"),

            // Retouching
            new(ToolType.CloneStamp, ToolCategory.Retouching, "Clone Stamp",
                "M6,16 L18,16 L16,20 L8,20 Z M9,10 H15 V16 H9 Z M9,7 A3,3 0 1 1 15,7 A3,3 0 1 1 9,7 Z", "S"),

            new(ToolType.HealingBrush, ToolCategory.Retouching, "Healing Brush",
                "M19.6,2.2 L20.8,3.4 L11.8,12.4 L10.6,11.2 Z M7,14 H9 V20 H7 Z M4,16 H12 V18 H4 Z", "J"),

            new(ToolType.Blur, ToolCategory.Retouching, "Blur",
                "M12,2 C16,8 19,12 19,16 A7,7 0 1 1 5,16 C5,12 8,8 12,2 Z", "R"),

            new(ToolType.Sharpen, ToolCategory.Retouching, "Sharpen",
                "M12,2 L15,14 L20,16 L12,22 L4,16 L9,14 Z", "R"),

            new(ToolType.Sponge, ToolCategory.Retouching, "Sponge",
                "M4,14 C4,9 8,6 12,6 C16,6 20,9 20,14 C20,18 16,20 12,20 C8,20 4,18 4,14 Z " +
                "M7.7,11 A1.3,1.3 0 1 1 10.3,11 A1.3,1.3 0 1 1 7.7,11 Z " +
                "M12.7,9 A1.3,1.3 0 1 1 15.3,9 A1.3,1.3 0 1 1 12.7,9 Z " +
                "M11.7,15 A1.3,1.3 0 1 1 14.3,15 A1.3,1.3 0 1 1 11.7,15 Z", "O"),

            new(ToolType.Dodge, ToolCategory.Retouching, "Dodge",
                "M11,10 H13 V22 H11 Z M6,7 A6,6 0 1 1 18,7 A6,6 0 1 1 6,7 Z", "O"),

            new(ToolType.Burn, ToolCategory.Retouching, "Burn",
                "M11,10 H13 V22 H11 Z M5,7 A7,5 0 1 1 19,7 A7,5 0 1 1 5,7 Z", "O"),

            // Vector & Text
            new(ToolType.Pen, ToolCategory.VectorText, "Pen (Path)",
                "M18,3 L21,6 L10,17 L7,14 Z M5,16 H8 V19 H5 Z", "P"),

            new(ToolType.Shape, ToolCategory.VectorText, "Shape",
                "M4,10 H16 V22 H4 Z M10,8 A6,6 0 1 1 22,8 A6,6 0 1 1 10,8 Z", "U"),

            new(ToolType.TextHorizontal, ToolCategory.VectorText, "Horizontal Text",
                "M4,4 H20 V7 H4 Z M10.5,7 H13.5 V20 H10.5 Z", "T"),

            new(ToolType.TextVertical, ToolCategory.VectorText, "Vertical Text",
                "M4,4 V20 H7 V4 Z M7,10.5 H20 V13.5 H7 Z", "T"),
        };
    }
}
