using System;
using JolieCat.Core.Documents;
using SkiaSharp;

namespace JolieCat.Core.Rendering
{
    /// <summary>
    /// Composites a <see cref="Scene"/>'s layers - respecting visibility, opacity, blend
    /// mode, and (if present and enabled) each layer's mask - onto a destination canvas,
    /// or into a brand new flattened bitmap. The one place this logic lives: shared by
    /// <c>JolieCat.UI</c>'s live canvas renderer, <see cref="Scene.MergeLayerDown"/>, and
    /// <c>JolieCat.Core</c>'s image-export pipeline, so masking behaves identically
    /// everywhere instead of drifting apart across independent copies.
    /// </summary>
    public static class SceneCompositor
    {
        /// <summary>Draws every visible layer in <paramref name="scene"/>, back-to-front,
        /// onto <paramref name="canvas"/> - the document-space compositing step shared by
        /// the live canvas (inside its own pan/zoom transform) and a flattened export
        /// (drawn 1:1 with no transform at all).</summary>
        public static void DrawLayers(SKCanvas canvas, Scene scene, int documentWidth, int documentHeight)
        {
            ArgumentNullException.ThrowIfNull(canvas);
            ArgumentNullException.ThrowIfNull(scene);

            foreach (var layer in scene.Layers)
            {
                if (!layer.IsVisible) continue;
                DrawLayer(canvas, layer, documentWidth, documentHeight);
            }
        }

        /// <summary>Draws one layer - masked, if it has an enabled mask - onto
        /// <paramref name="canvas"/> with its own opacity and blend mode.</summary>
        public static void DrawLayer(SKCanvas canvas, Layer layer, int documentWidth, int documentHeight)
        {
            using var layerPaint = new SKPaint
            {
                Color = SKColors.White.WithAlpha((byte)Math.Clamp(layer.Opacity * 255.0, 0, 255)),
                BlendMode = Layer.ToSkiaBlendMode(layer.BlendMode),
            };

            if (layer.Mask is { IsEnabled: true } mask)
            {
                // The mask has to clip this layer's own alpha *before* its opacity/blend
                // mode are applied against whatever's already on `canvas` - masking
                // directly against `canvas` would instead blend the mask against
                // whatever layers beneath this one have already painted there, which is
                // wrong the instant there's more than one layer. So this layer is first
                // composited alone on its own offscreen surface, masked there, and only
                // the masked result is drawn onto `canvas`.
                using var maskedSurface = SKSurface.Create(new SKImageInfo(documentWidth, documentHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
                var maskedCanvas = maskedSurface.Canvas;
                maskedCanvas.Clear(SKColors.Transparent);
                maskedCanvas.DrawBitmap(layer.Bitmap, 0, 0);

                // SKColorFilter.CreateLumaColor() converts whatever's drawn through it to
                // its luminance value carried in the alpha channel - exactly "read the
                // mask as grayscale" - and BlendMode.DstIn then multiplies that alpha
                // into what's already on maskedCanvas rather than painting over it. This
                // is the standard Skia/Android idiom for luma-mask alpha clipping, not a
                // bespoke technique.
                using (var maskPaint = new SKPaint { BlendMode = SKBlendMode.DstIn, ColorFilter = SKColorFilter.CreateLumaColor() })
                    maskedCanvas.DrawBitmap(mask.Bitmap, 0, 0, maskPaint);

                using var maskedImage = maskedSurface.Snapshot();
                canvas.DrawImage(maskedImage, 0, 0, layerPaint);
            }
            else
            {
                canvas.DrawBitmap(layer.Bitmap, 0, 0, layerPaint);
            }
        }

        /// <summary>Flattens every visible layer (mask, opacity, and blend mode all
        /// respected) into one brand new opaque-background-free RGBA bitmap sized exactly
        /// (<paramref name="documentWidth"/>, <paramref name="documentHeight"/>) - no
        /// checkerboard, no selection overlay, no pan/zoom: the same pixels a viewer of
        /// an exported file would see. Used by image export
        /// (<c>Export.ImageExportService</c>).</summary>
        public static SKBitmap Flatten(Scene scene, int documentWidth, int documentHeight)
        {
            ArgumentNullException.ThrowIfNull(scene);

            var result = new SKBitmap(documentWidth, documentHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(result);
            canvas.Clear(SKColors.Transparent);
            DrawLayers(canvas, scene, documentWidth, documentHeight);
            return result;
        }

        /// <summary>Renders a single layer alone - masked if applicable, at full opacity
        /// and Normal blend (neither means anything without other layers beneath it) -
        /// into a brand new bitmap the same size as <paramref name="layer"/>. Used to
        /// export one selected layer rather than the whole flattened composite.</summary>
        public static SKBitmap FlattenLayer(Layer layer)
        {
            ArgumentNullException.ThrowIfNull(layer);

            var result = new SKBitmap(layer.Bitmap.Width, layer.Bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(result);
            canvas.Clear(SKColors.Transparent);

            if (layer.Mask is { IsEnabled: true } mask)
            {
                canvas.DrawBitmap(layer.Bitmap, 0, 0);
                using var maskPaint = new SKPaint { BlendMode = SKBlendMode.DstIn, ColorFilter = SKColorFilter.CreateLumaColor() };
                canvas.DrawBitmap(mask.Bitmap, 0, 0, maskPaint);
            }
            else
            {
                canvas.DrawBitmap(layer.Bitmap, 0, 0);
            }

            return result;
        }
    }
}
