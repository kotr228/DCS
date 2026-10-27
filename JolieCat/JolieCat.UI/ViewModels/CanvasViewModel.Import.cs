using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Documents;
using JolieCat.Core.History;
using JolieCat.Shared.Enums;
using JolieCat.UI.Media;
using JolieCat.UI.ViewModels.Layers;
using JolieCat.UI.ViewModels.ToolOptions;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    public partial class CanvasViewModel
    {
        // ================= Image import =================

        private static readonly HashSet<string> SupportedImageExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".webp" };

        /// <summary>
        /// Imports every supported image among <paramref name="paths"/> (PNG/JPG/JPEG/
        /// BMP/WEBP - anything else is silently skipped) as its own new topmost layer,
        /// named after its file - the file dialog's (<c>MainViewModel.ImportImageCommand</c>)
        /// and the canvas's drag-and-drop (<see cref="Views.CanvasView"/>'s Drop handler)
        /// both funnel through here. If any image's size doesn't already match the
        /// document, the user is asked once per batch (not once per file) whether to
        /// resize the canvas to match it or fit the image(s) within the canvas as it is -
        /// see <see cref="Layers"/>' <c>ImportImage</c> for what either choice actually
        /// does. Answering "cancel" abandons the whole batch, including any files not
        /// yet processed.
        /// </summary>
        public void ImportImageFiles(IReadOnlyList<string> paths)
        {
            ArgumentNullException.ThrowIfNull(paths);

            // Only the first successfully-decoded image in the batch can trigger the
            // resize prompt/choice - every image after it always imports via "fit to the
            // canvas", which by then is whatever size this first choice left it at.
            // Re-asking (or worse, re-resizing) per file would otherwise let a later,
            // differently-sized image in the same drop/selection resize the canvas out
            // from under an image already imported earlier in this same call.
            var isFirstImage = true;

            foreach (var path in paths)
            {
                if (!SupportedImageExtensions.Contains(Path.GetExtension(path))) continue;

                try
                {
                    using var bitmap = SKBitmap.Decode(path);
                    if (bitmap is null)
                    {
                        MessageBox.Show($"Couldn't read '{Path.GetFileName(path)}' as an image.", "Import Image",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        continue;
                    }

                    var resizeDocumentToMatch = false;
                    if (isFirstImage)
                    {
                        if (bitmap.Width != Layers.DocumentWidth || bitmap.Height != Layers.DocumentHeight)
                        {
                            var choice = PromptResizeChoice(bitmap.Width, bitmap.Height, Layers.DocumentWidth, Layers.DocumentHeight);
                            if (choice is null) return; // cancelled - abandon the whole batch

                            resizeDocumentToMatch = choice.Value;
                        }

                        isFirstImage = false;
                    }

                    Layers.ImportImage(bitmap, Path.GetFileNameWithoutExtension(path), resizeDocumentToMatch);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The file vanished, is locked, or isn't readable between being
                    // dropped/picked and being decoded - reported and skipped rather than
                    // aborting the rest of the batch.
                    MessageBox.Show($"Couldn't read '{Path.GetFileName(path)}':\n{ex.Message}", "Import Image",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        /// <summary>Asks whether an about-to-be-imported image's size, which doesn't
        /// match the document's current size, should instead become the document's new
        /// size (every existing layer resized to match) or be left as the document's
        /// size is, fitting the image within it. Null means the user cancelled - abandon
        /// the import rather than guessing.</summary>
        private static bool? PromptResizeChoice(int imageWidth, int imageHeight, int canvasWidth, int canvasHeight)
        {
            var result = MessageBox.Show(
                $"This image is {imageWidth}×{imageHeight}, but the canvas is {canvasWidth}×{canvasHeight}.\n\n" +
                "Yes - resize the canvas to match this image.\n" +
                "No - keep the canvas size and fit the image within it.",
                "Import Image",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            return result switch
            {
                MessageBoxResult.Yes => true,
                MessageBoxResult.No => false,
                _ => null,
            };
        }
    }
}
