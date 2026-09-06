using System.Collections.Generic;

namespace JolieCat.Core.Serialization
{
    /// <summary>
    /// The plain-data description of a whole <c>.jolie</c> project, serialized as
    /// "manifest.json" inside the project's zip container (see <see cref="ProjectSerializer"/>).
    /// Every layer's actual pixels live alongside it as a separate PNG entry, referenced
    /// by <see cref="LayerManifestEntry.BitmapEntryName"/> - keeping the manifest itself
    /// small and human-readable rather than embedding raw/base64 pixel data in the JSON.
    /// </summary>
    public sealed class ProjectManifest
    {
        public string SceneName { get; set; } = string.Empty;

        /// <summary><see cref="Shared.Enums.ProjectType"/>'s name, kept as a string for
        /// the same reason as <see cref="LayerManifestEntry.Type"/>: the manifest stays
        /// readable and doesn't hard-code the enum's underlying integer values into the
        /// file format.</summary>
        public string ProjectType { get; set; } = "StandardImage";

        /// <summary>The Sprite Sheet slicing grid - present in every manifest
        /// regardless of <see cref="ProjectType"/> (matching <see cref="Documents.Document.SpriteSheetGrid"/>
        /// always being present too), only meaningful when it's "SpriteSheet".</summary>
        public SpriteSheetGridData SpriteSheetGrid { get; set; } = new();

        public int DocumentWidth { get; set; }

        public int DocumentHeight { get; set; }

        /// <summary>Back-to-front, matching <c>Scene.Layers</c>' own order.</summary>
        public List<LayerManifestEntry> Layers { get; set; } = new();

        /// <summary>Index into <see cref="Layers"/> of the active layer; -1 if none.</summary>
        public int ActiveLayerIndex { get; set; } = -1;

        public double TimelineTotalFrames { get; set; }

        public double TimelineFrameRate { get; set; }

        public List<TimelineTrackData> TimelineTracks { get; set; } = new();
    }

    /// <summary>One layer's metadata, plus which zip entry holds its pixels.</summary>
    public sealed class LayerManifestEntry
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>"Raster", "Vector", "Text", "Group", or "Adjustment" - <see cref="Shared.Enums.LayerType"/>'s
        /// name, kept as a string so the manifest stays readable and doesn't hard-code the
        /// enum's underlying integer values into the file format.</summary>
        public string Type { get; set; } = string.Empty;

        public bool IsVisible { get; set; } = true;

        public bool IsLocked { get; set; }

        public double Opacity { get; set; } = 1.0;

        /// <summary><see cref="Shared.Enums.BlendMode"/>'s name, for the same reason as <see cref="Type"/>.</summary>
        public string BlendMode { get; set; } = "Normal";

        /// <summary>Zip entry name (under "layers/") holding this layer's bitmap as a PNG.</summary>
        public string BitmapEntryName { get; set; } = string.Empty;

        /// <summary>Whether this layer has a mask at all - <see cref="MaskEntryName"/>
        /// is only meaningful when this is true, distinguishing "no mask" from a mask
        /// entry name that happens to be empty/missing in a hand-edited or corrupted file.</summary>
        public bool HasMask { get; set; }

        /// <summary>Mirrors <see cref="Documents.LayerMask.IsEnabled"/> - whether the mask
        /// currently affects compositing, independent of whether one exists at all.</summary>
        public bool IsMaskEnabled { get; set; } = true;

        /// <summary>Zip entry name (under "layers/") holding this layer's mask as a PNG -
        /// only present when <see cref="HasMask"/> is true.</summary>
        public string? MaskEntryName { get; set; }
    }

    /// <summary>One timeline track's clips and keyframes - enough to reconstruct
    /// <c>JolieCat.UI.ViewModels.Timeline.TimelineTrackViewModel</c> without this (UI-agnostic)
    /// project referencing that UI type.</summary>
    public sealed class TimelineTrackData
    {
        public string Name { get; set; } = string.Empty;

        public List<TimelineClipData> Clips { get; set; } = new();

        public List<double> KeyframeFrames { get; set; } = new();
    }

    public sealed class TimelineClipData
    {
        public string Name { get; set; } = string.Empty;

        public double StartFrame { get; set; }

        public double LengthFrames { get; set; }
    }

    /// <summary>Plain-data mirror of <see cref="Documents.SpriteSheetGrid"/>'s own
    /// settings - kept separate (rather than serializing that class directly) for the
    /// same reason every other manifest type in this file is a plain record: so the
    /// project file's JSON shape stays independent of that class's internal API.</summary>
    public sealed class SpriteSheetGridData
    {
        public int Columns { get; set; } = 4;

        public int Rows { get; set; } = 4;

        public int PaddingX { get; set; }

        public int PaddingY { get; set; }

        public int MarginX { get; set; }

        public int MarginY { get; set; }
    }
}
