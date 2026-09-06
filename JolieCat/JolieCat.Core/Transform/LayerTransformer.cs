using System;
using SkiaSharp;

namespace JolieCat.Core.Transform
{
    /// <summary>
    /// Bakes a Free Transform (translate/rotate/scale) into a fresh, same-size bitmap -
    /// the commit step for <c>JolieCat.UI.ViewModels.CanvasViewModel</c>'s Free
    /// Transform tool. Interaction (dragging handles, showing a live preview before the
    /// user commits) lives entirely in the UI project; this is only the final,
    /// once-per-commit pixel operation, kept in Core so it's covered by a runnable
    /// verification independent of WPF.
    /// </summary>
    public static class LayerTransformer
    {
        /// <summary>Draws <paramref name="source"/> through <paramref name="matrix"/>
        /// onto a new transparent bitmap sized (<paramref name="width"/>,
        /// <paramref name="height"/>) - the layer's own document-space size, so content
        /// transformed near an edge is clipped exactly the way every other paint
        /// operation on this layer already is, rather than silently growing the
        /// layer's own bounds.</summary>
        public static SKBitmap Bake(SKBitmap source, SKMatrix matrix, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(source);

            var result = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(result);
            canvas.Clear(SKColors.Transparent);

            using var paint = new SKPaint { IsAntialias = true };
            canvas.Save();
            canvas.SetMatrix(matrix);
            canvas.DrawBitmap(source, 0, 0, paint);
            canvas.Restore();

            return result;
        }

        /// <summary>Builds the matrix for a Free Transform gesture: scale first (around
        /// the transform's own origin), then rotate, then translate - the standard
        /// order for a handle-driven transform, where <paramref name="origin"/> is the
        /// bounding box's original center (so scaling/rotating visually pivots around
        /// the box's own center rather than the document's top-left corner).</summary>
        public static SKMatrix BuildMatrix(SKPoint origin, float scaleX, float scaleY, float rotationDegrees, float translateX, float translateY)
        {
            var matrix = SKMatrix.CreateTranslation(-origin.X, -origin.Y);
            matrix = matrix.PostConcat(SKMatrix.CreateScale(scaleX, scaleY));
            matrix = matrix.PostConcat(SKMatrix.CreateRotationDegrees(rotationDegrees));
            matrix = matrix.PostConcat(SKMatrix.CreateTranslation(origin.X + translateX, origin.Y + translateY));
            return matrix;
        }
    }
}
