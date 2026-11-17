using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using JolieCat.Core.Filters;
using JolieCat.Core.History;
using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels.ToolOptions;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    /// <summary>
    /// Tools added on top of the original set: Move, Zoom, Shape, and the dab-based
    /// retouching brushes (Clone Stamp, Healing Brush, Blur, Sharpen, Sponge, Dodge,
    /// Burn). The retouching brushes reuse the Brush's stroke machinery (spacing,
    /// hardness, pen pressure, selection clipping, one undo step per stroke) and only
    /// change what each dab does.
    /// </summary>
    public partial class CanvasViewModel
    {
        /// <summary>Short hint for the status bar about what the active tool needs
        /// (e.g. "Alt+click to set the clone source").</summary>
        [ObservableProperty]
        private string toolHintText = string.Empty;

        /// <summary>Last pointer position in document space, for the brush outline and
        /// clone-source marker. Null while the pointer is outside the canvas.</summary>
        public SKPoint? HoverDocumentPoint { get; private set; }

        public void ClearHover()
        {
            HoverDocumentPoint = null;
            CursorPositionText = string.Empty;
            RaiseInvalidate();
        }

        internal static bool IsRetouchTool(ToolType type) => type is
            ToolType.CloneStamp or ToolType.HealingBrush or ToolType.Blur or
            ToolType.Sharpen or ToolType.Sponge or ToolType.Dodge or ToolType.Burn or
            ToolType.Smudge or ToolType.HistoryBrush or ToolType.BackgroundEraser or
            ToolType.PatternStamp or ToolType.MixerBrush;

        internal static bool IsBrushLikeTool(ToolType type) =>
            type is ToolType.Brush or ToolType.Pencil or ToolType.Eraser or ToolType.SpotHealing || IsRetouchTool(type);

        /// <summary>Brush diameter in document pixels, or null if the active tool has no brush.</summary>
        public float? BrushOutlineDiameter =>
            IsBrushLikeTool(_toolbox.ActiveTool.Type) && _toolbox.CurrentToolOptions is PaintToolOptionsViewModel options
                ? (float)Math.Max(1, options.Size)
                : _toolbox.ActiveTool.Type == ToolType.QuickSelection && SelectionOptions is { } selectionOptions
                    ? (float)Math.Max(1, selectionOptions.BrushSize)
                    : null;

        /// <summary>The brush tip's shape for the outline cursor: angle (degrees) and
        /// roundness (0..1); a plain circle for tools without tip settings.</summary>
        public (float Angle, float Roundness) BrushOutlineShape =>
            _toolbox.CurrentToolOptions is PaintToolOptionsViewModel options && !IsRetouchTool(_toolbox.ActiveTool.Type)
                ? ((float)options.Angle, (float)Math.Clamp(options.Roundness / 100, 0.02, 1))
                : (0f, 1f);

        /// <summary>Handles presses for the tools in this file. Returns true if the
        /// press was consumed (the caller's main tool switch is then skipped).</summary>
        private bool TryHandleExtendedToolPress(SKPoint devicePoint, SKPoint doc, Core.Documents.Layer? activeLayer)
        {
            var type = _toolbox.ActiveTool.Type;
            var alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;

            switch (type)
            {
                case ToolType.Patch:
                case ToolType.ContentAwareMove:
                    return BeginPatch(doc, activeLayer);

                case ToolType.MagicEraser:
                    MagicErase(activeLayer, doc);
                    return true;

                case ToolType.RedEye:
                    FixRedEye(activeLayer, doc);
                    return true;

                case ToolType.ColorSampler:
                    PressColorSampler(doc);
                    return true;

                case ToolType.Ruler:
                    return PressRuler(doc);

                case ToolType.ObjectSelection:
                    return PressObjectSelection(doc);

                case ToolType.PuppetWarp:
                    return PressPuppetWarp(doc);

                case ToolType.PerspectiveCrop:
                    return BeginPerspectiveDrag(doc);

                case ToolType.Zoom:
                {
                    var target = alt ? NextZoomStep(-1) : NextZoomStep(+1);
                    ZoomAtPoint(devicePoint, target);
                    return true;
                }

                case ToolType.Move:
                    if (activeLayer is null) return true;
                    if (AnimatedMove?.BeginAnimatedMove(activeLayer) == true)
                    {
                        _animatedMoveStart = doc;
                        ToolHintText = "Dragging sets the layer's position at this frame (a keyframe)";
                        return true;
                    }
                    if (LayersToMoveTogether(activeLayer) is { } many)
                    {
                        if (many.Count > 0) BeginMoveMany(many, doc);
                    }
                    else if (!activeLayer.IsLocked
                             && !(Layers.Scene.Selection.HasSelection && BlockedBySmartFilters(activeLayer)))
                    {
                        BeginMove(activeLayer, doc);
                    }
                    return true;

                case ToolType.Gradient:
                    if (activeLayer is not null && !activeLayer.IsLocked)
                        BeginGradient(doc);
                    return true;

                case ToolType.CanvasRotate:
                    BeginRotateView(devicePoint);
                    return true;

                case ToolType.QuickSelection:
                    if (activeLayer is not null)
                        BeginQuickSelection(activeLayer, doc);
                    return true;

                case ToolType.SpotHealing:
                    if (activeLayer is not null && !activeLayer.IsLocked && !activeLayer.IsAdjustment)
                        BeginSpotHealing(doc);
                    return true;

                case ToolType.Shape:
                    if (activeLayer is not null && !activeLayer.IsLocked)
                    {
                        _isDrawingShape = true;
                        _shapeStart = doc;
                        _shapeEnd = doc;
                    }
                    return true;

                case ToolType.CloneStamp or ToolType.HealingBrush:
                    if (alt)
                    {
                        _cloneSource = doc;
                        _cloneOffset = null; // re-aligned on the next stroke
                        ToolHintText = "Clone source set - now paint where you want the copy";
                        RaiseInvalidate();
                        return true;
                    }

                    if (_cloneSource is null)
                    {
                        ToolHintText = "Alt+click to set the clone source first";
                        return true;
                    }

                    if (activeLayer is null || activeLayer.IsLocked) return true;

                    // "Aligned" cloning: the offset is fixed on the first stroke after
                    // picking a source and kept for every later stroke.
                    _cloneOffset ??= new SKPoint(doc.X - _cloneSource.Value.X, doc.Y - _cloneSource.Value.Y);
                    _cloneSnapshot?.Dispose();
                    _cloneSnapshot = activeLayer.PaintBitmap.Copy();
                    ToolHintText = string.Empty;
                    return false; // fall through to the shared brush stroke start

                default:
                    return false;
            }
        }

        /// <summary>Pointer moves for this file's tools. Returns true if consumed.</summary>
        private bool TryHandleExtendedToolMove(SKPoint doc)
        {
            if (MoveColorSampler(doc)) return true;
            if (MoveRuler(doc)) return true;
            if (MoveObjectSelection(doc)) return true;
            if (UpdateTextDrag(doc)) return true;
            if (_animatedMoveStart is { } moveStart && AnimatedMove is { } animated)
            {
                animated.UpdateAnimatedMove(doc.X - moveStart.X, doc.Y - moveStart.Y);
                return true;
            }
            if (MovePuppetPin(doc)) return true;
            if (UpdatePatch(doc)) return true;
            if (UpdatePerspectiveDrag(doc)) return true;

            if (_isDraggingGradient)
            {
                UpdateGradient(doc);
                return true;
            }

            if (_isQuickSelecting)
            {
                UpdateQuickSelection(doc);
                return true;
            }

            if (_spotHealPath is not null)
            {
                UpdateSpotHealing(doc);
                return true;
            }

            if (_isDrawingShape)
            {
                _shapeEnd = ConstrainShapeEnd(_shapeStart, doc);
                RaiseInvalidate();
                return true;
            }

            if (_isMoving)
            {
                UpdateMove(doc);
                return true;
            }

            if (_movingMany is not null)
            {
                UpdateMoveMany(doc);
                return true;
            }

            return false;
        }

        private void HandleExtendedToolRelease()
        {
            ReleaseColorSampler();
            ReleaseRuler();
            ReleaseObjectSelection();
            EndTextDrag();
            _animatedMoveStart = null;
            ReleasePuppetPin();
            if (_isDraggingGradient)
                CommitGradient();

            _isRotatingView = false;

            if (_isQuickSelecting)
                CommitQuickSelection();

            CommitSpotHealing();

            if (_isDrawingShape)
            {
                _isDrawingShape = false;
                CommitShape();
            }

            if (_isMoving)
                CommitMove();

            CommitMoveMany();

            _cloneSnapshot?.Dispose();
            _cloneSnapshot = null;
        }

        private void CancelExtendedTools()
        {
            _isDraggingGradient = false;
            CancelQuickSelection();
            CancelSpotHealing();
            _isDrawingShape = false;
            if (_isMoving) CommitMove();
            CommitMoveMany();
            _cloneSnapshot?.Dispose();
            _cloneSnapshot = null;
        }

        // ================= Zoom tool =================

        private double NextZoomStep(int direction)
        {
            if (direction > 0)
            {
                foreach (var step in ZoomSteps)
                    if (step > Zoom + 1e-6) return step;
                return MaxZoom;
            }

            for (var i = ZoomSteps.Length - 1; i >= 0; i--)
                if (ZoomSteps[i] < Zoom - 1e-6) return ZoomSteps[i];
            return MinZoom;
        }

        private void ZoomAtPoint(SKPoint devicePoint, double newZoom)
        {
            MarkViewChangedByUser();
            newZoom = Math.Clamp(newZoom, MinZoom, MaxZoom);
            var doc = ToDocumentSpace(devicePoint);
            var screen = ToUnrotatedScreen(devicePoint);
            PanX = SnapToPixel(screen.X - doc.X * newZoom);
            PanY = SnapToPixel(screen.Y - doc.Y * newZoom);
            Zoom = newZoom;
        }

        // ================= Move tool =================

        private bool _isMoving;
        private Core.Documents.Layer? _moveLayer;
        private SKBitmap? _moveSnapshot;
        private SKBitmap? _moveBase;
        private SKBitmap? _moveFloating;
        private SKPath? _moveSelectionPath;
        private SKPoint _moveStart;
        private SKPointI _moveDelta;
        private SKRect _moveContentBounds;

        /// <summary>Starts dragging the active layer's content - or only the selected
        /// pixels, if there is a selection (the hole they leave becomes transparent).</summary>
        private void BeginMove(Core.Documents.Layer layer, SKPoint doc)
        {
            _isMoving = true;
            _moveLayer = layer;
            _moveStart = doc;
            _moveDelta = default;
            _moveSnapshot = layer.PaintBitmap.Copy();

            var selection = Layers.Scene.Selection;
            // Text moves as a whole (it stays editable), whatever is selected.
            if (!layer.IsText && selection.HasSelection && selection.Path is { } path)
            {
                _moveSelectionPath = new SKPath(path);

                _moveBase = _moveSnapshot.Copy();
                using (var baseCanvas = new SKCanvas(_moveBase))
                using (var clear = new SKPaint { BlendMode = SKBlendMode.Clear })
                {
                    baseCanvas.ClipPath(path, antialias: true);
                    baseCanvas.DrawPaint(clear);
                }

                _moveFloating = new SKBitmap(_moveSnapshot.Info);
                using var floatCanvas = new SKCanvas(_moveFloating);
                floatCanvas.Clear(SKColors.Transparent);
                floatCanvas.ClipPath(path, antialias: true);
                floatCanvas.DrawBitmap(_moveSnapshot, 0, 0);
            }
            else
            {
                _moveSelectionPath = null;
                _moveBase = null;
                _moveFloating = _moveSnapshot.Copy();
            }

            // What's being moved, for snapping its edges to guides.
            _moveContentBounds = _moveSelectionPath is { } moving
                ? moving.Bounds
                : Core.Rendering.SceneCompositor.ContentBounds(_moveFloating);
        }

        private void UpdateMove(SKPoint doc)
        {
            if (_moveLayer is null || _moveFloating is null) return;

            // Whole-pixel steps keep pixel art crisp (no resampling blur).
            var delta = new SKPointI((int)MathF.Round(doc.X - _moveStart.X), (int)MathF.Round(doc.Y - _moveStart.Y));
            delta = SnapMoveDelta(_moveContentBounds, delta);
            if (delta == _moveDelta) return;
            _moveDelta = delta;

            var canvas = _moveLayer.PaintCanvas;
            canvas.Clear(_moveLayer.IsMaskActive && _moveLayer.Mask is not null ? SKColors.White : SKColors.Transparent);
            if (_moveBase is not null) canvas.DrawBitmap(_moveBase, 0, 0);
            canvas.DrawBitmap(_moveFloating, delta.X, delta.Y);

            if (_moveSelectionPath is not null)
            {
                var moved = new SKPath(_moveSelectionPath);
                moved.Transform(SKMatrix.CreateTranslation(delta.X, delta.Y));
                LiveSelectionPath = moved;
            }

            RaiseInvalidate();
        }

        private void CommitMove()
        {
            _isMoving = false;

            if (_moveLayer is { Shape: { } shape } shapeLayer && _moveDelta != default)
            {
                var movedShape = shape.MovedBy(_moveDelta.X, _moveDelta.Y);
                shapeLayer.Shape = movedShape;
                shapeLayer.RenderShape();
                Layers.History.Push(new Core.History.ShapeChangeCommand(Layers.Scene, shapeLayer, shape, movedShape), "Move");
            }
            else if (_moveLayer is { Text: { } text } textLayer && _moveDelta != default)
            {
                var moved = text.MovedBy(_moveDelta.X, _moveDelta.Y);
                textLayer.Text = moved;
                textLayer.RenderText();
                Layers.History.Push(new Core.History.TextChangeCommand(Layers.Scene, textLayer, text, moved), "Move");
            }
            else if (_moveLayer is { HasSmartFilters: true, IsMaskActive: false } smartLayer && _moveDelta != default)
            {
                // The original moves and the filters run again (so blur edges etc. follow).
                Layers.History.Push(ShiftSmartFilterSource(smartLayer, _moveDelta), "Move");
            }
            else if (_moveLayer is not null && _moveSnapshot is not null && _moveDelta != default)
            {
                Layers.History.Push(PixelEdit(_moveLayer, _moveSnapshot.Pixels));

                if (_moveSelectionPath is not null)
                {
                    var moved = new SKPath(_moveSelectionPath);
                    moved.Transform(SKMatrix.CreateTranslation(_moveDelta.X, _moveDelta.Y));
                    Layers.Scene.Selection.SetPath(moved, Layers.DocumentWidth, Layers.DocumentHeight);
                }
            }

            LiveSelectionPath = null;
            _moveSnapshot?.Dispose();
            _moveBase?.Dispose();
            _moveFloating?.Dispose();
            _moveSelectionPath?.Dispose();
            _moveSnapshot = _moveBase = _moveFloating = null;
            _moveSelectionPath = null;
            _moveLayer = null;
            RaiseInvalidate();
        }

        // ================= Shape tool =================

        private bool _isDrawingShape;
        private SKPoint _shapeStart;
        private SKPoint _shapeEnd;

        /// <summary>Shift: square/circle for box shapes, 45° steps for lines.</summary>
        private SKPoint ConstrainShapeEnd(SKPoint start, SKPoint end)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0) return end;

            var dx = end.X - start.X;
            var dy = end.Y - start.Y;

            if ((_toolbox.CurrentToolOptions as ShapeToolOptionsViewModel)?.Kind == ShapeKind.Line)
            {
                var angle = MathF.Atan2(dy, dx);
                var snapped = MathF.Round(angle / (MathF.PI / 4)) * (MathF.PI / 4);
                var length = MathF.Sqrt(dx * dx + dy * dy);
                return new SKPoint(start.X + MathF.Cos(snapped) * length, start.Y + MathF.Sin(snapped) * length);
            }

            var side = Math.Max(Math.Abs(dx), Math.Abs(dy));
            return new SKPoint(start.X + Math.Sign(dx) * side, start.Y + Math.Sign(dy) * side);
        }

        /// <summary>Draws the in-progress shape - called by the renderer for the live
        /// preview and by <see cref="CommitShape"/> for the final pixels.</summary>
        public void DrawLiveShape(SKCanvas canvas)
        {
            DrawLiveGradient(canvas);
            if (!_isDrawingShape) return;
            DrawShape(canvas, _shapeStart, _shapeEnd);
        }

        private void DrawShape(SKCanvas canvas, SKPoint start, SKPoint end)
        {
            var options = _toolbox.CurrentToolOptions as ShapeToolOptionsViewModel ?? new ShapeToolOptionsViewModel();
            options.ToShape(start, end).Render(canvas);
        }

        private void CommitShape()
        {
            var dx = _shapeEnd.X - _shapeStart.X;
            var dy = _shapeEnd.Y - _shapeStart.Y;
            if (Math.Abs(dx) < 1 && Math.Abs(dy) < 1) { RaiseInvalidate(); return; }

            var options = _toolbox.CurrentToolOptions as ShapeToolOptionsViewModel ?? new ShapeToolOptionsViewModel();
            if (options.Mode == ShapeMode.Shape && !IsQuickMask)
            {
                // An editable shape layer above the active layer.
                Layers.AddShapeLayer(options.ToShape(_shapeStart, _shapeEnd));
                RaiseInvalidate();
                return;
            }

            var layer = Layers.ActiveLayer?.Model;
            if (layer is null || layer.IsLocked && layer.PaintRedirect is null) return;
            if (!layer.PaintsMask) RasterizeIfText(layer);
            var before = layer.PaintBitmap.Pixels;
            WithSelectionClip(layer.PaintCanvas, canvas => DrawShape(canvas, _shapeStart, _shapeEnd));
            Layers.History.Push(PixelEdit(layer, before));
            RaiseInvalidate();
        }

        // ================= Retouching brushes =================

        private SKPoint? _cloneSource;
        private SKPoint? _cloneOffset;
        private SKBitmap? _cloneSnapshot;

        /// <summary>Where the clone source currently sits relative to the pointer -
        /// drawn as a crosshair by the renderer while a clone tool is active.</summary>
        public SKPoint? CloneSourceMarker
        {
            get
            {
                if (_toolbox.ActiveTool.Type is not (ToolType.CloneStamp or ToolType.HealingBrush)) return null;
                if (_cloneSource is null) return null;
                if (_cloneOffset is { } offset && HoverDocumentPoint is { } hover)
                    return new SKPoint(hover.X - offset.X, hover.Y - offset.Y);
                return _cloneSource;
            }
        }

        /// <summary>One retouching dab: builds a processed copy of the area under the
        /// brush, then blends it back through the same soft round mask the Brush uses
        /// (<paramref name="maskPaint"/>, drawn with DstIn), so hardness, opacity and pen
        /// pressure all apply the usual way.</summary>
        private void DrawRetouchDab(SKCanvas canvas, SKPoint center, float radius, float scale, SKPaint maskPaint)
        {
            var layer = _strokeLayer;
            if (layer is null) return;

            var bitmap = layer.PaintBitmap;
            var r = radius * scale;
            var margin = (int)MathF.Ceiling(r * 0.5f) + 2;
            var area = SKRectI.Ceiling(new SKRect(center.X - r - margin, center.Y - r - margin, center.X + r + margin, center.Y + r + margin));
            area.Intersect(new SKRectI(0, 0, bitmap.Width, bitmap.Height));
            if (area.Width <= 0 || area.Height <= 0) return;

            var type = _toolbox.ActiveTool.Type;
            using var patch = BuildRetouchPatch(type, bitmap, area, r, center);
            _lastDabCenter = center;
            if (patch is null) return;

            var dabBounds = new SKRect(center.X - r - 1, center.Y - r - 1, center.X + r + 1, center.Y + r + 1);

            // History Brush and Smudge replace what's there (so they can bring back
            // transparency too): first take the brush's share of the old pixels away...
            var replaces = type is ToolType.HistoryBrush or ToolType.Smudge;
            if (replaces)
            {
                var mode = maskPaint.BlendMode;
                maskPaint.BlendMode = SKBlendMode.DstOut;
                canvas.Save();
                canvas.Translate(center.X, center.Y);
                canvas.Scale(scale);
                canvas.DrawCircle(0, 0, radius, maskPaint);
                canvas.Restore();
                maskPaint.BlendMode = mode;
            }

            // ...then add the new ones in (Plus); the Background Eraser's patch is an
            // erase mask instead, applied with DstOut.
            using var layerPaint = new SKPaint
            {
                BlendMode = replaces ? SKBlendMode.Plus : type == ToolType.BackgroundEraser ? SKBlendMode.DstOut : SKBlendMode.SrcOver,
            };
            canvas.SaveLayer(dabBounds, layerPaint);

            using (var circle = new SKPath())
            {
                circle.AddCircle(center.X, center.Y, r);
                canvas.Save();
                canvas.ClipPath(circle, antialias: true);
                canvas.DrawBitmap(patch, area.Left, area.Top);
                canvas.Restore();
            }

            var originalMode = maskPaint.BlendMode;
            maskPaint.BlendMode = SKBlendMode.DstIn;
            canvas.Save();
            canvas.Translate(center.X, center.Y);
            canvas.Scale(scale);
            canvas.DrawCircle(0, 0, radius, maskPaint);
            canvas.Restore();
            maskPaint.BlendMode = originalMode;

            canvas.Restore();
        }

        private SKBitmap? BuildRetouchPatch(ToolType type, SKBitmap layerBitmap, SKRectI area, float radius, SKPoint center)
        {
            switch (type)
            {
                case ToolType.Smudge:
                {
                    // Pixels from where the previous dab was, dragged to here.
                    if (_lastDabCenter is not { } previous) return null;
                    var copy = new SKBitmap(area.Width, area.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
                    using var c = new SKCanvas(copy);
                    c.Clear(SKColors.Transparent);
                    c.DrawBitmap(layerBitmap, -area.Left + (center.X - previous.X), -area.Top + (center.Y - previous.Y));
                    return copy;
                }

                case ToolType.HistoryBrush:
                    return _historySource is null ? null : CopyArea(_historySource, area);

                case ToolType.PatternStamp:
                {
                    // Anchored to the document, so neighboring strokes line up.
                    var name = (_toolbox.CurrentToolOptions as PaintToolOptionsViewModel)?.PatternName;
                    using var tile = Core.Effects.PatternTiles.Create(name, BrushColor);
                    var patch = new SKBitmap(area.Width, area.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
                    using var c = new SKCanvas(patch);
                    using var shader = SKShader.CreateBitmap(tile, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat,
                        SKMatrix.CreateTranslation(-area.Left, -area.Top));
                    using var paint = new SKPaint { Shader = shader };
                    c.Clear(SKColors.Transparent);
                    c.DrawRect(0, 0, area.Width, area.Height, paint);
                    return patch;
                }

                case ToolType.MixerBrush:
                    return MixerPatch(layerBitmap, area, center, radius);

                case ToolType.BackgroundEraser:
                {
                    var x = Math.Clamp((int)center.X, 0, layerBitmap.Width - 1);
                    var y = Math.Clamp((int)center.Y, 0, layerBitmap.Height - 1);
                    var sample = layerBitmap.GetPixel(x, y);
                    if (sample.Alpha == 0) return null;
                    var tolerance = (float)((_toolbox.CurrentToolOptions as PaintToolOptionsViewModel)?.Tolerance ?? 50);
                    using var source = CopyArea(layerBitmap, area);
                    return RetouchOps.EraseMask(source, sample, tolerance);
                }

                case ToolType.CloneStamp:
                case ToolType.HealingBrush:
                {
                    if (_cloneSnapshot is null || _cloneOffset is not { } offset) return null;

                    var clone = new SKBitmap(area.Width, area.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
                    using (var c = new SKCanvas(clone))
                    {
                        c.Clear(SKColors.Transparent);
                        c.DrawBitmap(_cloneSnapshot, -(area.Left - offset.X), -(area.Top - offset.Y));
                    }

                    if (type == ToolType.HealingBrush)
                    {
                        using var target = CopyArea(layerBitmap, area);
                        MatchMeanColor(clone, target);
                    }

                    return clone;
                }

                case ToolType.Blur:
                {
                    using var source = CopyArea(layerBitmap, area);
                    return BlurFilter.Apply(source, BlurType.Gaussian, Math.Max(1f, radius * 0.2f));
                }

                case ToolType.Sharpen:
                {
                    using var source = CopyArea(layerBitmap, area);
                    return SharpenFilter.Apply(source, 0.4f);
                }

                case ToolType.Dodge:
                    return MapArea(layerBitmap, area, c => new SKColor(
                        (byte)(c.Red + (255 - c.Red) * 0.2f), (byte)(c.Green + (255 - c.Green) * 0.2f),
                        (byte)(c.Blue + (255 - c.Blue) * 0.2f), c.Alpha));

                case ToolType.Burn:
                    return MapArea(layerBitmap, area, c => new SKColor(
                        (byte)(c.Red * 0.8f), (byte)(c.Green * 0.8f), (byte)(c.Blue * 0.8f), c.Alpha));

                case ToolType.Sponge:
                    return MapArea(layerBitmap, area, c =>
                    {
                        var y = 0.2126f * c.Red + 0.7152f * c.Green + 0.0722f * c.Blue;
                        byte Toward(byte v) => (byte)Math.Clamp(v + (y - v) * 0.25f, 0, 255);
                        return new SKColor(Toward(c.Red), Toward(c.Green), Toward(c.Blue), c.Alpha);
                    });

                default:
                    return null;
            }
        }

        private static SKBitmap CopyArea(SKBitmap source, SKRectI area)
        {
            var copy = new SKBitmap(area.Width, area.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var c = new SKCanvas(copy);
            c.Clear(SKColors.Transparent);
            c.DrawBitmap(source, -area.Left, -area.Top);
            return copy;
        }

        private static SKBitmap MapArea(SKBitmap source, SKRectI area, Func<SKColor, SKColor> map)
        {
            var copy = CopyArea(source, area);
            var pixels = copy.Pixels;
            for (var i = 0; i < pixels.Length; i++)
                if (pixels[i].Alpha != 0) pixels[i] = map(pixels[i]);
            copy.Pixels = pixels;
            return copy;
        }

        /// <summary>The "healing" part of the Healing Brush: shifts the cloned texture's
        /// average color to the destination's, so the copied detail blends into the
        /// surrounding tone instead of carrying its own.</summary>
        private static void MatchMeanColor(SKBitmap clone, SKBitmap target)
        {
            static (double R, double G, double B) Mean(SKColor[] pixels)
            {
                double r = 0, g = 0, b = 0, w = 0;
                foreach (var p in pixels)
                {
                    if (p.Alpha == 0) continue;
                    r += p.Red * p.Alpha; g += p.Green * p.Alpha; b += p.Blue * p.Alpha; w += p.Alpha;
                }
                return w == 0 ? (0, 0, 0) : (r / w, g / w, b / w);
            }

            var clonePixels = clone.Pixels;
            var targetMean = Mean(target.Pixels);
            var cloneMean = Mean(clonePixels);
            var dr = targetMean.R - cloneMean.R;
            var dg = targetMean.G - cloneMean.G;
            var db = targetMean.B - cloneMean.B;

            for (var i = 0; i < clonePixels.Length; i++)
            {
                var p = clonePixels[i];
                if (p.Alpha == 0) continue;
                clonePixels[i] = new SKColor(
                    (byte)Math.Clamp(p.Red + dr, 0, 255),
                    (byte)Math.Clamp(p.Green + dg, 0, 255),
                    (byte)Math.Clamp(p.Blue + db, 0, 255),
                    p.Alpha);
            }

            clone.Pixels = clonePixels;
        }
    }
}
