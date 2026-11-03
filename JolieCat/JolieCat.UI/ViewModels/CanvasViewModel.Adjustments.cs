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
        // ================= Filter / Color Adjustment Preview =================

        private Core.Documents.Layer? _adjustmentLayer;
        private SKBitmap? _adjustmentOriginalBitmap;
        private Func<SKBitmap, SKBitmap>? _adjustmentPreview;
        private SKBitmap? _adjustmentPreviewBitmap;

        // ================= Filter / Color Adjustment Preview =================

        /// <summary>The active layer's pixels exactly as they were the moment
        /// <see cref="BeginAdjustment"/> snapshotted them - what a filter/adjustment
        /// dialog's own live preview (see <see cref="UpdateAdjustmentPreview"/>) is
        /// always computed from, and what a dialog reads to build things like
        /// <see cref="Core.Adjustments.Histogram"/> from the pixels as they actually
        /// are right now, not some earlier state. Null unless an adjustment is in
        /// progress.</summary>
        public SKBitmap? AdjustmentSourceBitmap => _adjustmentOriginalBitmap;

        /// <summary>Snapshots the active layer's current pixels and starts a live
        /// preview session - called once, right before a filter/color adjustment
        /// dialog is shown. The layer's real bitmap is never touched until
        /// <see cref="CommitAdjustment"/> runs, exactly like Free Transform/Warp:
        /// <see cref="CancelAdjustment"/> (the dialog's Cancel button, or closing it
        /// without applying) is always a true no-op on the actual document.</summary>
        /// <summary>History panel name of the adjustment being previewed.</summary>
        private string? _adjustmentName;

        public void BeginAdjustment(string? historyName = null)
        {
            var layer = Layers.ActiveLayer?.Model;
            if (layer is null || layer.IsGroup) return;
            RasterizeIfText(layer);

            _adjustmentName = historyName;
            _adjustmentLayer = layer;
            _adjustmentOriginalBitmap?.Dispose();
            _adjustmentOriginalBitmap = layer.Bitmap.Copy();
            _adjustmentPreview = null;
            RaiseInvalidate();
        }

        /// <summary>Sets (or replaces) the function the live preview renders through -
        /// called every time a dialog's own slider/control-point/etc. changes, so the
        /// canvas always shows what committing right now would actually produce.
        /// <paramref name="preview"/> receives <see cref="AdjustmentSourceBitmap"/>
        /// (the original, untouched snapshot) each time it's invoked - never a
        /// previous preview's own output - so repeated adjustments of the same
        /// slider never compound against each other.</summary>
        public void UpdateAdjustmentPreview(Func<SKBitmap, SKBitmap> preview)
        {
            _adjustmentPreview = preview ?? throw new ArgumentNullException(nameof(preview));
            _previewPending = true;

            // Dragging a slider sends many changes; a slow filter on a big photo can't
            // keep up with each one. So the preview is computed once the UI is idle,
            // for the latest settings only (OK computes it right away if still pending).
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.Thread != System.Threading.Thread.CurrentThread)
            {
                FlushAdjustmentPreview();
                return;
            }
            if (_previewScheduled) return;
            _previewScheduled = true;
            dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
            {
                _previewScheduled = false;
                FlushAdjustmentPreview();
            }));
        }

        private bool _previewPending;
        private bool _previewScheduled;

        /// <summary>Computes the pending preview now (cached: the canvas repaints many
        /// times while a dialog is open, and a large blur is too slow to redo per frame).</summary>
        private void FlushAdjustmentPreview()
        {
            if (!_previewPending || _adjustmentPreview is null) return;
            _previewPending = false;
            _adjustmentPreviewBitmap?.Dispose();
            _adjustmentPreviewBitmap = _adjustmentOriginalBitmap is null
                ? null
                : LimitToSelection(_adjustmentOriginalBitmap, _adjustmentPreview(_adjustmentOriginalBitmap));
            RaiseInvalidate();
        }

        /// <summary>Keeps <paramref name="filtered"/> only inside the current selection
        /// (soft edge included) and the original everywhere else - how a filter or
        /// adjustment behaves with an active selection. Takes ownership of
        /// <paramref name="filtered"/>.</summary>
        private SKBitmap LimitToSelection(SKBitmap original, SKBitmap filtered)
        {
            var selection = Layers.Scene.Selection;
            if (!selection.HasSelection || selection.Path is not { } clipPath)
                return filtered;

            var result = original.Copy();
            using (var canvas = new SKCanvas(result))
                BlendThroughSelection(canvas, filtered);

            filtered.Dispose();
            return result;
        }

        /// <summary>Bakes whatever the live preview currently shows into the active
        /// layer's actual bitmap and records it as one undo/redo entry - the dialog's
        /// Apply/OK button. A no-op (same as <see cref="CancelAdjustment"/>) if
        /// nothing was ever previewed, so opening a dialog and immediately closing it
        /// with Apply/OK never pushes a do-nothing history entry.</summary>
        public void CommitAdjustment()
        {
            FlushAdjustmentPreview();
            if (_adjustmentLayer is null || _adjustmentOriginalBitmap is null || _adjustmentPreviewBitmap is null)
            {
                EndAdjustment();
                RaiseInvalidate();
                return;
            }

            // Bakes exactly what was previewed (same pixels, no recompute).
            var before = _adjustmentLayer.Bitmap.Pixels;
            _adjustmentLayer.Bitmap.Pixels = _adjustmentPreviewBitmap.Pixels;

            Layers.History.Push(PixelEdit(_adjustmentLayer, before, contentOnly: true), _adjustmentName ?? "Adjustment");
            EndAdjustment();
            RaiseInvalidate();
        }

        /// <summary>Discards the live preview without changing the document - the
        /// dialog's Cancel button, or closing it any other way. The active layer's
        /// actual bitmap was never touched (only ever live-previewed), so this is
        /// just dropping the preview state, not undoing a write.</summary>
        public void CancelAdjustment()
        {
            EndAdjustment();
            RaiseInvalidate();
        }

        private void EndAdjustment()
        {
            _adjustmentLayer = null;
            _adjustmentOriginalBitmap?.Dispose();
            _adjustmentOriginalBitmap = null;
            _adjustmentPreview = null;
            _previewPending = false;
            _adjustmentPreviewBitmap?.Dispose();
            _adjustmentPreviewBitmap = null;
        }
    }
}
