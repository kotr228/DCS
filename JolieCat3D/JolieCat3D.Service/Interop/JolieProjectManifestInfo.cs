namespace JolieCat3D.Service.Interop
{
    /// <summary>
    /// Plain-data mirror of <c>JolieCat.Core.Serialization.ProjectManifest</c> - the
    /// JSON shape a <c>.jolie</c> project's own "manifest.json" zip entry actually
    /// deserializes to (see <see cref="JolieProjectReader"/>'s own remarks on why this
    /// is an independent reimplementation, not a shared type, despite both solutions
    /// living in the same repository). Field names/types/defaults match
    /// <c>ProjectManifest</c> exactly (same JSON property names via
    /// <see cref="System.Text.Json"/>'s default camelCase-agnostic behavior - both sides
    /// use PascalCase property names, so no <c>JsonPropertyName</c> attributes are
    /// needed either side), but only the subset of fields <see cref="JolieProjectReader"/>
    /// actually needs (timeline data has no meaning for a 3D material texture slot, so
    /// isn't reproduced here).
    /// </summary>
    public sealed class JolieProjectManifestInfo
    {
        public string SceneName { get; set; } = string.Empty;

        public string ProjectType { get; set; } = "StandardImage";

        public JolieSpriteSheetGridInfo SpriteSheetGrid { get; set; } = new();

        public int DocumentWidth { get; set; }

        public int DocumentHeight { get; set; }

        /// <summary>Back-to-front, matching the original project's own layer order.</summary>
        public List<JolieLayerManifestEntry> Layers { get; set; } = new();

        /// <summary>Index into <see cref="Layers"/> of the layer that was active when
        /// the project was last saved; -1 if none. The natural "which layer is THE
        /// texture" default when loading a whole project rather than one named layer -
        /// see <see cref="JolieProjectReader.ExtractActiveLayerTexture"/>.</summary>
        public int ActiveLayerIndex { get; set; } = -1;
    }

    /// <summary>One layer's metadata, plus which zip entry holds its pixels - mirrors
    /// <c>JolieCat.Core.Serialization.LayerManifestEntry</c>.</summary>
    public sealed class JolieLayerManifestEntry
    {
        public string Name { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        public bool IsVisible { get; set; } = true;

        public double Opacity { get; set; } = 1.0;

        public string BlendMode { get; set; } = "Normal";

        /// <summary>Zip entry name (under "layers/") holding this layer's bitmap as a
        /// PNG - see <see cref="JolieProjectReader.ExtractLayerBitmap"/>.</summary>
        public string BitmapEntryName { get; set; } = string.Empty;
    }

    /// <summary>Plain-data mirror of <c>JolieCat.Core.Serialization.SpriteSheetGridData</c> -
    /// the manifest's OWN literal (integer-valued) JSON shape, distinct from this
    /// project's own <see cref="SpriteSheetGridInfo"/> (a double-valued record struct
    /// built for direct use with <see cref="TwoDAssetBridge.GetCellRect"/>) so a
    /// manifest read never needs an intermediate conversion just to deserialize -
    /// <see cref="JolieProjectReader"/>'s own <c>ToSpriteSheetGridInfo</c> converts
    /// between the two when a caller actually wants to slice a sprite-sheet-type
    /// project's texture into cells.</summary>
    public sealed class JolieSpriteSheetGridInfo
    {
        public int Columns { get; set; } = 4;

        public int Rows { get; set; } = 4;

        public int PaddingX { get; set; }

        public int PaddingY { get; set; }

        public int MarginX { get; set; }

        public int MarginY { get; set; }
    }
}
