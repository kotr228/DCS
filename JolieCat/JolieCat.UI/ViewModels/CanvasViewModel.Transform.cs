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
        // ================= Free Transform =================

        private enum TransformHandle { None, TopLeft, TopRight, BottomLeft, BottomRight, Rotate, Inside }

        private bool _isTransforming;
        private Core.Documents.Layer? _transformLayer;
        private SKBitmap? _transformOriginalBitmap;
        private SKRect _transformOriginalBounds;
        private float _transformScaleX = 1f;
        private float _transformScaleY = 1f;
        private float _transformRotation;
        private float _transformTranslateX;
        private float _transformTranslateY;
        private TransformHandle _activeTransformHandle = TransformHandle.None;
        private SKPoint _transformDragStart;
        private float _transformDragStartScaleX;
        private float _transformDragStartScaleY;
        private float _transformDragStartRotation;
        private float _transformDragStartTranslateX;
        private float _transformDragStartTranslateY;

        // ================= Free Transform =================

        /// <summary>The active layer, while Free Transform, Warp, or a filter/color
        /// adjustment dialog is live-previewing it rather than its own committed
        /// bitmap - <see cref="Rendering.CanvasRenderer"/> substitutes
        /// <see cref="BuildLivePreviewBitmap"/>'s result for exactly this one layer
        /// (see <see cref="Rendering.SceneCompositor.DrawLayers"/>'s preview-layer
        /// overload) so it composites correctly against the layers above and below
        /// it while mid-gesture/mid-preview.</summary>
        public Core.Documents.Layer? LivePreviewLayer => _transformLayer ?? _warpLayer ?? _puppetLayer ?? _adjustmentLayer;

        /// <summary>Bakes the current Free Transform/Warp gesture's, or filter/color
        /// adjustment dialog's, live preview into a fresh bitmap for
        /// <see cref="Rendering.CanvasRenderer"/> to draw in place of
        /// <see cref="LivePreviewLayer"/>'s own committed content - null when none of
        /// them is active. Rebuilt fresh on every call rather than cached: it's only
        /// ever called once per repaint, and a repaint only happens when something (a
        /// drag, or a dialog's slider) actually changed, so there's nothing to gain
        /// from caching it.</summary>
        public SKBitmap? BuildLivePreviewBitmap()
        {
            if (_isTransforming && _transformLayer is not null && _transformOriginalBitmap is not null)
            {
                var matrix = BuildLiveTransformMatrix();
                return Core.Transform.LayerTransformer.Bake(_transformOriginalBitmap, matrix, _transformLayer.Bitmap.Width, _transformLayer.Bitmap.Height);
            }

            if (_isWarping && _warpMesh is not null && _warpOriginalBitmap is not null)
                return _warpMesh.Bake(_warpOriginalBitmap);

            if (_puppetLayer is not null)
                return BuildPuppetPreview() ?? _puppetLayer.Bitmap.Copy();

            // A copy, because the renderer disposes what this returns.
            if (_adjustmentLayer is not null && _adjustmentPreviewBitmap is not null)
                return _adjustmentPreviewBitmap.Copy();

            return null;
        }

        /// <summary>The Free Transform box's current (live) four corners, in document
        /// space - null while it isn't active. <see cref="Rendering.CanvasRenderer"/>
        /// draws the box outline and its corner/rotate handles from these.</summary>
        public SKPoint[]? TransformCorners()
        {
            if (!_isTransforming) return null;

            var matrix = BuildLiveTransformMatrix();
            var b = _transformOriginalBounds;
            return new[]
            {
                matrix.MapPoint(new SKPoint(b.Left, b.Top)),
                matrix.MapPoint(new SKPoint(b.Right, b.Top)),
                matrix.MapPoint(new SKPoint(b.Right, b.Bottom)),
                matrix.MapPoint(new SKPoint(b.Left, b.Bottom)),
            };
        }

        /// <summary>The Free Transform box's rotate handle position, in document space -
        /// a fixed screen distance beyond the box's own top edge, along whichever
        /// direction that edge currently faces (so it stays "above" the box through any
        /// rotation) - or null while Free Transform isn't active.</summary>
        public SKPoint? TransformRotateHandle()
        {
            if (!_isTransforming) return null;

            var matrix = BuildLiveTransformMatrix();
            var b = _transformOriginalBounds;
            var center = matrix.MapPoint(new SKPoint(b.MidX, b.MidY));
            var topMid = matrix.MapPoint(new SKPoint(b.MidX, b.Top));

            var dx = topMid.X - center.X;
            var dy = topMid.Y - center.Y;
            var length = MathF.Sqrt(dx * dx + dy * dy);
            if (length < 0.001f) return topMid;

            var offset = 30f / (float)Zoom;
            return new SKPoint(topMid.X + dx / length * offset, topMid.Y + dy / length * offset);
        }

        private void StartTransform()
        {
            var layer = Layers.ActiveLayer?.Model;
            if (layer is null || layer.IsGroup) return;
            RasterizeIfText(layer);

            _transformLayer = layer;
            _transformOriginalBitmap?.Dispose();
            _transformOriginalBitmap = layer.Bitmap.Copy();
            _transformOriginalBounds = SKRect.Create(0, 0, layer.Bitmap.Width, layer.Bitmap.Height);
            _transformScaleX = 1f;
            _transformScaleY = 1f;
            _transformRotation = 0f;
            _transformTranslateX = 0f;
            _transformTranslateY = 0f;
            _isTransforming = true;
        }

        /// <summary>Bakes the live scale/rotation/translation into the active layer's
        /// actual bitmap (see <see cref="Core.Transform.LayerTransformer.Bake"/>) and
        /// records it as one undo/redo entry - a no-op if Free Transform isn't active.
        /// A Smart Object layer (<see cref="Core.Documents.Layer.SmartObjectTransform"/>
        /// non-null) never bakes pixels directly - see the branch below for why.</summary>
        public void CommitTransform()
        {
            if (!_isTransforming || _transformLayer is null || _transformOriginalBitmap is null)
            {
                EndTransform();
                return;
            }

            var matrix = BuildLiveTransformMatrix();

            if (_transformLayer.SmartObjectTransform is { } smartTransform)
            {
                // Baking pixels directly here would defeat the whole point of a Smart
                // Object: repeated transforms would compound resampling loss exactly
                // like an ordinary raster layer, and SmartObjectTransform itself would
                // go stale, so a later "Edit Contents" refresh would silently discard
                // this placement. Instead, this gesture's own matrix is folded into the
                // existing placement (PostConcat - see SmartObjectTransform's own
                // remarks for why that composition, not a decomposed scale/rotate/
                // translate, is what generalizes to two gestures with different
                // pivots), and the layer is re-rendered fresh from its pristine source.
                var before = smartTransform.Matrix;
                var after = before.PostConcat(matrix);
                Layers.History.Push(new SmartObjectTransformCommand(_transformLayer, before, after, Layers.Scene));
                smartTransform.Matrix = after;
                _transformLayer.RenderSmartObject();
            }
            else if (_transformLayer is { HasSmartFilters: true, SmartFilterSource: { } source })
            {
                // Smart Filters: transform the untouched original, then run the filters again.
                var before = source.Pixels;
                using (var baked = Core.Transform.LayerTransformer.Bake(source, matrix, source.Width, source.Height))
                    source.Pixels = baked.Pixels;
                _transformLayer.RenderSmartFilters();
                Layers.History.Push(new Core.History.SmartFilterSourceCommand(Layers.Scene, _transformLayer, before, source.Pixels), "Free Transform");
            }
            else
            {
                var before = _transformLayer.Bitmap.Pixels;
                using (var baked = Core.Transform.LayerTransformer.Bake(_transformOriginalBitmap, matrix, _transformLayer.Bitmap.Width, _transformLayer.Bitmap.Height))
                    _transformLayer.Bitmap.Pixels = baked.Pixels;

                Layers.History.Push(PixelEdit(_transformLayer, before, contentOnly: true));
            }

            EndTransform();
            RaiseInvalidate();
        }

        /// <summary>Discards the in-progress transform - the active layer's actual
        /// bitmap was never touched (only ever live-previewed), so this is just
        /// dropping the preview state, not undoing a write.</summary>
        public void CancelTransform()
        {
            EndTransform();
            RaiseInvalidate();
        }

        private void EndTransform()
        {
            _isTransforming = false;
            _transformLayer = null;
            _transformOriginalBitmap?.Dispose();
            _transformOriginalBitmap = null;
            _activeTransformHandle = TransformHandle.None;
        }

        private SKMatrix BuildLiveTransformMatrix() => Core.Transform.LayerTransformer.BuildMatrix(
            new SKPoint(_transformOriginalBounds.MidX, _transformOriginalBounds.MidY),
            _transformScaleX, _transformScaleY, _transformRotation, _transformTranslateX, _transformTranslateY);

        private TransformHandle HitTestTransformHandle(SKPoint doc)
        {
            if (_transformLayer is null) return TransformHandle.None;

            var radius = HandleHitRadius / (float)Zoom;
            bool Near(SKPoint p) => SKPoint.Distance(p, doc) <= radius;

            if (TransformRotateHandle() is { } rotateHandle && Near(rotateHandle)) return TransformHandle.Rotate;

            var corners = TransformCorners();
            if (corners is not null)
            {
                if (Near(corners[0])) return TransformHandle.TopLeft;
                if (Near(corners[1])) return TransformHandle.TopRight;
                if (Near(corners[2])) return TransformHandle.BottomRight;
                if (Near(corners[3])) return TransformHandle.BottomLeft;
            }

            // Inside test: map the click back into the box's own original
            // (untransformed) space via the inverse matrix, then a plain axis-aligned
            // Contains check - correct regardless of the box's current rotation.
            var matrix = BuildLiveTransformMatrix();
            if (matrix.TryInvert(out var inverse) && _transformOriginalBounds.Contains(inverse.MapPoint(doc)))
                return TransformHandle.Inside;

            return TransformHandle.None;
        }

        private void UpdateTransformDrag(SKPoint doc)
        {
            switch (_activeTransformHandle)
            {
                case TransformHandle.Inside:
                    _transformTranslateX = _transformDragStartTranslateX + (doc.X - _transformDragStart.X);
                    _transformTranslateY = _transformDragStartTranslateY + (doc.Y - _transformDragStart.Y);
                    break;

                case TransformHandle.Rotate:
                {
                    var center = new SKPoint(
                        _transformOriginalBounds.MidX + _transformDragStartTranslateX,
                        _transformOriginalBounds.MidY + _transformDragStartTranslateY);
                    var startAngle = MathF.Atan2(_transformDragStart.Y - center.Y, _transformDragStart.X - center.X);
                    var currentAngle = MathF.Atan2(doc.Y - center.Y, doc.X - center.X);
                    _transformRotation = _transformDragStartRotation + (currentAngle - startAngle) * (180f / MathF.PI);
                    break;
                }

                // Corner handles scale uniformly, by how much further from (or closer
                // to) the box's own original center the drag point is now versus where
                // the drag started - rotation-agnostic (it's a plain radial distance
                // ratio) and the same regardless of which corner was grabbed.
                case TransformHandle.TopLeft:
                case TransformHandle.TopRight:
                case TransformHandle.BottomLeft:
                case TransformHandle.BottomRight:
                {
                    var center = new SKPoint(_transformOriginalBounds.MidX, _transformOriginalBounds.MidY);
                    var startDistance = SKPoint.Distance(center, _transformDragStart);
                    var currentDistance = SKPoint.Distance(center, doc);
                    if (startDistance > 0.001f)
                    {
                        var factor = currentDistance / startDistance;
                        _transformScaleX = _transformDragStartScaleX * factor;
                        _transformScaleY = _transformDragStartScaleY * factor;
                    }
                    break;
                }
            }
        }
    }
}
