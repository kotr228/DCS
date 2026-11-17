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

            // A dashed box around a blob: draw a box, the thing in it gets selected.
            new(ToolType.ObjectSelection, ToolCategory.Selection, "Object Selection",
                "M2,2 H7 V3.5 H3.5 V7 H2 Z M17,2 H22 V7 H20.5 V3.5 H17 Z M2,17 H3.5 V20.5 H7 V22 H2 Z M20.5,17 H22 V22 H17 V20.5 H20.5 Z " +
                "M7,12 C7,8 10,6 12.5,7 C16,8 17.5,11 16,14.5 C14.5,17.5 9,18 7.5,15 C7.1,14 7,13 7,12 Z", "W"),

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

            // A slanted ruler with tick marks.
            new(ToolType.Ruler, ToolCategory.Navigation, "Ruler",
                "M2,17 L17,2 L22,7 L7,22 Z M4.8,17 L17,4.8 L19.2,7 L7,19.2 Z M5.6,16.3 L6.3,15.6 L8.4,17.7 L7.7,18.4 Z M8.6,13.3 L9.3,12.6 L10.5,13.8 L9.8,14.5 Z M11.6,10.3 L12.3,9.6 L14.4,11.7 L13.7,12.4 Z M14.6,7.3 L15.3,6.6 L16.5,7.8 L15.8,8.5 Z", "I"),

            // Painting & Editing
            new(ToolType.Brush, ToolCategory.Painting, "Brush",
                "M19.6,2.2 L20.8,3.4 L11.8,12.4 L10.6,11.2 Z M12,11 L9,20 L6,17 L10,12 Z", "B"),

            // A brush over two drops of paint (it mixes them).
            new(ToolType.MixerBrush, ToolCategory.Painting, "Mixer Brush",
                "M19.6,2.2 L20.8,3.4 L11.8,12.4 L10.6,11.2 Z M12,11 L9,20 L6,17 L10,12 Z " +
                "M2,9 C2,7 4,5 4,5 C4,5 6,7 6,9 A2,2 0 0 1 2,9 Z M16,19 C16,17 18,15 18,15 C18,15 20,17 20,19 A2,2 0 0 1 16,19 Z", "B"),

            new(ToolType.Pencil, ToolCategory.Painting, "Pencil",
                "M18,4 L20,6 L9,17 L7,15 Z M7,15 L9,17 L6,20 Z", "N"),

            new(ToolType.Eraser, ToolCategory.Painting, "Eraser",
                "M6,14 L14,6 L20,12 L12,20 Z M12.5,7.5 L15,6 L16,9 Z", "E"),

            // An eraser with a pair of scissors' worth of cut-out: erases a color, not a stroke.
            new(ToolType.BackgroundEraser, ToolCategory.Painting, "Background Eraser",
                "M6,14 L14,6 L20,12 L12,20 Z M3,20 H9 V22 H3 Z M2.5,4 A1.5,1.5 0 1 1 5.5,4 A1.5,1.5 0 1 1 2.5,4 Z", "E"),

            // An eraser with a sparkle: erases a whole same-colored area at one click.
            new(ToolType.MagicEraser, ToolCategory.Painting, "Magic Eraser",
                "M6,16 L14,8 L20,14 L12,22 Z M6,1 L7,4 L10,5 L7,6 L6,9 L5,6 L2,5 L5,4 Z", "E"),

            new(ToolType.PaintBucket, ToolCategory.Painting, "Paint Bucket",
                "M6,10 L18,10 L16,20 L8,20 Z M5.2,22 A1.8,1.8 0 1 1 8.8,22 A1.8,1.8 0 1 1 5.2,22 Z", "G"),

            new(ToolType.Gradient, ToolCategory.Painting, "Gradient",
                "M6,18 L8,20 L20,8 L18,6 Z M3.5,18 A2.5,2.5 0 1 1 8.5,18 A2.5,2.5 0 1 1 3.5,18 Z M17,4 H21 V8 H17 Z", "G"),

            new(ToolType.Eyedropper, ToolCategory.Painting, "Eyedropper",
                "M20.7,3.3 C21.9,4.5 21.9,6.4 20.7,7.6 L19.4,8.9 L15.1,4.6 L16.4,3.3 C17.6,2.1 19.5,2.1 20.7,3.3 Z " +
                "M13.7,6 L18,10.3 L8.4,19.9 L4,21 L5.1,16.6 Z", "I"),

            // An eyedropper over a small target ring.
            new(ToolType.ColorSampler, ToolCategory.Painting, "Color Sampler",
                "M20.7,3.3 C21.9,4.5 21.9,6.4 20.7,7.6 L19.4,8.9 L15.1,4.6 L16.4,3.3 C17.6,2.1 19.5,2.1 20.7,3.3 Z " +
                "M13.7,6 L18,10.3 L12,16.3 L7.7,12 Z M1,19 A4,4 0 1 1 9,19 A4,4 0 1 1 1,19 Z M3,19 A2,2 0 1 1 7,19 A2,2 0 1 1 3,19 Z", "I"),

            // Retouching
            new(ToolType.CloneStamp, ToolCategory.Retouching, "Clone Stamp",
                "M6,16 L18,16 L16,20 L8,20 Z M9,10 H15 V16 H9 Z M9,7 A3,3 0 1 1 15,7 A3,3 0 1 1 9,7 Z", "S"),

            new(ToolType.SpotHealing, ToolCategory.Retouching, "Spot Healing Brush",
                "M4.9,15.5 L15.5,4.9 A3.5,3.5 0 0 1 20.5,9.9 L9.9,20.5 A3.5,3.5 0 0 1 4.9,15.5 Z " +
                "M10.5,10.5 A1,1 0 1 1 10.51,10.5 Z M13.5,13.5 A1,1 0 1 1 13.51,13.5 Z M10.5,13.5 A1,1 0 1 1 10.51,13.5 Z M13.5,10.5 A1,1 0 1 1 13.51,10.5 Z", "J"),

            // A stamp over a checkered pattern.
            new(ToolType.PatternStamp, ToolCategory.Retouching, "Pattern Stamp",
                "M6,16 L18,16 L16,20 L8,20 Z M9,10 H15 V16 H9 Z M9,7 A3,3 0 1 1 15,7 A3,3 0 1 1 9,7 Z " +
                "M2,21 H4 V23 H2 Z M4,19 H6 V21 H4 Z M18,21 H20 V23 H18 Z M20,19 H22 V21 H20 Z", "S"),

            new(ToolType.HealingBrush, ToolCategory.Retouching, "Healing Brush",
                "M19.6,2.2 L20.8,3.4 L11.8,12.4 L10.6,11.2 Z M7,14 H9 V20 H7 Z M4,16 H12 V18 H4 Z", "J"),

            // A patch: a dashed-edge square with a stitch across it.
            new(ToolType.Patch, ToolCategory.Retouching, "Patch",
                "M4,6 H8 V8 H6 V10 H4 Z M10,6 H14 V8 H10 Z M16,6 H20 V10 H18 V8 H16 Z M4,12 H6 V16 H4 Z M18,12 H20 V16 H18 Z " +
                "M4,18 H6 V20 H8 V22 H4 Z M10,20 H14 V22 H10 Z M18,18 H20 V22 H16 V20 H18 Z M8,13 H16 V15 H8 Z", "J"),

            // Two crossed arrows over a small dashed square.
            new(ToolType.ContentAwareMove, ToolCategory.Retouching, "Content-Aware Move",
                "M12,2 L15,5 H13 V11 H19 V9 L22,12 L19,15 V13 H13 V19 H15 L12,22 L9,19 H11 V13 H5 V15 L2,12 L5,9 V11 H11 V5 H9 Z", "J"),

            // An eye with a dot for the pupil.
            new(ToolType.RedEye, ToolCategory.Retouching, "Red Eye",
                "M2,12 C6,5 18,5 22,12 C18,19 6,19 2,12 Z M4.5,12 C8,16.5 16,16.5 19.5,12 C16,7.5 8,7.5 4.5,12 Z " +
                "M9,12 A3,3 0 1 1 15,12 A3,3 0 1 1 9,12 Z", "J"),

            // A brush with a curved arrow (back in time).
            new(ToolType.HistoryBrush, ToolCategory.Retouching, "History Brush",
                "M19.6,2.2 L20.8,3.4 L11.8,12.4 L10.6,11.2 Z M12,11 L9,20 L6,17 L10,12 Z " +
                "M2,8 A6,6 0 0 1 8,2 L8,0.5 L11,3 L8,5.5 L8,4 A4,4 0 0 0 4,8 Z", "Y"),

            new(ToolType.Blur, ToolCategory.Retouching, "Blur",
                "M12,2 C16,8 19,12 19,16 A7,7 0 1 1 5,16 C5,12 8,8 12,2 Z", "R"),

            new(ToolType.Sharpen, ToolCategory.Retouching, "Sharpen",
                "M12,2 L15,14 L20,16 L12,22 L4,16 L9,14 Z", "R"),

            // A finger pushing paint.
            new(ToolType.Smudge, ToolCategory.Retouching, "Smudge",
                "M9,3 A2,2 0 0 1 13,3 V11 L18,12 A2,2 0 0 1 19.5,14.5 L17,21 H10 L6,15 A1.5,1.5 0 0 1 8.5,13.5 L9,14 Z", "R"),

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

            new(ToolType.PathSelection, ToolCategory.VectorText, "Path Selection",
                "M5,3 L5,19 L9,15 L12,21 L15,19.5 L12,13.5 L17,13.5 Z", "A"),

            new(ToolType.DirectSelection, ToolCategory.VectorText, "Direct Selection",
                "M5,3 L5,19 L9,15 L12,21 L15,19.5 L12,13.5 L17,13.5 Z " +
                "M5.5,10.5 A1.5,1.5 0 1 1 8.5,10.5 A1.5,1.5 0 1 1 5.5,10.5 Z", "A"),

            new(ToolType.Shape, ToolCategory.VectorText, "Shape",
                "M4,10 H16 V22 H4 Z M10,8 A6,6 0 1 1 22,8 A6,6 0 1 1 10,8 Z", "U"),

            new(ToolType.TextHorizontal, ToolCategory.VectorText, "Horizontal Text",
                "M4,4 H20 V7 H4 Z M10.5,7 H13.5 V20 H10.5 Z", "T"),

            new(ToolType.TextVertical, ToolCategory.VectorText, "Vertical Text",
                "M4,4 V20 H7 V4 Z M7,10.5 H20 V13.5 H7 Z", "T"),

            // Transform
            new(ToolType.Move, ToolCategory.Transform, "Move",
                "M12,2 L16,6 H13 V11 H18 V8 L22,12 L18,16 V13 H13 V18 H16 L12,22 L8,18 H11 V13 H6 V16 L2,12 L6,8 V11 H11 V6 H8 Z", "V"),

            new(ToolType.Crop, ToolCategory.Transform, "Crop",
                "M6,2 V18 H22 V20 H6 V22 H4 V20 H2 V18 H4 V6 H2 V4 H4 V2 Z M8,6 H18 V16 H20 V6 Z M8,8 V16 H16 V18 H8 Z", "C"),

            new(ToolType.PerspectiveCrop, ToolCategory.Transform, "Perspective Crop",
                "M6,3 L18,5 L21,19 L3,21 Z M7.6,5.4 L5.4,18.8 L19.1,17.3 L16.4,6.7 Z", "C"),

            new(ToolType.FreeTransform, ToolCategory.Transform, "Free Transform",
                "M2,2 H8 V4 H4 V8 H2 Z M16,2 H22 V8 H20 V4 H16 Z M2,16 H4 V20 H8 V22 H2 Z M20,16 H22 V22 H16 V20 H20 Z " +
                "M9,9 A1.5,1.5 0 1 1 12,9 A1.5,1.5 0 1 1 9,9 Z M9,15 A1.5,1.5 0 1 1 12,15 A1.5,1.5 0 1 1 9,15 Z", "Ctrl+T"),

            // A little figure held up by pins.
            new(ToolType.PuppetWarp, ToolCategory.Transform, "Puppet Warp",
                "M10,4 A2,2 0 1 1 14,4 A2,2 0 1 1 10,4 Z M11,7 H13 L17,12 L15.5,13 L13,10 V14 L16,21 H14 L12,16 L10,21 H8 L11,14 V10 L8.5,13 L7,12 Z " +
                "M2,11 A1.5,1.5 0 1 1 5,11 A1.5,1.5 0 1 1 2,11 Z M19,11 A1.5,1.5 0 1 1 22,11 A1.5,1.5 0 1 1 19,11 Z"),

            new(ToolType.Warp, ToolCategory.Transform, "Warp",
                "M2,2 H22 V22 H2 Z M2,9 H22 M2,15 H22 M9,2 V22 M15,2 V22 " +
                "M9,9 A1.3,1.3 0 1 1 11.6,9 A1.3,1.3 0 1 1 9,9 Z M15,15 A1.3,1.3 0 1 1 17.6,15 A1.3,1.3 0 1 1 15,15 Z"),
        };
    }
}
