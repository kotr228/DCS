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
        // ================= Crop =================

        private enum CropHandle { None, TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left, Inside }

        /// <summary>Device-pixel radius a click/drag has to land within a handle to hit
        /// it - converted to document space via <see cref="Zoom"/> at test time, so
        /// handles stay equally easy to grab regardless of zoom level.</summary>
        private const float HandleHitRadius = 10f;

        private bool _isCropping;
        private SKRect _cropRect;
        private CropHandle _activeCropHandle = CropHandle.None;
        private SKPoint _cropDragStart;
        private SKRect _cropDragStartRect;

        // ================= Crop =================

        /// <summary>The live crop rectangle, in document space, or null while the Crop
        /// tool isn't active - <see cref="Rendering.CanvasRenderer"/>'s own read of this
        /// drives the darkened-outside-area overlay and handle positions.</summary>
        public SKRect? CropRect => _isCropping ? _cropRect : null;

        private void StartCrop()
        {
            _cropRect = SKRect.Create(0, 0, Layers.DocumentWidth, Layers.DocumentHeight);
            _isCropping = true;
        }

        /// <summary>Applies the live crop rectangle (and the Crop tool's own straighten
        /// angle) to the whole document - see <see cref="Core.Documents.Scene.CropLayers"/>.
        /// A no-op if Crop isn't active or the rectangle has collapsed to nothing.</summary>
        public void CommitCrop()
        {
            if (!_isCropping) return;

            var rect = SKRectI.Round(_cropRect);
            if (rect.Width > 0 && rect.Height > 0)
            {
                var rotation = (float)((_toolbox.CurrentToolOptions as CropToolOptionsViewModel)?.RotationDegrees ?? 0);
                Layers.CropDocument(rect, rotation);
            }

            _isCropping = false;
            _activeCropHandle = CropHandle.None;
            RaiseInvalidate();
        }

        /// <summary>Discards the in-progress crop rectangle without changing the
        /// document - Escape's behavior for the Crop tool.</summary>
        public void CancelCrop()
        {
            _isCropping = false;
            _activeCropHandle = CropHandle.None;
            RaiseInvalidate();
        }

        private CropHandle HitTestCropHandle(SKPoint doc)
        {
            var r = _cropRect;
            var radius = HandleHitRadius / (float)Zoom;

            bool Near(float x, float y) => Math.Abs(doc.X - x) <= radius && Math.Abs(doc.Y - y) <= radius;

            if (Near(r.Left, r.Top)) return CropHandle.TopLeft;
            if (Near(r.Right, r.Top)) return CropHandle.TopRight;
            if (Near(r.Left, r.Bottom)) return CropHandle.BottomLeft;
            if (Near(r.Right, r.Bottom)) return CropHandle.BottomRight;
            if (Near(r.MidX, r.Top)) return CropHandle.Top;
            if (Near(r.MidX, r.Bottom)) return CropHandle.Bottom;
            if (Near(r.Left, r.MidY)) return CropHandle.Left;
            if (Near(r.Right, r.MidY)) return CropHandle.Right;
            return r.Contains(doc) ? CropHandle.Inside : CropHandle.None;
        }

        private void UpdateCropDrag(SKPoint doc)
        {
            if (_activeCropHandle == CropHandle.None) return;

            var dx = doc.X - _cropDragStart.X;
            var dy = doc.Y - _cropDragStart.Y;
            var start = _cropDragStartRect;

            if (_activeCropHandle == CropHandle.Inside)
            {
                _cropRect = SKRect.Create(start.Left + dx, start.Top + dy, start.Width, start.Height);
                return;
            }

            var left = start.Left;
            var top = start.Top;
            var right = start.Right;
            var bottom = start.Bottom;

            switch (_activeCropHandle)
            {
                case CropHandle.TopLeft: left += dx; top += dy; break;
                case CropHandle.Top: top += dy; break;
                case CropHandle.TopRight: right += dx; top += dy; break;
                case CropHandle.Right: right += dx; break;
                case CropHandle.BottomRight: right += dx; bottom += dy; break;
                case CropHandle.Bottom: bottom += dy; break;
                case CropHandle.BottomLeft: left += dx; bottom += dy; break;
                case CropHandle.Left: left += dx; break;
            }

            // A minimum size - not clamped to the document's own bounds, since
            // dragging a handle past the original edge is how this tool also extends
            // the canvas (padding the new area with transparency/a fully-visible
            // mask) rather than only ever being able to shrink it.
            const float minSize = 8f;
            if (right - left < minSize)
            {
                if (_activeCropHandle is CropHandle.TopLeft or CropHandle.Left or CropHandle.BottomLeft) left = right - minSize;
                else right = left + minSize;
            }
            if (bottom - top < minSize)
            {
                if (_activeCropHandle is CropHandle.TopLeft or CropHandle.Top or CropHandle.TopRight) top = bottom - minSize;
                else bottom = top + minSize;
            }

            _cropRect = ApplyCropAspectLock(SKRect.Create(left, top, right - left, bottom - top), _activeCropHandle);
        }

        /// <summary>Recomputes whichever dimension the dragged handle didn't primarily
        /// control from the other, to match the Crop tool's current aspect-ratio lock -
        /// a no-op (returns <paramref name="rect"/> unchanged) while it's Free. Keeps
        /// the rectangle anchored at whichever corner/edge is opposite the one being
        /// dragged, rather than letting it drift as the locked dimension changes.</summary>
        private SKRect ApplyCropAspectLock(SKRect rect, CropHandle handle)
        {
            if (_toolbox.CurrentToolOptions is not CropToolOptionsViewModel options) return rect;

            var ratio = options.AspectRatio == CropAspectRatioMode.Original
                ? (Layers.DocumentHeight > 0 ? (double)Layers.DocumentWidth / Layers.DocumentHeight : (double?)null)
                : options.GetFixedRatio();
            if (ratio is not { } r || r <= 0) return rect;

            var width = rect.Width;
            var height = rect.Height;

            // Dragging a purely-vertical edge (Top/Bottom) primarily changed height,
            // so width follows it; every other handle (the corners, and the purely-
            // horizontal Left/Right edges) primarily changed width, so height follows.
            if (handle is CropHandle.Top or CropHandle.Bottom)
                width = (float)(height * r);
            else
                height = (float)(width / r);

            return handle switch
            {
                CropHandle.TopLeft or CropHandle.Top or CropHandle.TopRight => SKRect.Create(rect.Right - width, rect.Bottom - height, width, height),
                CropHandle.BottomLeft or CropHandle.Left => SKRect.Create(rect.Right - width, rect.Top, width, height),
                _ => SKRect.Create(rect.Left, rect.Top, width, height),
            };
        }
    }
}
