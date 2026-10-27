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
    /// <summary>
    /// Owns the center canvas's interactive state: the pan/zoom transform, the current
    /// selection tool's in-progress drag/path, and the active foreground color.
    /// <see cref="Views.CanvasView"/> forwards raw WPF pointer events here (in device
    /// pixels, already resolved for DPI); this class turns them into the per-tool
    /// behavior described by <see cref="Core.Tools.ToolCatalog"/>. Painting tools draw
    /// onto whichever layer <see cref="Layers"/> currently has active - not a bitmap of
    /// its own - constrained to <see cref="Core.Documents.Scene.Selection"/> when one is
    /// active. <see cref="CanvasRenderer"/> then composites every visible layer and reads
    /// the pan/zoom/selection state back out to draw a frame.
    /// </summary>
    public partial class CanvasViewModel : ObservableObject, IDisposable
    {
        private const double MinZoom = 0.05;

        /// <summary>High enough to edit individual pixels of small sprites.</summary>
        private const double MaxZoom = 32.0;
        private const double ZoomWheelFactor = 1.1;

        /// <summary>Zoomed out a little so the whole document tends to fit in a typical
        /// canvas panel - the startup view, and also what a freshly loaded project resets
        /// to (see <see cref="ResetView"/>) rather than inheriting whatever view the
        /// previously open document happened to be left at.</summary>
        private const double DefaultZoom = 0.5;

        /// <summary>Below this frame-space distance, a marquee/lasso drag is treated as a
        /// click rather than a drag - it clears the selection instead of setting a
        /// near-zero-area one, giving click-to-deselect for free.</summary>
        private const float MinSelectionExtent = 2f;

        private readonly ToolboxViewModel _toolbox;

        private bool _isPainting;
        private SKPoint _lastPaintPoint;

        /// <summary>Distance (device pixels) still to travel along the current stroke
        /// before the next Brush/Eraser dab is stamped - carried across PaintLineTo calls
        /// so spacing stays even across mouse-move samples, not just within one. Reset by
        /// PaintDot at the start of every new stroke.</summary>
        private float _distanceUntilNextDab;

        /// <summary>Pen pressure at the previous pointer sample - interpolated toward the
        /// new sample's pressure along each painted segment so strokes taper smoothly.</summary>
        private float _lastPressure = 1f;

        /// <summary>Document-space cursor position for the status bar ("X: 120  Y: 48"),
        /// empty while the pointer is outside the document.</summary>
        [ObservableProperty]
        private string cursorPositionText = string.Empty;

        /// <summary>The layer a Brush/Pencil/Eraser stroke is currently painting onto, and
        /// its pixel content from right before the stroke began - <see cref="Layers.History"/>'s
        /// "before" half of the stroke's undo entry, pushed once the stroke ends (see
        /// <see cref="CommitStrokeHistory"/>). Null whenever no stroke is in progress.</summary>
        private Core.Documents.Layer? _strokeLayer;
        private SKColor[]? _strokeBeforePixels;

        private bool _isPanning;
        private SKPoint _panDragStart;
        private double _panStartX;
        private double _panStartY;

        private bool _isDraggingMarquee;
        private SKPoint _marqueeStart;

        private bool _isDrawingLasso;
        private SKPath? _lassoPath;

        private bool _isDrawingPolygon;
        private readonly List<SKPoint> _polygonVertices = new();
        private SKPoint _polygonHoverPoint;

        /// <summary>Document-space anchor of the in-progress text edit - where its first
        /// character/line gets drawn once committed.</summary>
        private SKPoint _textEditOrigin;

        /// <summary>True if the in-progress text edit was started with Vertical Text
        /// rather than Horizontal Text - captured at click time, since the tool could in
        /// principle change while <see cref="IsTextEditing"/> is still true (it doesn't
        /// in practice: switching tools commits first, see <see cref="OnToolboxPropertyChanged"/>).</summary>
        private bool _isVerticalTextEdit;

        private bool _disposed;

        [ObservableProperty]
        private double zoom = 1.0;

        [ObservableProperty]
        private double panX;

        [ObservableProperty]
        private double panY;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PrimaryColorBrush))]
        [NotifyPropertyChangedFor(nameof(HexCode))]
        private Color primaryColor = Colors.Black;

        /// <summary>The color picker's own copy of <see cref="PrimaryColor"/>'s hue, kept
        /// as a separate 0-360 value (rather than always re-deriving it from RGB) so a
        /// desaturated color (S=0, where hue is mathematically undefined) doesn't reset
        /// the Hue slider back to red every time <see cref="Saturation"/> hits zero.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HueBrush))]
        private double hue;

        /// <summary>0-1. Together with <see cref="Hue"/> and <see cref="Brightness"/>,
        /// the color picker's own state - kept in sync with <see cref="PrimaryColor"/>
        /// both ways via <see cref="_isSyncingColor"/>.</summary>
        [ObservableProperty]
        private double saturation;

        /// <summary>0-1 ("Value" in HSV terms - named Brightness here to read clearly
        /// next to <see cref="Saturation"/> rather than colliding with the generic
        /// "value" every generated property setter parameter is already named).</summary>
        [ObservableProperty]
        private double brightness = 1.0;

        /// <summary>Guards <see cref="OnPrimaryColorChanged"/> and <see cref="SyncColorFromHsv"/>
        /// against re-driving each other: setting <see cref="PrimaryColor"/> from HSV (or
        /// vice versa) is a one-way push while this is true, not a round trip.</summary>
        private bool _isSyncingColor;

        /// <summary>True while the Horizontal/Vertical Text tool has an inline text-input
        /// overlay open on the canvas (see <see cref="Views.CanvasView"/>'s TextEditBox).</summary>
        [ObservableProperty]
        private bool isTextEditing;

        /// <summary>The in-progress text edit's content, bound two-way to the overlay
        /// textbox - not yet drawn onto any layer until <see cref="CommitTextEdit"/> runs.</summary>
        [ObservableProperty]
        private string textEditContent = string.Empty;

        /// <summary>The document's layer stack - what tools paint onto and what
        /// <see cref="CanvasRenderer"/> composites.</summary>
        public LayersViewModel Layers { get; }

        /// <summary>The shared tool palette - exposed here (rather than kept purely
        /// private) only so the text-edit overlay in <see cref="Views.CanvasView"/> can
        /// bind its live preview font straight to whichever Text tool's FontFamily/
        /// FontSize/Bold/Italic options are current, without a fragile reach-up to the
        /// Window's DataContext.</summary>
        public ToolboxViewModel Toolbox => _toolbox;

        /// <summary>Screen-space (the canvas surface's own device-pixel space, same as
        /// every <c>OnPointer*</c> parameter) position of the text-edit overlay's anchor
        /// point - recomputed from <see cref="_textEditOrigin"/> whenever <see cref="Zoom"/>
        /// or pan changes (see the partial On*Changed methods), so the overlay tracks its
        /// anchored document position live if the user pans or zooms mid-edit.</summary>
        public double TextEditScreenX => ToRotatedScreen(new SKPoint((float)(_textEditOrigin.X * Zoom + PanX), (float)(_textEditOrigin.Y * Zoom + PanY))).X;

        public double TextEditScreenY => ToRotatedScreen(new SKPoint((float)(_textEditOrigin.X * Zoom + PanX), (float)(_textEditOrigin.Y * Zoom + PanY))).Y;

        /// <summary>
        /// The in-progress selection outline while a marquee/lasso/polygon is being
        /// drawn - not yet committed to <see cref="Core.Documents.Scene.Selection"/>.
        /// Null once nothing is being drawn, at which point <see cref="CanvasRenderer"/>
        /// falls back to drawing the committed selection's own outline instead.
        /// </summary>
        public SKPath? LiveSelectionPath { get; private set; }

        /// <summary>The active document's Sprite Sheet slicing grid - non-null only for
        /// a <see cref="Shared.Enums.ProjectType.SpriteSheet"/> project, so
        /// <see cref="Rendering.CanvasRenderer"/>'s overlay can gate on this one
        /// property alone rather than separately checking <see cref="Layers"/>'
        /// <see cref="Layers.ProjectType"/> itself.</summary>
        public Core.Documents.SpriteSheetGrid? SpriteSheetGrid =>
            Layers.ProjectType == ProjectType.SpriteSheet ? Layers.SpriteSheetGrid : null;

        /// <summary>
        /// <see cref="PrimaryColor"/> converted to SkiaSharp's color type - every paint/
        /// fill/gradient operation draws with this.
        /// </summary>
        public SKColor BrushColor => new(PrimaryColor.R, PrimaryColor.G, PrimaryColor.B, PrimaryColor.A);

        /// <summary>Bindable brush for the color-picker swatches (Border/Button.Background
        /// can't bind directly to a <see cref="Color"/> - there's no built-in converter).</summary>
        public SolidColorBrush PrimaryColorBrush => new(PrimaryColor);

        /// <summary>The pure hue at full saturation/brightness (S=1, V=1) - what the SV
        /// box's tinted base layer should show regardless of the actual color's own
        /// saturation/brightness.</summary>
        public SolidColorBrush HueBrush
        {
            get
            {
                var (r, g, b) = HsvColor.ToRgb(Hue, 1.0, 1.0);
                return new SolidColorBrush(Color.FromRgb(r, g, b));
            }
        }

        /// <summary>"#RRGGBB" view of <see cref="PrimaryColor"/> for the picker's hex
        /// textbox. Setting it parses the text and, only if it's a valid color, adopts
        /// its RGB (keeping the current alpha) - an invalid or still-being-typed value is
        /// silently ignored rather than throwing, so the box just doesn't commit yet.</summary>
        public string HexCode
        {
            get => $"#{PrimaryColor.R:X2}{PrimaryColor.G:X2}{PrimaryColor.B:X2}";
            set
            {
                var hex = value?.Trim();
                if (string.IsNullOrEmpty(hex)) return;
                if (!hex.StartsWith('#')) hex = "#" + hex;

                try
                {
                    if (ColorConverter.ConvertFromString(hex) is Color parsed)
                        PrimaryColor = Color.FromArgb(PrimaryColor.A, parsed.R, parsed.G, parsed.B);
                }
                catch (FormatException)
                {
                    // Not a complete/valid hex color yet (e.g. still mid-typing) - ignored.
                }
            }
        }

        /// <summary>Keeps <see cref="Hue"/>/<see cref="Saturation"/>/<see cref="Brightness"/>
        /// in step whenever <see cref="PrimaryColor"/> changes from anywhere other than
        /// the HSV picker itself (a quick-pick swatch, the hex box, the Eyedropper) -
        /// guarded by <see cref="_isSyncingColor"/> so this doesn't fight with
        /// <see cref="SyncColorFromHsv"/> driving <see cref="PrimaryColor"/> the other way.</summary>
        partial void OnPrimaryColorChanged(Color value)
        {
            if (_isSyncingColor) return;

            var (h, s, v) = HsvColor.FromRgb(value.R, value.G, value.B);
            _isSyncingColor = true;
            Hue = h;
            Saturation = s;
            Brightness = v;
            _isSyncingColor = false;
        }

        partial void OnHueChanged(double value) => SyncColorFromHsv();

        partial void OnSaturationChanged(double value) => SyncColorFromHsv();

        partial void OnBrightnessChanged(double value) => SyncColorFromHsv();

        /// <summary>Pushes the current Hue/Saturation/Brightness into <see cref="PrimaryColor"/>
        /// as RGB - the SV box and Hue strip's side of the two-way sync with <see cref="OnPrimaryColorChanged"/>.</summary>
        private void SyncColorFromHsv()
        {
            if (_isSyncingColor) return;

            _isSyncingColor = true;
            var (r, g, b) = HsvColor.ToRgb(Hue, Saturation, Brightness);
            PrimaryColor = Color.FromArgb(PrimaryColor.A, r, g, b);
            _isSyncingColor = false;
        }

        /// <summary>The active tool's type, mirrored from <see cref="ToolboxViewModel.ActiveTool"/>
        /// so the view can bind the canvas cursor to it without needing the whole toolbox.</summary>
        public ToolType ActiveToolType => _toolbox.ActiveTool.Type;

        /// <summary>Tools that change the active layer's pixels (so need a real layer,
        /// not a group).</summary>
        internal static bool EditsPixels(ToolType type) => type is
            ToolType.Brush or ToolType.Pencil or ToolType.Eraser or ToolType.PaintBucket or ToolType.Gradient or
            ToolType.CloneStamp or ToolType.HealingBrush or ToolType.SpotHealing or ToolType.Blur or ToolType.Sharpen or
            ToolType.Sponge or ToolType.Dodge or ToolType.Burn or ToolType.Shape or
            ToolType.Pen or ToolType.FreeTransform or ToolType.Warp;

        /// <summary>Raised after any change the view should repaint for - layer pixel
        /// content and selection state included, neither of which is an observable
        /// property the view can react to directly.</summary>
        public event EventHandler? InvalidateRequested;

        private (int Width, int Height) _lastDocumentSize;

        public CanvasViewModel(ToolboxViewModel toolbox, LayersViewModel layers)
        {
            _toolbox = toolbox ?? throw new ArgumentNullException(nameof(toolbox));
            _toolbox.PropertyChanged += OnToolboxPropertyChanged;

            Layers = layers ?? throw new ArgumentNullException(nameof(layers));
            Layers.InvalidateRequested += OnLayersInvalidateRequested;

            // Rotating the canvas, Image/Canvas Size, cropping (and undoing them) change
            // the document's size: fit the new shape into the view, like Photoshop.
            _lastDocumentSize = (Layers.DocumentWidth, Layers.DocumentHeight);
            Layers.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is not (nameof(LayersViewModel.DocumentWidth) or nameof(LayersViewModel.DocumentHeight))) return;
                var size = (Layers.DocumentWidth, Layers.DocumentHeight);
                if (size == _lastDocumentSize) return;
                _lastDocumentSize = size;
                if (_viewportWidth > 0 && _viewportHeight > 0) ZoomToFit();
            };

            // History panel names: pixel edits are named after the tool that made them.
            Layers.History.DefaultName = _ => _toolbox.ActiveTool.DisplayName;

            // Anchored at the top-left (pan starts at 0,0) rather than centered - true
            // centering needs the viewport's size, which isn't known yet at construction
            // time; a reasonable follow-up once this is in daily use.
            zoom = DefaultZoom;
        }

        /// <summary>Resets pan/zoom back to their startup defaults - called after loading
        /// a project (see <c>MainViewModel.OpenProject</c>) so a newly opened document
        /// always appears at a consistent, predictable view instead of wherever the
        /// previously open document happened to be panned/zoomed to. Without this, a
        /// document reopened after the user had panned/zoomed around the last one reads
        /// as its content having shifted, even though every pixel loaded back exactly
        /// where it was saved.</summary>
        public void ResetView()
        {
            Zoom = DefaultZoom;
            PanX = 0;
            PanY = 0;

            // Fit and center the document once the viewport size is known (right away
            // if it already is).
            _fitPending = true;
            if (_viewportWidth > 0 && _viewportHeight > 0)
            {
                _fitPending = false;
                ZoomToFit();
            }
        }

        private void OnToolboxPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ToolboxViewModel.ActiveTool)) return;

            // An in-progress text edit is finalized (drawn to the layer), not discarded -
            // unlike every other gesture below, it holds real typed content the user
            // would not expect to vanish just from clicking a different tool.
            CommitTextEdit();

            // Crop/Free Transform/Warp are the same: each holds real, user-driven work
            // (a dragged rectangle, a live scale/rotation, a warped grid) that would
            // otherwise silently vanish just from clicking a different tool - so
            // switching away from one commits it, exactly like the text edit above.
            CommitCrop();
            CommitTransform();
            CommitWarp();

            // Switching tools mid-gesture (e.g. clicking Brush while a polygon is only
            // half-drawn) abandons that gesture rather than leaving it to silently
            // resume - and silently resume it would: Polygonal Lasso's vertices survive
            // across individual clicks by design (see HandlePolygonClick), so without
            // this, switching away and back would append new clicks to the stale
            // in-progress shape. This never touches the *committed* selection - that's
            // meant to persist across tool switches (select with Lasso, then paint with
            // Brush inside it).
            CancelInteraction();

            // The Pen tool's working path is shared across Pen/Path Selection/Direct
            // Selection - the whole point of those three tools existing is to keep
            // building and editing the very same path - so it only gets discarded when
            // switching to something outside that trio, exactly like a *committed*
            // Selection survives every other tool switch while a still-in-progress
            // gesture (the polygon vertices just above) does not.
            if (!IsPenPathTool(_toolbox.ActiveTool.Type))
                ClearPenPath();

            // Switching *to* Crop/Free Transform/Warp starts each fresh against
            // whatever's active now - the whole document for Crop, the active layer's
            // current content for Free Transform/Warp.
            if (_toolbox.ActiveTool.Type == ToolType.Crop) StartCrop();
            else if (_toolbox.ActiveTool.Type == ToolType.FreeTransform) StartTransform();
            else if (_toolbox.ActiveTool.Type == ToolType.Warp) StartWarp();

            ToolHintText = _toolbox.ActiveTool.Type switch
            {
                ToolType.CloneStamp or ToolType.HealingBrush when _cloneSource is null => "Alt+click to set the clone source",
                ToolType.Zoom => "Click to zoom in, Alt+click to zoom out",
                ToolType.Move => "Drag to move the layer (or the selected pixels)",
                _ => string.Empty,
            };

            OnPropertyChanged(nameof(ActiveToolType));
            RaiseInvalidate();
        }

        private void OnLayersInvalidateRequested(object? sender, EventArgs e) => RaiseInvalidate();

        [RelayCommand]
        private void SetColorFromHex(string? hex)
        {
            if (!string.IsNullOrWhiteSpace(hex) && ColorConverter.ConvertFromString(hex) is Color color)
                PrimaryColor = color;
        }

        /// <param name="isDoubleClick">True for the second click of a double-click (WPF's
        /// <c>MouseButtonEventArgs.ClickCount == 2</c>) - Polygonal Lasso's "close the
        /// polygon" gesture.</param>
        /// <param name="pressure">Pen pressure 0-1 (1 for a mouse).</param>
        public void OnPointerPressed(SKPoint devicePoint, bool isDoubleClick = false, float pressure = 1f)
        {
            if (IsSpacePanArmed)
            {
                BeginPan(devicePoint);
                return;
            }

            var doc = ToDocumentSpace(devicePoint);
            var activeLayer = Layers.ActiveLayer?.Model;
            _lastPressure = pressure;

            // A group has no pixels: tools that edit pixels need a layer inside it.
            if (activeLayer is { IsGroup: true, IsMaskActive: false } && EditsPixels(_toolbox.ActiveTool.Type))
            {
                ToolHintText = "A group is selected - pick a layer inside it to edit pixels";
                return;
            }

            // Painting on live text rasterizes it first (one undoable step), like Photoshop.
            if (activeLayer is { IsText: true } && EditsPixels(_toolbox.ActiveTool.Type))
                RasterizeIfText(activeLayer);

            if (TryHandleExtendedToolPress(devicePoint, doc, activeLayer))
                return;

            switch (_toolbox.ActiveTool.Type)
            {
                case ToolType.Brush:
                case ToolType.Pencil:
                case ToolType.Eraser:
                case ToolType.CloneStamp:
                case ToolType.HealingBrush:
                case ToolType.Blur:
                case ToolType.Sharpen:
                case ToolType.Sponge:
                case ToolType.Dodge:
                case ToolType.Burn:
                    if (activeLayer is null || activeLayer.IsLocked) break;

                    _isPainting = true;
                    _lastPaintPoint = doc;
                    _strokeLayer = activeLayer;
                    _strokeBeforePixels = activeLayer.PaintBitmap.Pixels;
                    PaintDot(activeLayer, doc);
                    RaiseInvalidate();
                    break;

                case ToolType.RectangularMarquee:
                case ToolType.EllipticalMarquee:
                    CaptureSelectionCombine();
                    _isDraggingMarquee = true;
                    _marqueeStart = doc;
                    UpdateLiveMarqueePath(doc);
                    break;

                // Magnetic Lasso is the same freehand gesture with its points snapped
                // to nearby edges (see CanvasViewModel.MagneticLasso.cs).
                case ToolType.Lasso:
                case ToolType.MagneticLasso:
                    CaptureSelectionCombine();
                    _isDrawingLasso = true;
                    _lassoPath = new SKPath();
                    if (_toolbox.ActiveTool.Type == ToolType.MagneticLasso && activeLayer is not null)
                    {
                        BeginMagneticLasso(activeLayer, doc);
                        doc = SnapToEdge(doc);
                    }
                    _lassoPath.MoveTo(doc);
                    LiveSelectionPath = _lassoPath;
                    RaiseInvalidate();
                    break;

                case ToolType.PolygonalLasso:
                    HandlePolygonClick(doc, isDoubleClick);
                    break;

                case ToolType.MagicWand:
                    if (activeLayer is not null)
                        SelectByColor(activeLayer, doc);
                    break;

                case ToolType.Eyedropper:
                    if (activeLayer is not null)
                        SampleColor(activeLayer, doc);
                    break;

                case ToolType.TextHorizontal:
                case ToolType.TextVertical:
                    // New text goes on a new text layer, so the active layer doesn't matter.
                    BeginTextAt(doc);
                    break;

                case ToolType.Hand:
                    MarkViewChangedByUser();
                    _isPanning = true;
                    _panDragStart = devicePoint;
                    _panStartX = PanX;
                    _panStartY = PanY;
                    break;

                case ToolType.PaintBucket:
                    if (activeLayer is null || activeLayer.IsLocked) break;

                    var beforeFill = activeLayer.PaintBitmap.Pixels;
                    FloodFill(activeLayer, doc);
                    Layers.History.Push(PixelEdit(activeLayer, beforeFill));
                    RaiseInvalidate();
                    break;

                case ToolType.Crop:
                    if (!_isCropping) break;

                    _activeCropHandle = HitTestCropHandle(doc);
                    _cropDragStart = doc;
                    _cropDragStartRect = _cropRect;
                    break;

                case ToolType.FreeTransform:
                    if (!_isTransforming) break;

                    _activeTransformHandle = HitTestTransformHandle(doc);
                    _transformDragStart = doc;
                    _transformDragStartScaleX = _transformScaleX;
                    _transformDragStartScaleY = _transformScaleY;
                    _transformDragStartRotation = _transformRotation;
                    _transformDragStartTranslateX = _transformTranslateX;
                    _transformDragStartTranslateY = _transformTranslateY;
                    break;

                case ToolType.Warp:
                    if (_warpMesh is null) break;

                    (_warpDragRow, _warpDragCol) = _warpMesh.FindNearestPoint(doc);
                    break;

                case ToolType.Pen:
                    HandlePenPress(doc, isDoubleClick);
                    break;

                case ToolType.PathSelection:
                    HandlePathSelectionPress(doc);
                    break;

                case ToolType.DirectSelection:
                    HandleDirectSelectionPress(doc);
                    break;

                default:
                    // Every other tool (the remaining retouching tools, vector/text
                    // tools, canvas rotate) has a Properties panel and an icon but no
                    // canvas interaction implemented yet - a deliberate foundation-stage
                    // gap rather than a fake stand-in.
                    break;
            }
        }

        public void OnPointerMoved(SKPoint devicePoint, float pressure = 1f)
        {
            var doc = ToDocumentSpace(devicePoint);
            CursorPositionText = doc.X >= 0 && doc.Y >= 0 && doc.X < Layers.DocumentWidth && doc.Y < Layers.DocumentHeight
                ? $"X: {(int)doc.X}  Y: {(int)doc.Y}"
                : string.Empty;

            HoverDocumentPoint = doc;

            if (_isRotatingView)
            {
                UpdateRotateView(devicePoint);
                return;
            }

            if (TryHandleExtendedToolMove(doc))
                return;

            // Keeps the brush-size outline following the pointer.
            if (!_isPainting && BrushOutlineDiameter is not null)
                RaiseInvalidate();

            if (_isPainting)
            {
                var activeLayer = Layers.ActiveLayer?.Model;
                if (activeLayer is null || activeLayer.IsLocked)
                {
                    _isPainting = false;
                    return;
                }

                PaintLineTo(activeLayer, _lastPaintPoint, doc, _lastPressure, pressure);
                _lastPaintPoint = doc;
                _lastPressure = pressure;
                RaiseInvalidate();
            }
            else if (_isDraggingMarquee)
            {
                UpdateLiveMarqueePath(doc);
            }
            else if (_isDrawingLasso && _lassoPath is not null)
            {
                if (_magneticLuma is not null)
                    ExtendMagneticLasso(_lassoPath, doc);
                else
                    _lassoPath.LineTo(doc);
                LiveSelectionPath = _lassoPath;
                RaiseInvalidate();
            }
            else if (_isDrawingPolygon)
            {
                _polygonHoverPoint = doc;
                RebuildPolygonPreviewPath();
            }
            else if (_isPanning)
            {
                // Snapped to whole device pixels - see SnapToPixel's own remarks for why
                // an unsnapped Pan here would misalign the *entire* composited frame
                // (checkerboard and every layer alike) against the device-pixel grid for
                // as long as the pan lasts, not just this one gesture's own drawing.
                // With a rotated view the drag is turned back into unrotated space.
                var now = ToUnrotatedScreen(devicePoint);
                var start = ToUnrotatedScreen(_panDragStart);
                PanX = SnapToPixel(_panStartX + (now.X - start.X));
                PanY = SnapToPixel(_panStartY + (now.Y - start.Y));
            }
            else if (_isCropping && _activeCropHandle != CropHandle.None)
            {
                UpdateCropDrag(doc);
                RaiseInvalidate();
            }
            else if (_isTransforming && _activeTransformHandle != TransformHandle.None)
            {
                UpdateTransformDrag(doc);
                RaiseInvalidate();
            }
            else if (_isWarping && _warpDragRow >= 0 && _warpMesh is not null)
            {
                _warpMesh.WarpedGrid[_warpDragRow, _warpDragCol] = doc;
                RaiseInvalidate();
            }
            else if (_isDraggingPenHandle)
            {
                UpdatePenHandleDrag(doc);
            }
            else if (_isDrawingPen)
            {
                _penHoverPoint = doc;
                RaiseInvalidate();
            }
            else if (_penNodeDragTarget != PenNodeDragTarget.None)
            {
                UpdatePenNodeDrag(doc);
            }
            else if (_isDraggingPenPath)
            {
                UpdatePenPathDrag(doc);
            }
        }

        public void OnPointerReleased(SKPoint devicePoint)
        {
            if (_isPainting)
            {
                OnPointerMoved(devicePoint, _lastPressure);
                CommitStrokeHistory();
            }

            HandleExtendedToolRelease();

            if (_isDraggingMarquee)
                CommitLiveSelection();

            if (_isDrawingLasso)
            {
                _lassoPath?.Close();
                CommitLiveSelection();
            }

            _isPainting = false;
            _isPanning = false;
            _isDraggingMarquee = false;
            _isDrawingLasso = false;
            EndMagneticLasso();
            _lassoPath = null;

            // Crop/Free Transform/Warp deliberately survive a mouse-up (unlike every
            // gesture above): only the currently-dragged handle/point releases, not the
            // whole in-progress operation - the user can keep adjusting handles across
            // multiple drags before committing with Enter, exactly like every real
            // crop/transform/warp tool's own mouse-up behavior.
            _activeCropHandle = CropHandle.None;
            _activeTransformHandle = TransformHandle.None;
            _warpDragRow = -1;
            _warpDragCol = -1;

            _isDraggingPenHandle = false;
            _penPendingAnchorIndex = -1;
            _penNodeDragTarget = PenNodeDragTarget.None;
            _penNodeDragAnchorIndex = -1;
            _isDraggingPenPath = false;
            _penPathDragSnapshot = null;

            // Polygonal Lasso's _isDrawingPolygon, and the Pen tool's _isDrawingPen,
            // deliberately survive a mouse-up: both are multi-click gestures (click each
            // vertex/anchor, double-click to close), not a single drag - see
            // HandlePolygonClick/HandlePenPress.
        }

        /// <summary>Pushes the just-finished Brush/Pencil/Eraser stroke's before/after
        /// pixel snapshots as one undo entry, if a stroke was actually in progress - a
        /// no-op otherwise (including when called a second time, since the fields it
        /// reads are cleared after the first call).</summary>
        private void CommitStrokeHistory()
        {
            if (_strokeLayer is null || _strokeBeforePixels is null) return;

            Layers.History.Push(PixelEdit(_strokeLayer, _strokeBeforePixels));

            _strokeLayer = null;
            _strokeBeforePixels = null;
        }

        /// <summary>
        /// Ends any in-progress drag/paint/pan/selection gesture without finalizing it.
        /// Used when mouse capture is lost unexpectedly (e.g. a dialog steals it
        /// mid-drag) so a gesture flag can't get stuck true forever, which would
        /// otherwise make later mouse moves keep painting/panning/selecting with no
        /// button held.
        /// </summary>
        public void CancelInteraction()
        {
            // A stroke already painted real pixels before capture was lost - that's not
            // discarded along with the rest of the gesture state below, it's finalized
            // into history exactly as a normal mouse-up would.
            if (_isPainting)
                CommitStrokeHistory();

            CancelExtendedTools();

            _isPainting = false;
            _isPanning = false;
            _isDraggingMarquee = false;
            _isDrawingLasso = false;
            EndMagneticLasso();
            _lassoPath = null;
            _isDrawingPolygon = false;
            _polygonVertices.Clear();
            LiveSelectionPath = null;

            // Only the currently-dragged handle/point releases here too - not the whole
            // in-progress Crop/Free Transform/Warp operation, matching OnPointerReleased.
            _activeCropHandle = CropHandle.None;
            _activeTransformHandle = TransformHandle.None;
            _warpDragRow = -1;
            _warpDragCol = -1;

            // Same story for the Pen tool: only whichever handle/node/whole-path drag
            // was active releases - _penPath/_isDrawingPen themselves survive exactly
            // like _isDrawingPolygon does just above.
            _isDraggingPenHandle = false;
            _penPendingAnchorIndex = -1;
            _penNodeDragTarget = PenNodeDragTarget.None;
            _penNodeDragAnchorIndex = -1;
            _isDraggingPenPath = false;
            _penPathDragSnapshot = null;
        }

        /// <summary>Zooms in/out by one wheel notch, keeping the document point under the
        /// pointer fixed on screen.</summary>
        public void OnMouseWheel(SKPoint devicePoint, int wheelDelta)
        {
            MarkViewChangedByUser();
            var factor = wheelDelta > 0 ? ZoomWheelFactor : 1.0 / ZoomWheelFactor;
            var newZoom = Math.Clamp(Zoom * factor, MinZoom, MaxZoom);

            var doc = ToDocumentSpace(devicePoint);
            var screen = ToUnrotatedScreen(devicePoint);
            PanX = SnapToPixel(screen.X - doc.X * newZoom);
            PanY = SnapToPixel(screen.Y - doc.Y * newZoom);
            Zoom = newZoom;
        }

        /// <summary>
        /// Rounds a pan offset to the nearest whole device pixel. PanX/PanY feed
        /// straight into CanvasRenderer's canvas.Translate(PanX, PanY) - the very first
        /// thing applied to the whole frame, before the checkerboard or a single layer
        /// is drawn - so a fractional value here doesn't just shift one thing a
        /// sub-pixel amount, it misaligns the *entire* composited document (every edge
        /// of the checkerboard and every layer alike) against the device-pixel grid for
        /// as long as that pan/zoom lasts. Skia then has to antialias/blend right at
        /// that boundary instead of drawing a crisp edge, which reads as a faint
        /// "bleeding" seam - most visible at whichever edge has the strongest contrast
        /// against OutsideDocumentColor. Both gestures that set Pan (drag with the Hand
        /// tool, and the mouse-wheel zoom-anchor math) produce essentially-never-integer
        /// values on their own, so this is applied at both, not left to chance.
        /// </summary>
        private static double SnapToPixel(double value) => Math.Round(value, MidpointRounding.AwayFromZero);

        private SKPoint ToDocumentSpace(SKPoint devicePoint)
        {
            var screen = ToUnrotatedScreen(devicePoint);
            return new SKPoint((float)((screen.X - PanX) / Zoom), (float)((screen.Y - PanY) / Zoom));
        }

        private void RaiseInvalidate()
        {
            // Cheap even when nothing mask-related is going on (a null check and a
            // bool read) - keeps the Layers panel's mask thumbnail live while a mask is
            // actively being painted on, without every individual paint call site
            // needing its own copy of this same check.
            if (Layers.ActiveLayer is { } activeItem && activeItem.Model.IsMaskActive)
                activeItem.RefreshMaskThumbnail();

            InvalidateRequested?.Invoke(this, EventArgs.Empty);
        }

        partial void OnPanXChanged(double value)
        {
            RaiseInvalidate();
            OnPropertyChanged(nameof(TextEditScreenX));
        }

        partial void OnPanYChanged(double value)
        {
            RaiseInvalidate();
            OnPropertyChanged(nameof(TextEditScreenY));
        }

        partial void OnZoomChanged(double value)
        {
            RaiseInvalidate();
            OnPropertyChanged(nameof(ZoomText));
            OnPropertyChanged(nameof(TextEditScreenX));
            OnPropertyChanged(nameof(TextEditScreenY));
        }

        public void Dispose()
        {
            if (_disposed) return;

            _toolbox.PropertyChanged -= OnToolboxPropertyChanged;
            Layers.InvalidateRequested -= OnLayersInvalidateRequested;
            RenderCache?.Dispose();
            RenderCache = null;
            _disposed = true;
        }

        /// <summary>Renderer-owned cache of the composited layers above/below the
        /// active one (see <c>Rendering.LayerRenderCache</c>). Lives here so it's
        /// per-document and freed with the document.</summary>
        internal IDisposable? RenderCache { get; set; }
    }
}
