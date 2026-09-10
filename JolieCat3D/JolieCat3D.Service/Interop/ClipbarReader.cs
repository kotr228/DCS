using System.Text.RegularExpressions;

namespace JolieCat3D.Service.Interop
{
    /// <summary>
    /// Recognizes a JolieCat 2D "Clipbar Animation" project (<c>ProjectType.ClipbarAnimation</c>,
    /// the type <c>JolieCat.Core.Documents.SpriteSheetToClipbarConverter</c> produces
    /// from a Sprite Sheet project) - every frame already lives as its own separate
    /// layer, named <c>"Frame 000"</c>, <c>"Frame 001"</c>, ... (zero-padded to at least
    /// 3 digits, via that converter's own <c>FrameLayerName(int)</c> - reimplemented
    /// here independently as a regex, not a project reference, for the same reason as
    /// every other class in this namespace - see <see cref="TwoDAssetBridge"/>'s own
    /// remarks). Finding and extracting those layers, in frame order, is everything
    /// <see cref="ClipbarAnimationBridge"/> needs to turn a clipbar project into an
    /// animated texture sequence.
    /// </summary>
    public static class ClipbarReader
    {
        private static readonly Regex FrameLayerPattern = new(@"^Frame (?<index>\d{3,})$", RegexOptions.Compiled);

        /// <summary>True when <paramref name="manifest"/> identifies itself as a
        /// Clipbar Animation project - the same <c>ProjectType</c> string
        /// <c>SpriteSheetToClipbarConverter</c>'s own derivation produces. Not required
        /// for <see cref="GetFrameLayers"/>/<see cref="ExtractClipbarFrames"/> to work
        /// (both just look for "Frame NNN"-named layers regardless of the declared
        /// type), but lets a caller (<see cref="ClipbarAnimationBridge"/>) tell "this is
        /// a proper clipbar" apart from "this happens to have a layer that looks like
        /// one".</summary>
        public static bool IsClipbarProject(JolieProjectManifestInfo manifest)
        {
            ArgumentNullException.ThrowIfNull(manifest);
            return string.Equals(manifest.ProjectType, "ClipbarAnimation", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Every <c>"Frame NNN"</c>-named layer in <paramref name="manifest"/>,
        /// ordered by its own PARSED frame index - not manifest/layer-list order (which,
        /// while already frame-ordered by construction in
        /// <c>SpriteSheetToClipbarConverter.Convert</c>, isn't relied on here, the same
        /// "verify/derive it directly, don't just assume file order" discipline this
        /// project already applies elsewhere - e.g. <c>TwoDAssetBridge.FindClipbarFrameSequence</c>'s
        /// own explicit sort). A grid cell that collapsed to nothing during the original
        /// SpriteSheet-to-Clipbar derivation is skipped there (no layer at all), so a
        /// frame index appearing here is not guaranteed to be perfectly consecutive -
        /// callers should key frame TIMING off this list's own order, not off each
        /// entry's raw index number.</summary>
        public static IReadOnlyList<(int FrameIndex, JolieLayerManifestEntry Layer)> GetFrameLayers(JolieProjectManifestInfo manifest)
        {
            ArgumentNullException.ThrowIfNull(manifest);

            var frames = new List<(int FrameIndex, JolieLayerManifestEntry Layer)>();
            foreach (var layer in manifest.Layers)
            {
                var match = FrameLayerPattern.Match(layer.Name);
                if (match.Success) frames.Add((int.Parse(match.Groups["index"].Value), layer));
            }

            return frames.OrderBy(frame => frame.FrameIndex).ToList();
        }

        /// <summary>Extracts every <c>"Frame NNN"</c> layer's own bitmap (via
        /// <see cref="JolieProjectReader.ExtractLayerBitmap"/> - a raw byte copy, no
        /// decode needed) to <paramref name="outputDirectory"/>, named deterministically
        /// from the project file's own name plus frame index (so re-extracting after
        /// the source project changes overwrites the same files - the established
        /// live-refresh convention <see cref="JolieProjectReader.ExtractActiveLayerTexture"/>
        /// already uses), returned in ascending frame-index order.</summary>
        public static IReadOnlyList<string> ExtractClipbarFrames(string jolieFilePath, string outputDirectory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(jolieFilePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

            var manifest = JolieProjectReader.ReadManifest(jolieFilePath);
            var frameLayers = GetFrameLayers(manifest);
            if (frameLayers.Count == 0)
                throw new InvalidDataException($"'{jolieFilePath}' has no \"Frame NNN\"-named layers to extract.");

            var projectBaseName = Path.GetFileNameWithoutExtension(jolieFilePath);
            var paths = new List<string>(frameLayers.Count);

            foreach (var (frameIndex, layer) in frameLayers)
            {
                var outputPath = Path.Combine(outputDirectory, $"{projectBaseName}_{frameIndex:D3}.png");
                JolieProjectReader.ExtractLayerBitmap(jolieFilePath, layer.BitmapEntryName, outputPath);
                paths.Add(outputPath);
            }

            return paths;
        }
    }
}
