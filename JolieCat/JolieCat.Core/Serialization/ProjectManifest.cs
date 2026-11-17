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

        /// <summary>The document's artboards (named export areas).</summary>
        public List<ArtboardData>? Artboards { get; set; }

        public int DocumentWidth { get; set; }

        public int DocumentHeight { get; set; }

        /// <summary>Back-to-front, matching <c>Scene.Layers</c>' own order.</summary>
        public List<LayerManifestEntry> Layers { get; set; } = new();

        /// <summary>Index into <see cref="Layers"/> of the active layer; -1 if none.</summary>
        public int ActiveLayerIndex { get; set; } = -1;

        public double TimelineTotalFrames { get; set; }

        public double TimelineFrameRate { get; set; }

        public List<TimelineTrackData> TimelineTracks { get; set; } = new();

        /// <summary>Saved selections (Select > Save Selection): name -> SVG path data.</summary>
        public Dictionary<string, string> SavedSelections { get; set; } = new();

        /// <summary>Named Pen paths (Paths panel).</summary>
        public Dictionary<string, string>? SavedPaths { get; set; }

        /// <summary>Guides as "v:120" / "h:64.5" (vertical x / horizontal y, document pixels).</summary>
        public List<string> Guides { get; set; } = new();
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

        /// <summary>Fill opacity (the pixels only, not the outside effects), 0..1.</summary>
        public double FillOpacity { get; set; } = 1.0;

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

        /// <summary>Smart Filter layers only: the untouched original (a PNG entry) and
        /// the filter list (JSON, see <c>SmartFilter.Serialize</c>).</summary>
        public string? SmartFilterSourceEntryName { get; set; }

        public string? SmartFilters { get; set; }

        /// <summary>Shape layers only: the shape (see <c>ShapeContent.Serialize</c>).</summary>
        public string? Shape { get; set; }

        /// <summary>Fill layers only: the fill (see <c>FillContent.Serialize</c>).</summary>
        public string? Fill { get; set; }

        /// <summary>Adjustment layers only: the adjustment's settings (see
        /// <c>AdjustmentSettings.Serialize</c>). Null for every other layer.</summary>
        public string? Adjustment { get; set; }

        /// <summary>Layer style effects (see <c>LayerStyle.Serialize</c>), or null.</summary>
        public string? Style { get; set; }

        /// <summary>Index (in this list) of the group this layer is in, or null at the top level.</summary>
        public int? ParentIndex { get; set; }

        /// <summary>Groups: whether the Layers panel shows the members.</summary>
        public bool IsExpanded { get; set; } = true;

        /// <summary>Clipped to the layer below (clipping mask).</summary>
        public bool IsClipped { get; set; }

        /// <summary>Editable text layers: the text and its settings. Null otherwise.</summary>
        public TextLayerManifest? Text { get; set; }
    }

    /// <summary>One timeline track's clips and keyframes - enough to reconstruct
    /// <c>JolieCat.UI.ViewModels.Timeline.TimelineTrackViewModel</c> without this (UI-agnostic)
    /// project referencing that UI type.</summary>
    public sealed class TimelineTrackData
    {
        public string Name { get; set; } = string.Empty;

        public List<TimelineClipData> Clips { get; set; } = new();

        public List<double> KeyframeFrames { get; set; } = new();

        /// <summary>The layer this track animates (by name), or null.</summary>
        public string? LayerName { get; set; }

        /// <summary>An audio track: the sound file it plays (its one clip is where), or null.</summary>
        public string? AudioPath { get; set; }

        /// <summary>Audio track volume, 0..100.</summary>
        public double Volume { get; set; } = 100;

        /// <summary>Silenced (audio tracks).</summary>
        public bool Muted { get; set; }

        /// <summary>The animation camera's track (its keyframes zoom/pan/turn the whole picture).</summary>
        public bool IsCamera { get; set; }

        /// <summary>The markers track (its clips are named spots on the timeline).</summary>
        public bool IsMarkers { get; set; }

        /// <summary>Bones: the layer this track's layer is attached to (by name), or null.</summary>
        public string? ParentLayerName { get; set; }

        /// <summary>The layer's joint (what it turns around), or null for the middle of its content.</summary>
        public double? PivotX { get; set; }
        public double? PivotY { get; set; }

        /// <summary>Where the layer bends (the elbow), or null: it doesn't bend.</summary>
        public double? BendX { get; set; }
        public double? BendY { get; set; }

        /// <summary>Fast motion smears (motion blur).</summary>
        public bool MotionBlur { get; set; }

        /// <summary>Wiggle: shift (px), turn (degrees) and shakes a second.</summary>
        public double WiggleAmount { get; set; }
        public double WiggleRotation { get; set; }
        public double WiggleSpeed { get; set; } = 2;

        /// <summary>Not a real track: the work area (In = its clip's start, Out = its last frame).</summary>
        public bool IsWorkArea { get; set; }

        /// <summary>The sound file stored inside the project (so it travels with it), or null.</summary>
        public string? AudioEntryName { get; set; }

        /// <summary>The keyframes with their values (newer files; older ones only have <see cref="KeyframeFrames"/>).</summary>
        public List<KeyframeData>? Keys { get; set; }
    }

    public sealed class KeyframeData
    {
        public double Frame { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Scale { get; set; } = 100;
        public double Rotation { get; set; }
        public double Opacity { get; set; } = 100;
        /// <summary>Older files: true = ease in and out, false = linear.</summary>
        public bool Ease { get; set; } = true;

        /// <summary>The easing by name (newer files).</summary>
        public string? Easing { get; set; }

        /// <summary>The Custom easing's curve handles.</summary>
        public double CurveX1 { get; set; } = 0.42;
        public double CurveY1 { get; set; }
        public double CurveX2 { get; set; } = 0.58;
        public double CurveY2 { get; set; } = 1;

        /// <summary>Text layers: how much of the text shows (percent).</summary>
        public double Reveal { get; set; } = 100;

        /// <summary>Blur in pixels.</summary>
        public double Blur { get; set; }

        /// <summary>Bend at the track's bend point, in degrees.</summary>
        public double Bend { get; set; }

        /// <summary>Color effects: brightness, saturation (percent), hue (degrees).</summary>
        public double Brightness { get; set; }
        public double Saturation { get; set; } = 100;
        public double Hue { get; set; }

        public Animation.KeyEasing ToEasing() =>
            System.Enum.TryParse<Animation.KeyEasing>(Easing, out var e) ? e : Ease ? Animation.KeyEasing.EaseInOut : Animation.KeyEasing.Linear;
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

        public double PivotX { get; set; } = 50;

        public double PivotY { get; set; } = 100;

        public string RowNames { get; set; } = string.Empty;

        /// <summary>Cells with their own pivot: "index:x,y;..." (percent).</summary>
        public string CellPivots { get; set; } = string.Empty;
    }

    /// <summary>An artboard: a named area exported as its own picture.</summary>
    public sealed class ArtboardData
    {
        public string Name { get; set; } = string.Empty;
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    /// <summary>An editable text layer's content (see <c>Documents.TextContent</c>).</summary>
    public sealed class TextLayerManifest
    {
        public string Text { get; set; } = string.Empty;
        public string FontFamily { get; set; } = "Segoe UI";
        public float FontSize { get; set; } = 24;
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        /// <summary>#AARRGGBB</summary>
        public string Color { get; set; } = "#FF000000";
        public float X { get; set; }
        public float Y { get; set; }
        public bool Vertical { get; set; }
        public string Alignment { get; set; } = "Left";
        public float Leading { get; set; } = 100;
        public float Tracking { get; set; }
        public bool Underline { get; set; }
        public bool Strikethrough { get; set; }
        public float Warp { get; set; }
        public string WarpStyle { get; set; } = "Arc";

        /// <summary>Type on a path: the curve the letters follow (SVG path data), or null.</summary>
        public string? PathData { get; set; }

        /// <summary>Paragraph text: the width words wrap at (0: point text).</summary>
        public float BoxWidth { get; set; }
    }
}
