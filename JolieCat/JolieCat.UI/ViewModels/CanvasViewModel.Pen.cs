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
        // ================= Pen Tool / Vector Paths =================

        private enum PenNodeDragTarget { None, Anchor, InHandle, OutHandle }

        /// <summary>Below this document-space drag distance, a Pen-tool click-drag is
        /// still treated as a plain click (the anchor stays a Corner with no handles at
        /// all) rather than the deliberate drag that defines a Smooth point's handles -
        /// avoids a barely-moved click accidentally acquiring a zero-length handle pair.</summary>
        private const float PenHandleDragThreshold = 3f;

        /// <summary>The Pen tool's transient working path - built up by clicking
        /// anchors, edited further by Path Selection/Direct Selection, and consumed by
        /// <see cref="ConvertPenPathToSelection"/>/<see cref="StrokePenPath"/>/
        /// <see cref="FillPenPath"/>. Deliberately not a persisted per-layer vector data
        /// model - transient, exactly like <see cref="LiveSelectionPath"/> is for the
        /// Lasso tools - so the existing selection/paint machinery can be reused for all
        /// three outputs instead of inventing new persistence.</summary>
        private VectorPath? _penPath;

        /// <summary>True while still actively appending anchors (from the first click
        /// until Enter, a double-click, or clicking back on the start point closes it) -
        /// false once finished, when only Path Selection/Direct Selection edit
        /// <see cref="_penPath"/> further.</summary>
        private bool _isDrawingPen;

        private bool _isDraggingPenHandle;
        private int _penPendingAnchorIndex = -1;

        /// <summary>The pointer's current document position while <see cref="_isDrawingPen"/> -
        /// draws the rubber-band segment from the last placed anchor to here, the same
        /// live-preview convention <see cref="_polygonHoverPoint"/> uses for Polygonal Lasso.</summary>
        private SKPoint _penHoverPoint;

        private PenNodeDragTarget _penNodeDragTarget = PenNodeDragTarget.None;
        private int _penNodeDragAnchorIndex = -1;
        private SKPoint _penNodeDragStart;
        private SKPoint _penNodeDragOriginalPosition;
        private SKPoint? _penNodeDragOriginalInHandle;
        private SKPoint? _penNodeDragOriginalOutHandle;

        private bool _isDraggingPenPath;
        private SKPoint _penPathDragStart;

        /// <summary>Every anchor's Position/InHandle/OutHandle exactly as they were when
        /// the Path Selection drag started - each move recomputes from this snapshot
        /// plus the drag's total delta (not incrementally from the previous move), the
        /// same drag-start-snapshot approach Free Transform's own scale/rotate drag
        /// uses, so repeated moves can't accumulate floating-point drift.</summary>
        private List<(SKPoint Position, SKPoint? InHandle, SKPoint? OutHandle)>? _penPathDragSnapshot;

        // ================= Pen Tool / Vector Paths =================

        /// <summary>The Pen tool's current working path - see <see cref="_penPath"/>'s
        /// own remarks - null when nothing has been drawn yet. <see cref="Rendering.CanvasRenderer"/>
        /// draws its Bezier outline, anchor squares, and handle lines from this while
        /// the Pen/Path Selection/Direct Selection tools are active.</summary>
        public VectorPath? PenPath => _penPath;

        /// <summary>The rubber-band segment endpoint from the last placed anchor to the
        /// current pointer position, while still actively placing anchors - null once
        /// the path is finished (or nothing is being drawn).</summary>
        public SKPoint? PenHoverPoint => _isDrawingPen ? _penHoverPoint : null;

        private static bool IsPenPathTool(ToolType type) =>
            type is ToolType.Pen or ToolType.PathSelection or ToolType.DirectSelection;

        /// <summary>Pen tool click: adds a new corner-point anchor at <paramref name="doc"/>
        /// and arms it to become a Smooth point if the click turns into a drag (see
        /// <see cref="UpdatePenHandleDrag"/>) - or, if this lands back on the path's own
        /// first anchor with at least two already placed, closes the path instead of
        /// adding another one, exactly like every mainstream vector tool's own Pen tool.
        /// <paramref name="isDoubleClick"/> finishes the path open (no closing segment) -
        /// the same "double-click to end" gesture Polygonal Lasso already uses.</summary>
        private void HandlePenPress(SKPoint doc, bool isDoubleClick)
        {
            _penPath ??= new VectorPath();

            if (isDoubleClick)
            {
                FinishDrawingPen();
                return;
            }

            var radius = HandleHitRadius / (float)Zoom;
            if (_isDrawingPen && _penPath.Anchors.Count >= 2 && SKPoint.Distance(_penPath.Anchors[0].Position, doc) <= radius)
            {
                _penPath.IsClosed = true;
                FinishDrawingPen();
                return;
            }

            _isDrawingPen = true;
            _penPath.Anchors.Add(new PathAnchor(doc));
            _penPendingAnchorIndex = _penPath.Anchors.Count - 1;
            _isDraggingPenHandle = true;
            _penHoverPoint = doc;
            RaiseInvalidate();
        }

        /// <summary>Ends the anchor-placing phase without closing the path - reached by
        /// a double-click, by clicking back on the start point (which also sets
        /// <see cref="VectorPath.IsClosed"/> first), or by <see cref="CommitPenPath"/>
        /// (Enter). <see cref="_penPath"/> itself survives - only <see cref="_isDrawingPen"/>
        /// and its own in-progress drag state end - so Path Selection/Direct Selection
        /// can keep editing it afterward.</summary>
        private void FinishDrawingPen()
        {
            _isDrawingPen = false;
            _isDraggingPenHandle = false;
            _penPendingAnchorIndex = -1;
            RaiseInvalidate();
        }

        /// <summary>Finishes the in-progress Pen path without closing it - Enter's
        /// behavior, mirroring <see cref="CommitCrop"/>/<see cref="CommitTransform"/>/
        /// <see cref="CommitWarp"/>. A no-op once the path is already finished (or
        /// nothing has been drawn), so Path Selection/Direct Selection editing the same
        /// path afterward is unaffected by an extra Enter press.</summary>
        public void CommitPenPath()
        {
            if (!_isDrawingPen) return;
            FinishDrawingPen();
        }

        /// <summary>Discards the whole working path - Escape's behavior for the Pen/
        /// Path Selection/Direct Selection tools, mirroring <see cref="CancelCrop"/>/
        /// <see cref="CancelTransform"/>/<see cref="CancelWarp"/>.</summary>
        public void CancelPenPath() => ClearPenPath();

        private void ClearPenPath()
        {
            _penPath = null;
            _isDrawingPen = false;
            _isDraggingPenHandle = false;
            _penPendingAnchorIndex = -1;
            _penNodeDragTarget = PenNodeDragTarget.None;
            _penNodeDragAnchorIndex = -1;
            _isDraggingPenPath = false;
            _penPathDragSnapshot = null;
            RaiseInvalidate();
        }

        /// <summary>Grows the anchor just placed by <see cref="HandlePenPress"/> into a
        /// Smooth point with symmetric handles once the drag has traveled far enough
        /// (<see cref="PenHandleDragThreshold"/>) to count as deliberate rather than
        /// jitter from a plain click - a release before that threshold leaves it a
        /// Corner point with no handles at all, matching every mainstream vector tool's
        /// own Pen tool.</summary>
        private void UpdatePenHandleDrag(SKPoint doc)
        {
            if (_penPath is null || _penPendingAnchorIndex < 0 || _penPendingAnchorIndex >= _penPath.Anchors.Count) return;

            var anchor = _penPath.Anchors[_penPendingAnchorIndex];
            _penHoverPoint = doc;

            if (SKPoint.Distance(anchor.Position, doc) >= PenHandleDragThreshold / (float)Zoom)
            {
                anchor.Type = AnchorPointType.Smooth;
                anchor.SetOutHandleMirrored(doc);
            }

            RaiseInvalidate();
        }

        /// <summary>Direct Selection click: hit-tests every anchor and handle of
        /// <see cref="_penPath"/> and, if one is hit, arms it to be dragged by the next
        /// <see cref="OnPointerMoved"/> calls (see <see cref="UpdatePenNodeDrag"/>) - a
        /// no-op on an empty click, leaving nothing dragging.</summary>
        private void HandleDirectSelectionPress(SKPoint doc)
        {
            var (target, index) = HitTestPenNode(doc);
            if (target == PenNodeDragTarget.None || _penPath is null) return;

            var anchor = _penPath.Anchors[index];
            _penNodeDragTarget = target;
            _penNodeDragAnchorIndex = index;
            _penNodeDragStart = doc;
            _penNodeDragOriginalPosition = anchor.Position;
            _penNodeDragOriginalInHandle = anchor.InHandle;
            _penNodeDragOriginalOutHandle = anchor.OutHandle;
        }

        /// <summary>Path Selection click: hit-tests <see cref="_penPath"/> as a whole
        /// (its filled interior if closed, else a thin band around its own stroked
        /// outline - see <see cref="HitTestPenPath"/>) and, if hit, arms the entire path
        /// to be dragged by the next <see cref="OnPointerMoved"/> calls (see
        /// <see cref="UpdatePenPathDrag"/>).</summary>
        private void HandlePathSelectionPress(SKPoint doc)
        {
            if (_penPath is null || !HitTestPenPath(doc)) return;

            _isDraggingPenPath = true;
            _penPathDragStart = doc;
            _penPathDragSnapshot = new List<(SKPoint Position, SKPoint? InHandle, SKPoint? OutHandle)>(_penPath.Anchors.Count);
            foreach (var anchor in _penPath.Anchors)
                _penPathDragSnapshot.Add((anchor.Position, anchor.InHandle, anchor.OutHandle));
        }

        /// <summary>Moves whichever anchor/handle <see cref="HandleDirectSelectionPress"/>
        /// armed to <paramref name="doc"/>. Moving the anchor itself carries both its
        /// handles along, unchanged relative to it, rather than leaving them behind;
        /// moving a single handle goes through <see cref="PathAnchor.SetOutHandleMirrored"/>/
        /// <see cref="PathAnchor.SetInHandleMirrored"/> so a Smooth point's opposite
        /// handle keeps mirroring, exactly as it would while first drawing the path.</summary>
        private void UpdatePenNodeDrag(SKPoint doc)
        {
            if (_penPath is null || _penNodeDragAnchorIndex < 0 || _penNodeDragAnchorIndex >= _penPath.Anchors.Count) return;

            var anchor = _penPath.Anchors[_penNodeDragAnchorIndex];
            var deltaX = doc.X - _penNodeDragStart.X;
            var deltaY = doc.Y - _penNodeDragStart.Y;

            switch (_penNodeDragTarget)
            {
                case PenNodeDragTarget.Anchor:
                    anchor.Position = new SKPoint(_penNodeDragOriginalPosition.X + deltaX, _penNodeDragOriginalPosition.Y + deltaY);
                    if (_penNodeDragOriginalInHandle is { } inH)
                        anchor.InHandle = new SKPoint(inH.X + deltaX, inH.Y + deltaY);
                    if (_penNodeDragOriginalOutHandle is { } outH)
                        anchor.OutHandle = new SKPoint(outH.X + deltaX, outH.Y + deltaY);
                    break;

                case PenNodeDragTarget.InHandle:
                    anchor.SetInHandleMirrored(doc);
                    break;

                case PenNodeDragTarget.OutHandle:
                    anchor.SetOutHandleMirrored(doc);
                    break;
            }

            RaiseInvalidate();
        }

        /// <summary>Translates every anchor (position and both handles alike) by the
        /// Path Selection drag's total delta from <see cref="_penPathDragSnapshot"/> -
        /// recomputed fresh from that drag-start snapshot on every call rather than
        /// incrementally, so repeated moves can't accumulate floating-point drift (the
        /// same approach Free Transform's own scale/rotate drag uses).</summary>
        private void UpdatePenPathDrag(SKPoint doc)
        {
            if (_penPath is null || _penPathDragSnapshot is null) return;

            var deltaX = doc.X - _penPathDragStart.X;
            var deltaY = doc.Y - _penPathDragStart.Y;

            for (var i = 0; i < _penPath.Anchors.Count && i < _penPathDragSnapshot.Count; i++)
            {
                var (position, inHandle, outHandle) = _penPathDragSnapshot[i];
                var anchor = _penPath.Anchors[i];
                anchor.Position = new SKPoint(position.X + deltaX, position.Y + deltaY);
                anchor.InHandle = inHandle is { } inH ? new SKPoint(inH.X + deltaX, inH.Y + deltaY) : null;
                anchor.OutHandle = outHandle is { } outH ? new SKPoint(outH.X + deltaX, outH.Y + deltaY) : null;
            }

            RaiseInvalidate();
        }

        /// <summary>Hit-tests every anchor and handle of <see cref="_penPath"/> against
        /// <paramref name="doc"/>, within <see cref="HandleHitRadius"/> (converted to
        /// document space via <see cref="Zoom"/>, matching Crop/Free Transform's own
        /// handle hit tests) - anchors take priority over handles when both are within
        /// radius, since an anchor and its own handle can land very close together.</summary>
        private (PenNodeDragTarget Target, int AnchorIndex) HitTestPenNode(SKPoint doc)
        {
            if (_penPath is null) return (PenNodeDragTarget.None, -1);

            var radius = HandleHitRadius / (float)Zoom;
            for (var i = 0; i < _penPath.Anchors.Count; i++)
            {
                var anchor = _penPath.Anchors[i];
                if (SKPoint.Distance(anchor.Position, doc) <= radius) return (PenNodeDragTarget.Anchor, i);
            }
            for (var i = 0; i < _penPath.Anchors.Count; i++)
            {
                var anchor = _penPath.Anchors[i];
                if (anchor.OutHandle is { } outH && SKPoint.Distance(outH, doc) <= radius) return (PenNodeDragTarget.OutHandle, i);
                if (anchor.InHandle is { } inH && SKPoint.Distance(inH, doc) <= radius) return (PenNodeDragTarget.InHandle, i);
            }

            return (PenNodeDragTarget.None, -1);
        }

        /// <summary>Whether <paramref name="doc"/> lands "on" <see cref="_penPath"/> -
        /// inside it for a closed path, or within a thin band of its own stroked
        /// outline for an open one (built via <see cref="SKPaint.GetFillPath(SKPath, SKPath)"/>,
        /// widening the path's centerline into a fillable/hit-testable region rather
        /// than needing a bespoke point-to-segment distance test) - the Path Selection
        /// tool's own hit test for "drag the whole path" versus "click on empty canvas,
        /// do nothing".</summary>
        private bool HitTestPenPath(SKPoint doc)
        {
            if (_penPath is null || _penPath.Anchors.Count == 0) return false;

            using var skPath = _penPath.ToSKPath();
            if (_penPath.IsClosed && skPath.Contains(doc.X, doc.Y)) return true;

            var radius = Math.Max(HandleHitRadius / (float)Zoom, 4f);
            using var hitTestStrokePaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = radius * 2 };
            using var strokedRegion = new SKPath();
            hitTestStrokePaint.GetFillPath(skPath, strokedRegion);
            return strokedRegion.Contains(doc.X, doc.Y);
        }

        /// <summary>"Convert to Selection": rasterizes <see cref="_penPath"/> into the
        /// scene's pixel selection via <see cref="Core.Documents.Selection.SetPath"/> -
        /// the same machinery every marquee/lasso tool already commits its own path
        /// into, so painting/erasing/filling immediately respects it exactly like any
        /// other selection. A no-op with fewer than two anchors (nothing to enclose).</summary>
        [RelayCommand]
        private void ConvertPenPathToSelection()
        {
            if (_penPath is null || _penPath.Anchors.Count < 2) return;

            var skPath = _penPath.ToSKPath();
            Layers.Scene.Selection.SetPath(skPath, Layers.DocumentWidth, Layers.DocumentHeight);
            RaiseInvalidate();
        }

        /// <summary>"Stroke Path": paints <see cref="_penPath"/>'s own outline onto the
        /// active layer with the current <see cref="PrimaryColor"/> and the Pen tool's
        /// Stroke Width option - clipped to the scene's selection like every other
        /// paint operation (<see cref="WithSelectionClip"/>). Recorded as one undo/redo
        /// entry. A no-op with fewer than two anchors, no active layer, or a locked one.</summary>
        [RelayCommand]
        private void StrokePenPath()
        {
            var layer = Layers.ActiveLayer?.Model;
            if (_penPath is null || _penPath.Anchors.Count < 2 || layer is null || layer.IsLocked) return;

            var options = _toolbox.CurrentToolOptions as PenToolOptionsViewModel;
            var strokeWidth = Math.Max(1f, (float)(options?.StrokeWidth ?? 2));

            using var paint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = strokeWidth,
                Color = BrushColor,
                IsAntialias = true,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
            };

            var before = layer.PaintBitmap.Pixels;
            using (var skPath = _penPath.ToSKPath())
                WithSelectionClip(layer.PaintCanvas, canvas => canvas.DrawPath(skPath, paint));

            Layers.History.Push(PixelEdit(layer, before));
            RaiseInvalidate();
        }

        /// <summary>"Fill Path": fills <see cref="_penPath"/>'s enclosed area (Skia
        /// implicitly closes an open path for filling, same as every vector tool's own
        /// Fill Path) with the current <see cref="PrimaryColor"/> - clipped to the
        /// scene's selection like every other paint operation. Recorded as one undo/redo
        /// entry. A no-op with fewer than three anchors (no enclosed area), no active
        /// layer, or a locked one.</summary>
        [RelayCommand]
        private void FillPenPath()
        {
            var layer = Layers.ActiveLayer?.Model;
            if (_penPath is null || _penPath.Anchors.Count < 3 || layer is null || layer.IsLocked) return;

            using var paint = new SKPaint { Style = SKPaintStyle.Fill, Color = BrushColor, IsAntialias = true };

            var before = layer.PaintBitmap.Pixels;
            using (var skPath = _penPath.ToSKPath())
                WithSelectionClip(layer.PaintCanvas, canvas => canvas.DrawPath(skPath, paint));

            Layers.History.Push(PixelEdit(layer, before));
            RaiseInvalidate();
        }
    }
}
