using System;
using SkiaSharp;

namespace JolieCat.Core.Documents
{
    /// <summary>
    /// A grayscale visibility mask attached to a <see cref="Layer"/>: white paints the
    /// layer fully visible, black paints it fully invisible, and any gray in between
    /// scales its opacity proportionally - the standard non-destructive-masking
    /// convention every raster editor uses.
    /// </summary>
    /// <remarks>
    /// Backed by an ordinary <see cref="SKColorType.Rgba8888"/>/<see cref="SKAlphaType.Premul"/>
    /// bitmap - the same format every other buffer in this codebase uses - rather than a
    /// literal single-channel format (<c>Gray8</c>/<c>Alpha8</c>). This is a deliberate
    /// choice, not an oversight: every existing painting tool in <c>CanvasViewModel</c>
    /// (Brush, Pencil, Eraser, Paint Bucket, Gradient, Text) already draws correctly onto
    /// exactly this format, so a mask built from it can be painted with zero changes to
    /// any of them (see <see cref="Layer.PaintBitmap"/>/<see cref="Layer.PaintCanvas"/>).
    /// "Grayscale" is enforced at the one place it actually matters - compositing (see
    /// <see cref="Rendering.SceneCompositor"/>, which reads only the mask's luma via
    /// <see cref="SKColorFilter.CreateLumaColor"/>) - rather than by restricting what
    /// color a brush stroke can physically write, so a user who paints with a non-gray
    /// color still gets the expected result (its luminance) instead of a silently
    /// discarded stroke or a native-pixel-format edge case.
    /// </remarks>
    public sealed class LayerMask : IDisposable
    {
        private bool _disposed;

        /// <summary>Whether the mask currently affects compositing. Left in place
        /// (rather than removed) when unchecked in the Layers panel, so a user can
        /// toggle a mask off to compare against the unmasked layer without losing the
        /// mask content itself.</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>The mask's own pixel buffer - always exactly its owning
        /// <see cref="Layer"/>'s size, the same invariant every other per-layer buffer
        /// in this codebase already holds.</summary>
        public SKBitmap Bitmap { get; }

        /// <summary>A persistent canvas wrapping <see cref="Bitmap"/>, kept alive for
        /// the mask's lifetime - mirrors <see cref="Layer.Canvas"/>'s own reasoning.</summary>
        public SKCanvas Canvas { get; }

        /// <summary>Creates a mask sized to (<paramref name="width"/>, <paramref name="height"/>),
        /// starting fully opaque white - i.e. the layer starts exactly as visible as it
        /// was before the mask was added, rather than suddenly vanishing the instant one
        /// is attached.</summary>
        public LayerMask(int width, int height)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

            Bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            Canvas = new SKCanvas(Bitmap);
            Canvas.Clear(SKColors.White);
        }

        public void Dispose()
        {
            if (_disposed) return;

            Canvas.Dispose();
            Bitmap.Dispose();
            _disposed = true;
        }
    }
}
