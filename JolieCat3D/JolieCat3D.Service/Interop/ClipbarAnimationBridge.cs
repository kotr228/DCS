using System.Numerics;
using JolieCat3D.Core.Materials;
using JolieCat3D.Service.Animation;

namespace JolieCat3D.Service.Interop
{
    /// <summary>
    /// The actual "import a clipbar/sprite sheet as an animated texture sequence"
    /// bridge: builds a <see cref="TextureAnimationTrack"/> - ready to hand to
    /// <see cref="AnimationTimeline.SetTextureTrack"/> - from either a real JolieCat 2D
    /// Clipbar Animation project (<see cref="CreateFromClipbarProject"/>, one frame per
    /// "Frame NNN" layer - see <see cref="ClipbarReader"/>), a Sprite Sheet project
    /// (<see cref="CreateFromSpriteSheetProject"/>, one frame per grid cell, all
    /// sampling the SAME extracted image via a different UV sub-rect each - no
    /// compositing/decoding needed, since a Sprite Sheet project's own grid math and
    /// document size are both already plain manifest data), or a plain folder of
    /// separately-exported clipbar frame images (<see cref="CreateFromClipbarFolder"/>,
    /// no <c>.jolie</c> project at all - <see cref="TwoDAssetBridge.FindClipbarFrameSequence"/>'s
    /// own established naming convention).
    /// </summary>
    public static class ClipbarAnimationBridge
    {
        private const double DefaultFrameRate = 24.0;

        /// <summary>From a real Clipbar Animation <c>.jolie</c> project - one frame per
        /// <c>"Frame NNN"</c>-named layer (see <see cref="ClipbarReader"/>), spaced at
        /// the project's own authored <c>TimelineFrameRate</c> (falling back to
        /// <see cref="DefaultFrameRate"/> if that's unset/non-positive - an older or
        /// hand-edited project file, say).</summary>
        /// <exception cref="InvalidDataException"><paramref name="jolieFilePath"/> has
        /// no "Frame NNN"-named layers at all.</exception>
        public static TextureAnimationTrack CreateFromClipbarProject(Material material, string jolieFilePath, string outputDirectory)
        {
            ArgumentNullException.ThrowIfNull(material);
            ArgumentException.ThrowIfNullOrWhiteSpace(jolieFilePath);

            var manifest = JolieProjectReader.ReadManifest(jolieFilePath);
            var framePaths = ClipbarReader.ExtractClipbarFrames(jolieFilePath, outputDirectory);
            var frameRate = manifest.TimelineFrameRate > 0 ? manifest.TimelineFrameRate : DefaultFrameRate;

            var track = new TextureAnimationTrack(material);
            for (var i = 0; i < framePaths.Count; i++)
                track.AddFrame(i / frameRate, TextureFrame.WholeImage(framePaths[i]));

            return track;
        }

        /// <summary>From a Sprite Sheet <c>.jolie</c> project - one frame per grid cell,
        /// in the same row-major reading order (row 0 left-to-right, then row 1, ...)
        /// <c>JolieCat.Core.Documents.SpriteSheetGrid.EnumerateCells</c> itself defines
        /// (confirmed by reading that class directly, not assumed) - every frame samples
        /// the SAME extracted active-layer image (see <see cref="JolieProjectReader.ExtractActiveLayerTexture"/>),
        /// just a different UV sub-rect each (via <see cref="TwoDAssetBridge.GetCellRect"/>'s
        /// own already-verified formula/V-flip), since this project has no way to
        /// composite/re-encode a cropped-per-cell image of its own (see
        /// <see cref="TwoDAssetBridge"/>'s own remarks on why).</summary>
        public static TextureAnimationTrack CreateFromSpriteSheetProject(Material material, string jolieFilePath, string outputDirectory, double? frameRateOverride = null)
        {
            ArgumentNullException.ThrowIfNull(material);
            ArgumentException.ThrowIfNullOrWhiteSpace(jolieFilePath);

            var manifest = JolieProjectReader.ReadManifest(jolieFilePath);
            var texturePath = JolieProjectReader.ExtractActiveLayerTexture(jolieFilePath, outputDirectory);
            var grid = JolieProjectReader.ToSpriteSheetGridInfo(manifest.SpriteSheetGrid);
            var frameRate = frameRateOverride ?? (manifest.TimelineFrameRate > 0 ? manifest.TimelineFrameRate : DefaultFrameRate);

            var track = new TextureAnimationTrack(material);
            var frameIndex = 0;

            // Row-major: row 0's columns left-to-right, then row 1's, ... - matching
            // SpriteSheetGrid.EnumerateCells exactly.
            for (var row = 0; row < grid.Rows; row++)
            {
                for (var column = 0; column < grid.Columns; column++)
                {
                    var (x, y, width, height) = TwoDAssetBridge.GetCellRect(grid, column, row, manifest.DocumentWidth, manifest.DocumentHeight);

                    var offsetU = (float)(x / manifest.DocumentWidth);
                    var scaleU = (float)(width / manifest.DocumentWidth);
                    var scaleV = (float)(height / manifest.DocumentHeight);
                    // V-flipped (image rows count down from the top; UV's V counts up
                    // from the bottom) - the same conversion TwoDAssetBridge.LoadSpriteSheetCellMaterial
                    // already uses for the non-animated single-cell case.
                    var offsetV = (float)(1.0 - y / manifest.DocumentHeight - scaleV);

                    track.AddFrame(frameIndex / frameRate, new TextureFrame(texturePath, new Vector2(offsetU, offsetV), new Vector2(scaleU, scaleV)));
                    frameIndex++;
                }
            }

            return track;
        }

        /// <summary>From a plain folder of separately-exported clipbar frame images -
        /// <see cref="TwoDAssetBridge.FindClipbarFrameSequence"/>'s own
        /// <c>{baseName}_{index:D3}.ext</c> naming convention - with no <c>.jolie</c>
        /// project involved at all.</summary>
        public static TextureAnimationTrack CreateFromClipbarFolder(Material material, string folderPath, string baseName, double frameRate = DefaultFrameRate)
        {
            ArgumentNullException.ThrowIfNull(material);

            var frames = TwoDAssetBridge.FindClipbarFrameSequence(folderPath, baseName);
            var track = new TextureAnimationTrack(material);
            for (var i = 0; i < frames.Count; i++)
                track.AddFrame(i / frameRate, TextureFrame.WholeImage(frames[i]));

            return track;
        }
    }
}
