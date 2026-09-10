namespace JolieCat3D.Service.Interop
{
    /// <summary>
    /// Plain-data mirror of the <c>TimelineFrameRate</c>/<c>TimelineTotalFrames</c>/
    /// <c>TimelineTracks</c> portion of <c>JolieCat.Core.Serialization.ProjectManifest</c>
    /// (see <see cref="JolieProjectManifestInfo"/>'s own remarks on why this is an
    /// independent reimplementation, not a shared type) - what
    /// <see cref="JolieAnimationExporter"/> actually writes. Deliberately NOT a whole
    /// manifest (there's no scene/layers/sprite-sheet-grid data to fill those fields
    /// with meaningfully from a 3D scene), just this fragment on its own, as a
    /// standalone JSON file - field-for-field identical in name/shape to the same
    /// fragment inside a real "manifest.json", so a JolieCat 2D user (or a future
    /// importer on either side) can paste/merge its own "TimelineTracks" array straight
    /// into an existing project's manifest without any translation step.
    /// </summary>
    public sealed class JolieTimelineExportData
    {
        public double TimelineFrameRate { get; set; }

        public double TimelineTotalFrames { get; set; }

        public List<JolieTimelineTrackData> TimelineTracks { get; set; } = new();
    }

    /// <summary>Mirrors <c>JolieCat.Core.Serialization.TimelineTrackData</c> exactly.
    /// <see cref="JolieAnimationExporter"/> never populates <see cref="Clips"/> (a
    /// "clip" is a 2D-timeline-specific arrangement - a span of frames a layer plays
    /// over - with no equivalent concept on a 3D <c>AnimationTrack</c>) - it's only
    /// present, always empty, because the real format requires the field to exist at
    /// all (matching <c>TimelineTrackData.Clips</c>' own default of an empty list, so an
    /// empty array here reads as "no clips", not as data loss).</summary>
    public sealed class JolieTimelineTrackData
    {
        public string Name { get; set; } = string.Empty;

        public List<JolieTimelineClipData> Clips { get; set; } = new();

        /// <summary>Every keyframe TIME on the source <c>AnimationTrack</c>, converted
        /// to a frame number (<c>Time * FrameRate</c>, the same unit
        /// <c>JolieCat.UI.ViewModels.Timeline.KeyframeViewModel</c>'s own frame position
        /// uses) - a real, meaningful mapping: JolieCat 2D's own timeline keyframes are
        /// likewise just marker FRAMES with no per-keyframe value payload of their own
        /// (see <c>TimelineTrackViewModel.AddKeyframeAtPlayhead</c>), so "this 3D track
        /// has a keyframe at frame N" is exactly what this field already means on the 2D
        /// side too - not an approximation of a 2D concept that doesn't exist, but the
        /// same concept, faithfully carried over.</summary>
        public List<double> KeyframeFrames { get; set; } = new();
    }

    /// <summary>Mirrors <c>JolieCat.Core.Serialization.TimelineClipData</c> - declared
    /// only so <see cref="JolieTimelineTrackData.Clips"/> has a concrete element type to
    /// deserialize as, should a future importer ever populate it from a real 2D
    /// project's own manifest.</summary>
    public sealed class JolieTimelineClipData
    {
        public string Name { get; set; } = string.Empty;

        public double StartFrame { get; set; }

        public double LengthFrames { get; set; }
    }
}
