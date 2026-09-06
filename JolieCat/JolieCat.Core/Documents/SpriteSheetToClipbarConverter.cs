using System;
using System.Collections.Generic;
using JolieCat.Core.Export;
using JolieCat.Core.Rendering;
using JolieCat.Core.Serialization;
using SkiaSharp;

namespace JolieCat.Core.Documents
{
    /// <summary>
    /// Derives a <see cref="Shared.Enums.ProjectType.ClipbarAnimation"/> project directly
    /// from an existing <see cref="Shared.Enums.ProjectType.SpriteSheet"/> project's
    /// current state (Type 2 -&gt; Type 3 derivation) - the sprite sheet's grid slices its
    /// flattened composite into cells exactly like <see cref="ImageExportService.ExportSpriteSheetCells"/>
    /// does for a file export, except each cell becomes its own <see cref="Layer"/> in a
    /// brand new <see cref="Documents.Scene"/> (a "flipbook": every frame layer already
    /// exists, stacked, with only the first visible) plus one <see cref="TimelineTrackData"/>
    /// clip per frame, positioned sequentially - the foundational track assets
    /// <c>JolieCat.UI.ViewModels.MainViewModel</c>'s derivation command hands to the new
    /// document's Timeline. <c>TimelineViewModel.RewireFrameLayers</c> is what later
    /// reconnects each clip to its own frame layer (by name) so scrubbing the playhead
    /// actually shows only that frame - this class only builds the plain data both sides
    /// agree on, with no dependency on any UI view model.
    /// </summary>
    public static class SpriteSheetToClipbarConverter
    {
        /// <summary>Every frame layer is named this way, zero-padded to three digits
        /// ("Frame 000", "Frame 001", ...) - <c>TimelineViewModel.RewireFrameLayers</c>
        /// matches a clip back to its own layer by this exact name.</summary>
        public static string FrameLayerName(int frameIndex) => $"Frame {frameIndex:D3}";

        /// <summary>Everything the derivation produces: the new Clipbar Animation
        /// scene (caller owns it and must dispose it, same as any other freshly built
        /// <see cref="Documents.Scene"/>), its one "Frames" track worth of clip data, and
        /// the total frame count/frame rate the new document's Timeline should adopt.</summary>
        public sealed record Result(Scene Scene, IReadOnlyList<TimelineTrackData> TimelineTracks, double TotalFrames, double FrameRate);

        /// <summary>The track name every derivation produces - <c>TimelineViewModel.RewireFrameLayers</c>
        /// doesn't need this (it matches by clip name, not track name), but a caller
        /// wanting to recognize "this track came from a Sprite Sheet derivation" can.</summary>
        public const string FrameTrackName = "Frames";

        /// <summary>
        /// Slices <paramref name="sourceScene"/>'s flattened composite (sized
        /// <paramref name="documentWidth"/> by <paramref name="documentHeight"/>) into
        /// <paramref name="grid"/>'s own cells, in the same row-major order
        /// <see cref="SpriteSheetGrid.EnumerateCells"/> already defines - a cell whose
        /// configured rectangle has collapsed to nothing is skipped, not turned into a
        /// degenerate zero-size layer, but still counts toward the frame index so a
        /// later cell's own number always matches its position in the grid (mirroring
        /// <see cref="ImageExportService.ExportSpriteSheetCells"/>'s identical rule).
        /// Every frame layer is uniformly sized to one cell (<see cref="SpriteSheetGrid.GetCellSize"/>) -
        /// the new document's own canvas size, since every layer in a scene must match
        /// it - with only the first visible, so the new project opens already showing
        /// its first frame rather than every frame's pixels stacked on top of each other.
        /// </summary>
        public static Result Convert(Scene sourceScene, int documentWidth, int documentHeight, SpriteSheetGrid grid, double frameRate = 24)
        {
            ArgumentNullException.ThrowIfNull(sourceScene);
            ArgumentNullException.ThrowIfNull(grid);

            using var flattened = SceneCompositor.Flatten(sourceScene, documentWidth, documentHeight);

            var cellSize = grid.GetCellSize(documentWidth, documentHeight);
            var cellWidth = Math.Max(1, (int)MathF.Round(cellSize.Width));
            var cellHeight = Math.Max(1, (int)MathF.Round(cellSize.Height));

            var clipbarScene = new Scene($"{sourceScene.Name} (Clipbar)");
            var clips = new List<TimelineClipData>();
            var frameIndex = 0;

            foreach (var (_, _, rect) in grid.EnumerateCells(documentWidth, documentHeight))
            {
                var cellRegion = SKRectI.Round(rect);
                if (cellRegion.Width > 0 && cellRegion.Height > 0)
                {
                    var layer = clipbarScene.AddLayer(FrameLayerName(frameIndex), cellWidth, cellHeight);

                    using (var cropped = ImageExportService.CropRegion(flattened, cellRegion))
                        layer.Canvas.DrawBitmap(cropped, 0, 0);

                    // Flipbook semantics: exactly one frame's own layer is visible at a
                    // time - every frame after the first starts hidden, matching the
                    // clip range TimelineViewModel's playhead will drive it from.
                    layer.IsVisible = frameIndex == 0;

                    clips.Add(new TimelineClipData { Name = layer.Name, StartFrame = frameIndex, LengthFrames = 1 });
                }

                frameIndex++;
            }

            // A grid degenerate enough to produce zero real cells still needs a scene
            // with at least one layer - every other Scene in this codebase assumes that
            // (DocumentWidth/Height read Layers[0]), so this is the same fallback a
            // brand new project already gets.
            if (clipbarScene.Layers.Count == 0)
                clipbarScene.AddLayer(FrameLayerName(0), cellWidth, cellHeight);

            var tracks = new List<TimelineTrackData> { new() { Name = FrameTrackName, Clips = clips } };
            var totalFrames = Math.Max(1, clips.Count);

            return new Result(clipbarScene, tracks, totalFrames, frameRate);
        }
    }
}
