using System;
using JolieCat.Core.Documents;
using JolieCat.Core.Rendering;
using SkiaSharp;

namespace JolieCat.UI.Rendering
{
    /// <summary>
    /// Composites the scene for the live canvas into a screen-sized offscreen surface,
    /// caching the layers below and above the active layer. While you paint, only the
    /// active layer changes, so a frame is then three blits (below, active, above)
    /// instead of compositing every layer again.
    /// </summary>
    /// <remarks>
    /// Compositing into an own surface (rather than straight onto the window) gives
    /// adjustment layers something to read back: they filter a snapshot of what has been
    /// composited so far. It also keeps the checkerboard out of the image, so blend modes
    /// never blend with it.
    ///
    /// The cache is keyed on the view (the device matrix and size), the scene, the active
    /// layer and <see cref="ViewModels.Layers.LayersViewModel.ContentVersion"/>, which
    /// changes on every history entry, undo/redo and layer property change - everything
    /// that can touch a non-active layer. While the view is moving (panning/zooming)
    /// nothing is cached, because the cache would be stale on the very next frame; the
    /// first frame with a still view builds it. The layers above can only be pre-merged
    /// when they're all plain Normal layers (source-over is associative; other blend
    /// modes and adjustment layers depend on what's beneath them), otherwise they're
    /// drawn one by one.
    /// </remarks>
    internal sealed class LayerRenderCache : IDisposable
    {
        private SKSurface? _frame;
        private SKSurface? _below;
        private SKSurface? _above;
        private bool _aboveIsCached;

        private (int Width, int Height, SKMatrix Matrix) _lastView;
        private (Scene? Scene, Layer? Active, int ActiveIndex, long Version, int Width, int Height, SKMatrix Matrix) _cachedKey;
        private bool _valid;

        /// <summary>Composites every visible layer with the document-to-screen
        /// <paramref name="matrix"/> and returns the frame surface (transparent outside
        /// the document). Valid until the next call.</summary>
        public SKSurface Compose(SKImageInfo info, SKMatrix matrix, Scene scene, int documentWidth, int documentHeight,
            long contentVersion, SKSamplingOptions sampling, Layer? previewLayer, SKBitmap? previewBitmap)
        {
            var surfaceInfo = new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            EnsureSurface(ref _frame, surfaceInfo);
            var frame = _frame!;
            var canvas = frame.Canvas;
            Func<SKImage> backdrop = frame.Snapshot;

            canvas.ResetMatrix();
            canvas.Clear(SKColors.Transparent);
            canvas.SetMatrix(matrix);

            var view = (info.Width, info.Height, matrix);
            var viewIsStill = view == _lastView;
            _lastView = view;

            var active = scene.ActiveLayer;
            var activeIndex = active is null ? -1 : IndexOf(scene, active);

            // Inside a group, the whole top-level group is the "active" part: groups
            // composite their members together, so it can't be split.
            var unitStart = activeIndex;
            var unitEnd = activeIndex;
            if (active is not null && activeIndex >= 0 && (active.IsGroup || active.ParentId is not null))
            {
                var top = scene.MemberOf(active, null) ?? active;
                (unitStart, unitEnd) = scene.BlockOf(top);
            }
            else if (activeIndex >= 0)
            {
                // A clipping group composites as one, so it can't be split either.
                (unitStart, unitEnd) = scene.ClipRange(activeIndex);
            }
            if (!viewIsStill || activeIndex < 0 || (previewLayer is not null && !ReferenceEquals(previewLayer, active)))
            {
                SceneCompositor.DrawLayers(canvas, scene, 0, scene.Layers.Count, documentWidth, documentHeight,
                    sampling, backdrop, previewLayer, previewBitmap);
                canvas.ResetMatrix();
                return frame;
            }

            var key = (scene, active, activeIndex, contentVersion, info.Width, info.Height, matrix);
            if (!_valid || key != _cachedKey)
            {
                Rebuild(surfaceInfo, scene, unitStart, unitEnd, documentWidth, documentHeight, matrix, sampling);
                _cachedKey = key;
                _valid = true;
            }

            canvas.ResetMatrix();
            canvas.DrawSurface(_below!, 0, 0);
            canvas.SetMatrix(matrix);

            SceneCompositor.DrawLayers(canvas, scene, unitStart, unitEnd + 1, documentWidth, documentHeight,
                sampling, backdrop, previewLayer, previewBitmap);

            if (_aboveIsCached)
            {
                canvas.ResetMatrix();
                canvas.DrawSurface(_above!, 0, 0);
            }
            else
            {
                SceneCompositor.DrawLayers(canvas, scene, unitEnd + 1, scene.Layers.Count, documentWidth, documentHeight, sampling, backdrop);
            }

            canvas.ResetMatrix();
            return frame;
        }

        private void Rebuild(SKImageInfo info, Scene scene, int unitStart, int unitEnd, int documentWidth, int documentHeight,
            SKMatrix matrix, SKSamplingOptions sampling)
        {
            EnsureSurface(ref _below, info);
            DrawRange(_below!, scene, 0, unitStart, documentWidth, documentHeight, matrix, sampling);

            _aboveIsCached = true;
            for (var i = unitEnd + 1; i < scene.Layers.Count; i++)
            {
                var layer = scene.Layers[i];
                if (layer.IsVisible && (layer.IsAdjustment || layer.BlendMode != Shared.Enums.BlendMode.Normal))
                {
                    _aboveIsCached = false;
                    break;
                }
            }

            if (_aboveIsCached)
            {
                EnsureSurface(ref _above, info);
                DrawRange(_above!, scene, unitEnd + 1, scene.Layers.Count, documentWidth, documentHeight, matrix, sampling);
            }
            else
            {
                _above?.Dispose();
                _above = null;
            }
        }

        private static void DrawRange(SKSurface surface, Scene scene, int from, int to, int documentWidth, int documentHeight,
            SKMatrix matrix, SKSamplingOptions sampling)
        {
            var canvas = surface.Canvas;
            canvas.ResetMatrix();
            canvas.Clear(SKColors.Transparent);
            canvas.SetMatrix(matrix);
            SceneCompositor.DrawLayers(canvas, scene, from, to, documentWidth, documentHeight, sampling, surface.Snapshot);
            canvas.ResetMatrix();
        }

        private static void EnsureSurface(ref SKSurface? surface, SKImageInfo info)
        {
            if (surface is not null)
            {
                var existing = surface.Canvas.DeviceClipBounds;
                if (existing.Width == info.Width && existing.Height == info.Height) return;
                surface.Dispose();
            }

            surface = SKSurface.Create(info);
        }

        private static int IndexOf(Scene scene, Layer layer)
        {
            for (var i = 0; i < scene.Layers.Count; i++)
                if (ReferenceEquals(scene.Layers[i], layer)) return i;
            return -1;
        }

        public void Dispose()
        {
            _frame?.Dispose();
            _below?.Dispose();
            _above?.Dispose();
            _frame = _below = _above = null;
            _valid = false;
        }
    }
}
