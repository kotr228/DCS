using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using JolieCat.Core.Documents;
using JolieCat.Core.Rendering;
using SkiaSharp;

namespace JolieCat.Core.Export
{
    /// <summary>
    /// Renders a <see cref="Scene"/> (or a single <see cref="Layer"/>) to a standard
    /// raster file - PNG, JPEG, or WebP - at the exact document (or layer) pixel
    /// dimensions, no more and no less: the flattened bitmap is built at
    /// (<c>documentWidth</c>, <c>documentHeight</c>) with no checkerboard, no selection
    /// overlay, and no pan/zoom transform baked in (see <see cref="SceneCompositor"/>),
    /// so there is no top-edge clipping or coordinate offset to introduce in the first
    /// place - the export path never touches the live view's pan/zoom/viewport at all.
    /// </summary>
    /// <remarks>
    /// Split into a synchronous flatten step and a separately awaitable encode/write
    /// step rather than one big async method: flattening reads the live
    /// <see cref="Scene"/>/<see cref="Layer"/> objects a caller (e.g. <c>JolieCat.UI</c>)
    /// may still be mutating from other UI interactions, so it has to run on the
    /// calling thread before anything yields; the result is a brand new, private
    /// <see cref="SKBitmap"/> no longer shared with the live scene, which is what's then
    /// safe to encode and write to disk on a background thread via
    /// <see cref="ExportAsync"/> - mirroring <see cref="Serialization.ProjectSerializer.SaveAsync"/>'s
    /// own reasoning for why its write runs on a background thread at all.
    /// </remarks>
    public static class ImageExportService
    {
        /// <summary>Flattens every visible layer of <paramref name="scene"/> - masks,
        /// opacity, and blend modes all respected - into a new bitmap sized exactly
        /// (<paramref name="documentWidth"/>, <paramref name="documentHeight"/>). Caller
        /// owns the result and must dispose it.</summary>
        public static SKBitmap FlattenScene(Scene scene, int documentWidth, int documentHeight) =>
            SceneCompositor.Flatten(scene, documentWidth, documentHeight);

        /// <summary>Renders a single layer alone (masked, if applicable) at its own
        /// size - the "export just this layer" option. Caller owns the result and must
        /// dispose it.</summary>
        public static SKBitmap FlattenLayer(Layer layer) => SceneCompositor.FlattenLayer(layer);

        /// <summary>Encodes <paramref name="bitmap"/> as <paramref name="format"/> and
        /// writes it to <paramref name="path"/>. <paramref name="quality"/> is 1-100;
        /// ignored for PNG (always lossless). JPEG has no alpha channel, so
        /// transparency is first composited onto an opaque white background - the same
        /// "flatten transparency to a background color" convention every other editor
        /// uses for this format - rather than silently writing premultiplied,
        /// alpha-darkened color values or leaving the result undefined.</summary>
        public static void Export(string path, SKBitmap bitmap, ImageExportFormat format, int quality)
        {
            ArgumentNullException.ThrowIfNull(bitmap);
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Export path cannot be empty.", nameof(path));

            var skFormat = format switch
            {
                ImageExportFormat.Png => SKEncodedImageFormat.Png,
                ImageExportFormat.Jpeg => SKEncodedImageFormat.Jpeg,
                ImageExportFormat.WebP => SKEncodedImageFormat.Webp,
                _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown export format."),
            };
            var clampedQuality = Math.Clamp(quality, 1, 100);

            SKBitmap? whiteBacked = null;
            try
            {
                var bitmapToEncode = bitmap;
                if (format == ImageExportFormat.Jpeg)
                {
                    whiteBacked = new SKBitmap(bitmap.Width, bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
                    using var whiteCanvas = new SKCanvas(whiteBacked);
                    whiteCanvas.Clear(SKColors.White);
                    whiteCanvas.DrawBitmap(bitmap, 0, 0);
                    bitmapToEncode = whiteBacked;
                }

                using var image = SKImage.FromBitmap(bitmapToEncode);
                using var data = image.Encode(skFormat, clampedQuality);
                using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write);
                data.SaveTo(fileStream);
            }
            finally
            {
                whiteBacked?.Dispose();
            }
        }

        /// <summary>Runs <see cref="Export"/> on a background thread pool thread, so a
        /// UI-thread caller can <c>await</c> it without freezing while a large image
        /// encodes and writes.</summary>
        public static Task ExportAsync(string path, SKBitmap bitmap, ImageExportFormat format, int quality) =>
            Task.Run(() => Export(path, bitmap, format, quality));

        /// <summary>Crops a rectangular region out of <paramref name="source"/> into a
        /// brand new, same-region-sized bitmap - a Sprite Sheet grid cell's own "export
        /// as a separate frame" step. Draws the whole source shifted so
        /// <paramref name="region"/>'s top-left lands at (0,0) rather than reading
        /// pixels out of it directly, so a region that runs past the source's own edges
        /// (an oversized last row/column from a margin/padding configuration that
        /// doesn't divide the document evenly) is simply clipped by the destination
        /// bitmap's own bounds instead of throwing - the same trick <c>Scene.CropLayers</c>'s
        /// own rotate-and-crop helper already uses. Caller owns the result and must
        /// dispose it.</summary>
        public static SKBitmap CropRegion(SKBitmap source, SKRectI region)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (region.Width <= 0 || region.Height <= 0)
                throw new ArgumentOutOfRangeException(nameof(region));

            var result = new SKBitmap(region.Width, region.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(result);
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(source, -region.Left, -region.Top);
            return result;
        }

        /// <summary>Slices <paramref name="scene"/>'s flattened composite into one file
        /// per <paramref name="grid"/> cell, row-major (matching <see cref="Documents.SpriteSheetGrid.EnumerateCells"/>'s
        /// own order) - <c>"{baseFileName}_000.png"</c>, <c>"_001"</c>, and so on -
        /// under <paramref name="folderPath"/> (created if it doesn't already exist).
        /// Flattens the scene once up front rather than per cell, since every cell comes
        /// from the exact same composite. A cell whose configured rectangle has
        /// collapsed to nothing (an odd margin/padding/column-count combination) is
        /// skipped, not written as a zero-size file - but still counts toward each
        /// filename's own frame index, so a later cell's number always matches its
        /// position in the grid regardless of any earlier ones skipped this way.
        /// Returns every file path actually written.</summary>
        public static IReadOnlyList<string> ExportSpriteSheetCells(
            Scene scene,
            int documentWidth,
            int documentHeight,
            Documents.SpriteSheetGrid grid,
            string folderPath,
            string baseFileName,
            ImageExportFormat format,
            int quality)
        {
            ArgumentNullException.ThrowIfNull(scene);
            ArgumentNullException.ThrowIfNull(grid);
            if (string.IsNullOrWhiteSpace(folderPath)) throw new ArgumentException("Folder path cannot be empty.", nameof(folderPath));
            if (string.IsNullOrWhiteSpace(baseFileName)) baseFileName = "frame";

            Directory.CreateDirectory(folderPath);

            var extension = format switch
            {
                ImageExportFormat.Png => "png",
                ImageExportFormat.Jpeg => "jpg",
                ImageExportFormat.WebP => "webp",
                _ => "png",
            };

            using var flattened = FlattenScene(scene, documentWidth, documentHeight);

            var written = new List<string>();
            var frameIndex = 0;

            foreach (var (_, _, rect) in grid.EnumerateCells(documentWidth, documentHeight))
            {
                var cellRegion = SKRectI.Round(rect);
                if (cellRegion.Width > 0 && cellRegion.Height > 0)
                {
                    using var cell = CropRegion(flattened, cellRegion);
                    var path = Path.Combine(folderPath, $"{baseFileName}_{frameIndex:D3}.{extension}");
                    Export(path, cell, format, quality);
                    written.Add(path);
                }

                frameIndex++;
            }

            return written;
        }

        /// <summary>Runs <see cref="ExportSpriteSheetCells"/> on a background thread
        /// pool thread, so a UI-thread caller can <c>await</c> it without freezing
        /// while every cell encodes and writes - mirrors <see cref="ExportAsync"/>'s own
        /// reasoning.</summary>
        public static Task<IReadOnlyList<string>> ExportSpriteSheetCellsAsync(
            Scene scene,
            int documentWidth,
            int documentHeight,
            Documents.SpriteSheetGrid grid,
            string folderPath,
            string baseFileName,
            ImageExportFormat format,
            int quality) =>
            Task.Run(() => ExportSpriteSheetCells(scene, documentWidth, documentHeight, grid, folderPath, baseFileName, format, quality));
    }
}
