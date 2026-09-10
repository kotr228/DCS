using System.Text.Json;
using JolieCat3D.Service.Animation;

namespace JolieCat3D.Service.Interop
{
    /// <summary>
    /// Writes an <see cref="AnimationTimeline"/>'s own keyframe TIMES out in JolieCat
    /// 2D's own "TimelineTracks" JSON shape (see <see cref="JolieTimelineExportData"/>'s
    /// own remarks) - the "back into JolieCat-compatible formats" half of this
    /// application's animation export story, as opposed to <see cref="AnimationExporter"/>'s
    /// own full-fidelity native interchange format (which round-trips complete
    /// position/rotation/scale curves, not just keyframe positions). One <see cref="AnimationTrack"/>
    /// (named after its own <c>Node.Name</c>, since a 2D timeline track is itself just a
    /// name plus its clips/keyframes - no node hierarchy of its own to preserve) becomes
    /// one <see cref="JolieTimelineTrackData"/>; a <see cref="TextureAnimationTrack"/>
    /// has no equivalent (a 2D layer's own visibility/opacity timeline has nothing
    /// comparable to a 3D material's texture-frame sequence), so texture tracks are not
    /// represented here at all - only <see cref="AnimationExporter"/>'s own native format
    /// carries those.
    /// </summary>
    public static class JolieAnimationExporter
    {
        private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

        /// <summary>Writes every transform track on <paramref name="timeline"/> to
        /// <paramref name="filePath"/> as a standalone <see cref="JolieTimelineExportData"/>
        /// JSON file - see this class's own remarks for exactly what is (and isn't)
        /// carried over. A track with no keyframes at all is still included (as a track
        /// with an empty <see cref="JolieTimelineTrackData.KeyframeFrames"/>), the same
        /// "include it anyway" convention <see cref="AnimationExporter.ExportJson"/>
        /// already follows.</summary>
        public static void ExportTimelineTracksJson(AnimationTimeline timeline, string filePath)
        {
            ArgumentNullException.ThrowIfNull(timeline);
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            var data = new JolieTimelineExportData
            {
                TimelineFrameRate = timeline.FrameRate,
                TimelineTotalFrames = timeline.TotalFrames,
            };

            foreach (var track in timeline.Tracks)
            {
                var trackData = new JolieTimelineTrackData { Name = track.Target.Name };
                foreach (var keyframe in track.Keyframes)
                    trackData.KeyframeFrames.Add(keyframe.Time * timeline.FrameRate);

                data.TimelineTracks.Add(trackData);
            }

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            JsonSerializer.Serialize(stream, data, WriteOptions);
        }
    }
}
