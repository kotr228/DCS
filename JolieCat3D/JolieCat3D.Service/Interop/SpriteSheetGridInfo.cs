namespace JolieCat3D.Service.Interop
{
    /// <summary>
    /// The same sprite-sheet grid layout <c>JolieCat</c>'s own 2D editor workspace
    /// describes (its <c>Core.Documents.SpriteSheetGrid</c>, in the separate JolieCat
    /// solution - not referenced here; this is an independent reimplementation of the
    /// same field names/formulas, so <c>JolieCat3D.Service</c> never needs a project
    /// reference into that other solution just to recognize a grid it exported): how
    /// many Columns/Rows a sprite sheet image is divided into, plus an optional outer
    /// Margin and inter-cell Padding (both in source-image pixels) - everything
    /// <see cref="TwoDAssetBridge"/> needs to compute any one cell's own pixel rectangle
    /// within the full image.
    /// </summary>
    public readonly record struct SpriteSheetGridInfo(
        int Columns,
        int Rows,
        double MarginX = 0,
        double MarginY = 0,
        double PaddingX = 0,
        double PaddingY = 0);
}
