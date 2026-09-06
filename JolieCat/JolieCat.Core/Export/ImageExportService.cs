using System;
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
    }
}
