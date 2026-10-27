using System;
using JolieCat.Shared.Enums;
using SkiaSharp;

namespace JolieCat.Core.History
{
    /// <summary>
    /// A frozen copy of one <see cref="Documents.Layer"/>'s metadata and pixel content -
    /// everything <c>Scene.RestoreLayers</c> needs to reconstruct an equivalent layer from
    /// scratch. The layer's <see cref="Id"/> is restored too, so pixel history recorded
    /// against the layer keeps working across structural undo/redo. A Smart Object's
    /// content is shared by reference (it is never modified in place).
    /// </summary>
    public sealed record LayerSnapshot(
        string Name,
        int Width,
        int Height,
        LayerType Type,
        bool IsVisible,
        bool IsLocked,
        double Opacity,
        BlendMode BlendMode,
        SKColor[] Pixels,
        bool HasMask,
        bool IsMaskEnabled,
        SKColor[]? MaskPixels,
        Guid Id = default,
        Documents.SmartObjectContent? SmartObject = null,
        SKMatrix? SmartObjectMatrix = null,
        Adjustments.AdjustmentSettings? Adjustment = null,
        Effects.LayerStyle? Style = null,
        Guid? ParentId = null,
        bool IsExpanded = true,
        bool IsClipped = false,
        Documents.TextContent? Text = null)
    {
        /// <summary>Approximate memory held by this snapshot.</summary>
        public long ByteSize => (Pixels.LongLength + (MaskPixels?.LongLength ?? 0)) * 4;
    }
}
