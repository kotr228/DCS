using JolieCat.Shared.Enums;
using SkiaSharp;

namespace JolieCat.Core.History
{
    /// <summary>
    /// A frozen copy of one <see cref="Documents.Layer"/>'s metadata and pixel content -
    /// everything <c>Scene.RestoreLayers</c> needs to reconstruct an equivalent layer from
    /// scratch. Deliberately not just a reference to the original <see cref="Documents.Layer"/>:
    /// structural undo/redo (see <see cref="SceneStructuralCommand"/>) always rebuilds
    /// fresh layer instances, since a removed layer's bitmap may already be disposed by
    /// the time an undo runs.
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
        SKColor[]? MaskPixels);
}
