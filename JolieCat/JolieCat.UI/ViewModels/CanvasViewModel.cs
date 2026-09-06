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
        private const double MinZoom = 0.1;
        private const double MaxZoom = 8.0;
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

        // ================= Warp =================

        private bool _isWarping;
        private Core.Documents.Layer? _warpLayer;
        private SKBitmap? _warpOriginalBitmap;
        private Core.Transform.MeshWarp? _warpMesh;
        private int _warpDragRow = -1;
        private int _warpDragCol = -1;

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
        private Color primaryColor = Colors.White;

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
        public double TextEditScreenX => _textEditOrigin.X * Zoom + PanX;

        public double TextEditScreenY => _textEditOrigin.Y * Zoom + PanY;

        /// <summary>
        /// The in-progress selection outline while a marquee/lasso/polygon is being
        /// drawn - not yet committed to <see cref="Core.Documents.Scene.Selection"/>.
        /// Null once nothing is being drawn, at which point <see cref="CanvasRenderer"/>
        /// falls back to drawing the committed selection's own outline instead.
        /// </summary>
        public SKPath? LiveSelectionPath { get; private set; }

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

        /// <summary>Raised after any change the view should repaint for - layer pixel
        /// content and selection state included, neither of which is an observable
        /// property the view can react to directly.</summary>
        public event EventHandler? InvalidateRequested;

        public CanvasViewModel(ToolboxViewModel toolbox, LayersViewModel layers)
        {
            _toolbox = toolbox ?? throw new ArgumentNullException(nameof(toolbox));
            _toolbox.PropertyChanged += OnToolboxPropertyChanged;

            Layers = layers ?? throw new ArgumentNullException(nameof(layers));
            Layers.InvalidateRequested += OnLayersInvalidateRequested;

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

            // Switching *to* Crop/Free Transform/Warp starts each fresh against
            // whatever's active now - the whole document for Crop, the active layer's
            // current content for Free Transform/Warp.
            if (_toolbox.ActiveTool.Type == ToolType.Crop) StartCrop();
            else if (_toolbox.ActiveTool.Type == ToolType.FreeTransform) StartTransform();
            else if (_toolbox.ActiveTool.Type == ToolType.Warp) StartWarp();

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
        public void OnPointerPressed(SKPoint devicePoint, bool isDoubleClick = false)
        {
            var doc = ToDocumentSpace(devicePoint);
            var activeLayer = Layers.ActiveLayer?.Model;

            switch (_toolbox.ActiveTool.Type)
            {
                case ToolType.Brush:
                case ToolType.Pencil:
                case ToolType.Eraser:
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
                    _isDraggingMarquee = true;
                    _marqueeStart = doc;
                    UpdateLiveMarqueePath(doc);
                    break;

                // Magnetic Lasso behaves as a plain freehand lasso here - true edge-
                // snapping (tracing high-contrast edges under the cursor, a la Sobel/
                // Canny) is a real image-analysis feature and a reasonable follow-up;
                // this gives it working selection behavior now rather than none.
                case ToolType.Lasso:
                case ToolType.MagneticLasso:
                    _isDrawingLasso = true;
                    _lassoPath = new SKPath();
                    _lassoPath.MoveTo(doc);
                    LiveSelectionPath = _lassoPath;
                    RaiseInvalidate();
                    break;

                case ToolType.PolygonalLasso:
                    HandlePolygonClick(doc, isDoubleClick);
                    break;

                // Quick Selection behaves as click-to-flood-select here, identical to
                // Magic Wand - true "paint to grow an adaptive region" is a reasonable
                // follow-up; this gives it working selection behavior now rather than none.
                case ToolType.MagicWand:
                case ToolType.QuickSelection:
                    if (activeLayer is not null)
                        SelectByColor(activeLayer, doc);
                    break;

                case ToolType.Eyedropper:
                    if (activeLayer is not null)
                        SampleColor(activeLayer, doc);
                    break;

                case ToolType.TextHorizontal:
                case ToolType.TextVertical:
                    if (activeLayer is null || activeLayer.IsLocked) break;

                    // A previous edit still open (the user clicked a second spot with a
                    // Text tool still active, without pressing Enter or switching tools
                    // first) is finalized before starting the new one at the new point.
                    CommitTextEdit();

                    _textEditOrigin = doc;
                    _isVerticalTextEdit = _toolbox.ActiveTool.Type == ToolType.TextVertical;
                    TextEditContent = string.Empty;
                    IsTextEditing = true;
                    break;

                case ToolType.Hand:
                    _isPanning = true;
                    _panDragStart = devicePoint;
                    _panStartX = PanX;
                    _panStartY = PanY;
                    break;

                case ToolType.PaintBucket:
                    if (activeLayer is null || activeLayer.IsLocked) break;

                    var beforeFill = activeLayer.PaintBitmap.Pixels;
                    FloodFill(activeLayer, doc);
                    Layers.History.Push(new LayerPixelsCommand(activeLayer.PaintBitmap, beforeFill, activeLayer.PaintBitmap.Pixels));
                    RaiseInvalidate();
                    break;

                case ToolType.Gradient:
                    if (activeLayer is null || activeLayer.IsLocked) break;

                    var beforeGradient = activeLayer.PaintBitmap.Pixels;
                    ApplyGradient(activeLayer, doc);
                    Layers.History.Push(new LayerPixelsCommand(activeLayer.PaintBitmap, beforeGradient, activeLayer.PaintBitmap.Pixels));
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

                default:
                    // Every other tool (the remaining retouching tools, vector/text
                    // tools, canvas rotate) has a Properties panel and an icon but no
                    // canvas interaction implemented yet - a deliberate foundation-stage
                    // gap rather than a fake stand-in.
                    break;
            }
        }

        public void OnPointerMoved(SKPoint devicePoint)
        {
            var doc = ToDocumentSpace(devicePoint);

            if (_isPainting)
            {
                var activeLayer = Layers.ActiveLayer?.Model;
                if (activeLayer is null || activeLayer.IsLocked)
                {
                    _isPainting = false;
                    return;
                }

                PaintLineTo(activeLayer, _lastPaintPoint, doc);
                _lastPaintPoint = doc;
                RaiseInvalidate();
            }
            else if (_isDraggingMarquee)
            {
                UpdateLiveMarqueePath(doc);
            }
            else if (_isDrawingLasso && _lassoPath is not null)
            {
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
                PanX = SnapToPixel(_panStartX + (devicePoint.X - _panDragStart.X));
                PanY = SnapToPixel(_panStartY + (devicePoint.Y - _panDragStart.Y));
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
        }

        public void OnPointerReleased(SKPoint devicePoint)
        {
            if (_isPainting)
            {
                OnPointerMoved(devicePoint);
                CommitStrokeHistory();
            }

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

            // Polygonal Lasso's _isDrawingPolygon deliberately survives a mouse-up: it's
            // a multi-click gesture (click each vertex, double-click to close), not a
            // single drag - see HandlePolygonClick.
        }

        /// <summary>Pushes the just-finished Brush/Pencil/Eraser stroke's before/after
        /// pixel snapshots as one undo entry, if a stroke was actually in progress - a
        /// no-op otherwise (including when called a second time, since the fields it
        /// reads are cleared after the first call).</summary>
        private void CommitStrokeHistory()
        {
            if (_strokeLayer is null || _strokeBeforePixels is null) return;

            Layers.History.Push(new LayerPixelsCommand(_strokeLayer.PaintBitmap, _strokeBeforePixels, _strokeLayer.PaintBitmap.Pixels));

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

            _isPainting = false;
            _isPanning = false;
            _isDraggingMarquee = false;
            _isDrawingLasso = false;
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
        }

        /// <summary>Zooms in/out by one wheel notch, keeping the document point under the
        /// pointer fixed on screen.</summary>
        public void OnMouseWheel(SKPoint devicePoint, int wheelDelta)
        {
            var factor = wheelDelta > 0 ? ZoomWheelFactor : 1.0 / ZoomWheelFactor;
            var newZoom = Math.Clamp(Zoom * factor, MinZoom, MaxZoom);

            var doc = ToDocumentSpace(devicePoint);
            PanX = SnapToPixel(devicePoint.X - doc.X * newZoom);
            PanY = SnapToPixel(devicePoint.Y - doc.Y * newZoom);
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

        private SKPoint ToDocumentSpace(SKPoint devicePoint) => new(
            (float)((devicePoint.X - PanX) / Zoom),
            (float)((devicePoint.Y - PanY) / Zoom));

        // ================= Marquee / Lasso / Polygon selection =================

        private void UpdateLiveMarqueePath(SKPoint current)
        {
            var x = Math.Min(_marqueeStart.X, current.X);
            var y = Math.Min(_marqueeStart.Y, current.Y);
            var width = Math.Abs(current.X - _marqueeStart.X);
            var height = Math.Abs(current.Y - _marqueeStart.Y);
            var rect = new SKRect(x, y, x + width, y + height);

            var path = new SKPath();
            if (_toolbox.ActiveTool.Type == ToolType.EllipticalMarquee)
                path.AddOval(rect);
            else
                path.AddRect(rect);

            LiveSelectionPath = path;
            RaiseInvalidate();
        }

        /// <summary>Commits <see cref="LiveSelectionPath"/> (a finished marquee drag or
        /// lasso stroke) to the scene's selection - or clears the selection instead if
        /// the drag was smaller than <see cref="MinSelectionExtent"/> (a click, not a
        /// drag), giving click-to-deselect for free.</summary>
        private void CommitLiveSelection()
        {
            var path = LiveSelectionPath;
            LiveSelectionPath = null;

            var selection = Layers.Scene.Selection;
            var bounds = path?.Bounds ?? SKRect.Empty;

            if (path is null || bounds.Width < MinSelectionExtent || bounds.Height < MinSelectionExtent)
                selection.Clear();
            else
                selection.SetPath(path, Layers.DocumentWidth, Layers.DocumentHeight);

            RaiseInvalidate();
        }

        private void HandlePolygonClick(SKPoint doc, bool isDoubleClick)
        {
            if (isDoubleClick)
            {
                if (_polygonVertices.Count >= 3)
                    CommitPolygonSelection();
                else
                    CancelPolygonSelection();
                return;
            }

            if (!_isDrawingPolygon)
            {
                _isDrawingPolygon = true;
                _polygonVertices.Clear();
            }

            _polygonVertices.Add(doc);
            _polygonHoverPoint = doc;
            RebuildPolygonPreviewPath();
        }

        private void RebuildPolygonPreviewPath()
        {
            if (_polygonVertices.Count == 0)
            {
                LiveSelectionPath = null;
                return;
            }

            var path = new SKPath();
            path.MoveTo(_polygonVertices[0]);
            for (var i = 1; i < _polygonVertices.Count; i++)
                path.LineTo(_polygonVertices[i]);
            path.LineTo(_polygonHoverPoint); // rubber-band segment to the current pointer position

            LiveSelectionPath = path;
            RaiseInvalidate();
        }

        private void CommitPolygonSelection()
        {
            var path = new SKPath();
            path.MoveTo(_polygonVertices[0]);
            for (var i = 1; i < _polygonVertices.Count; i++)
                path.LineTo(_polygonVertices[i]);
            path.Close();

            LiveSelectionPath = path;
            CommitLiveSelection();

            _isDrawingPolygon = false;
            _polygonVertices.Clear();
        }

        private void CancelPolygonSelection()
        {
            _isDrawingPolygon = false;
            _polygonVertices.Clear();
            LiveSelectionPath = null;
            RaiseInvalidate();
        }

        /// <summary>Magic Wand/Quick Selection: replaces the scene's selection with every
        /// pixel reachable from <paramref name="point"/> within the active tool's
        /// Tolerance.</summary>
        private void SelectByColor(Core.Documents.Layer layer, SKPoint point)
        {
            var options = _toolbox.CurrentToolOptions as SelectionToolOptionsViewModel;
            var tolerance = (float)(options?.Tolerance ?? 32);

            var region = Selection.CreateRegionFromColorFlood(layer.Bitmap, (int)point.X, (int)point.Y, tolerance);
            Layers.Scene.Selection.SetRegion(region);
            RaiseInvalidate();
        }

        /// <summary>Eyedropper: reads the active layer's pixel at <paramref name="point"/>
        /// and adopts it (alpha included) as <see cref="PrimaryColor"/>. A click outside
        /// the document bounds is ignored rather than clamped, since there's no pixel
        /// there to sample.</summary>
        private void SampleColor(Core.Documents.Layer layer, SKPoint point)
        {
            var bitmap = layer.Bitmap;
            var x = (int)point.X;
            var y = (int)point.Y;
            if (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Height) return;

            var sampled = bitmap.GetPixel(x, y);
            PrimaryColor = Color.FromArgb(sampled.Alpha, sampled.Red, sampled.Green, sampled.Blue);
        }

        // ================= Image import =================

        private static readonly HashSet<string> SupportedImageExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".webp" };

        /// <summary>
        /// Imports every supported image among <paramref name="paths"/> (PNG/JPG/JPEG/
        /// BMP/WEBP - anything else is silently skipped) as its own new topmost layer,
        /// named after its file - the file dialog's (<c>MainViewModel.ImportImageCommand</c>)
        /// and the canvas's drag-and-drop (<see cref="Views.CanvasView"/>'s Drop handler)
        /// both funnel through here. If any image's size doesn't already match the
        /// document, the user is asked once per batch (not once per file) whether to
        /// resize the canvas to match it or fit the image(s) within the canvas as it is -
        /// see <see cref="Layers"/>' <c>ImportImage</c> for what either choice actually
        /// does. Answering "cancel" abandons the whole batch, including any files not
        /// yet processed.
        /// </summary>
        public void ImportImageFiles(IReadOnlyList<string> paths)
        {
            ArgumentNullException.ThrowIfNull(paths);

            // Only the first successfully-decoded image in the batch can trigger the
            // resize prompt/choice - every image after it always imports via "fit to the
            // canvas", which by then is whatever size this first choice left it at.
            // Re-asking (or worse, re-resizing) per file would otherwise let a later,
            // differently-sized image in the same drop/selection resize the canvas out
            // from under an image already imported earlier in this same call.
            var isFirstImage = true;

            foreach (var path in paths)
            {
                if (!SupportedImageExtensions.Contains(Path.GetExtension(path))) continue;

                try
                {
                    using var bitmap = SKBitmap.Decode(path);
                    if (bitmap is null)
                    {
                        MessageBox.Show($"Couldn't read '{Path.GetFileName(path)}' as an image.", "Import Image",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        continue;
                    }

                    var resizeDocumentToMatch = false;
                    if (isFirstImage)
                    {
                        if (bitmap.Width != Layers.DocumentWidth || bitmap.Height != Layers.DocumentHeight)
                        {
                            var choice = PromptResizeChoice(bitmap.Width, bitmap.Height, Layers.DocumentWidth, Layers.DocumentHeight);
                            if (choice is null) return; // cancelled - abandon the whole batch

                            resizeDocumentToMatch = choice.Value;
                        }

                        isFirstImage = false;
                    }

                    Layers.ImportImage(bitmap, Path.GetFileNameWithoutExtension(path), resizeDocumentToMatch);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The file vanished, is locked, or isn't readable between being
                    // dropped/picked and being decoded - reported and skipped rather than
                    // aborting the rest of the batch.
                    MessageBox.Show($"Couldn't read '{Path.GetFileName(path)}':\n{ex.Message}", "Import Image",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        /// <summary>Asks whether an about-to-be-imported image's size, which doesn't
        /// match the document's current size, should instead become the document's new
        /// size (every existing layer resized to match) or be left as the document's
        /// size is, fitting the image within it. Null means the user cancelled - abandon
        /// the import rather than guessing.</summary>
        private static bool? PromptResizeChoice(int imageWidth, int imageHeight, int canvasWidth, int canvasHeight)
        {
            var result = MessageBox.Show(
                $"This image is {imageWidth}×{imageHeight}, but the canvas is {canvasWidth}×{canvasHeight}.\n\n" +
                "Yes - resize the canvas to match this image.\n" +
                "No - keep the canvas size and fit the image within it.",
                "Import Image",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            return result switch
            {
                MessageBoxResult.Yes => true,
                MessageBoxResult.No => false,
                _ => null,
            };
        }

        // ================= Text =================

        /// <summary>Rasterizes the in-progress text edit onto the active layer at its
        /// anchor point, then ends the edit - the Horizontal/Vertical Text tools' commit
        /// step. A no-op if nothing is being edited; empty typed content ends the edit
        /// without drawing anything, so an accidental click-then-click-away leaves no
        /// debris on the layer. Explicitly repaints afterward - unlike every other paint
        /// operation, this one isn't invoked from <see cref="OnPointerPressed"/> or
        /// <see cref="OnPointerMoved"/> (which already repaint themselves after calling
        /// into a paint method), so without this the text would be written to the
        /// bitmap correctly but never actually appear until some unrelated action (a pan,
        /// a zoom, another stroke) happened to trigger a redraw.</summary>
        public void CommitTextEdit()
        {
            if (!IsTextEditing) return;

            var text = TextEditContent;
            var layer = Layers.ActiveLayer?.Model;
            if (!string.IsNullOrEmpty(text) && layer is not null && !layer.IsLocked)
            {
                var before = layer.PaintBitmap.Pixels;
                DrawTextOnLayer(layer, text);
                Layers.History.Push(new LayerPixelsCommand(layer.PaintBitmap, before, layer.PaintBitmap.Pixels));
                RaiseInvalidate();
            }

            EndTextEdit();
        }

        /// <summary>Ends the in-progress text edit without drawing anything - Escape's behavior.</summary>
        public void CancelTextEdit() => EndTextEdit();

        private void EndTextEdit()
        {
            IsTextEditing = false;
            TextEditContent = string.Empty;
        }

        /// <summary>Draws <paramref name="text"/> onto <paramref name="layer"/> at
        /// <see cref="_textEditOrigin"/>, using the active Text tool's FontFamily/
        /// FontSize/Bold/Italic options and the current <see cref="PrimaryColor"/> -
        /// clipped to the selection like every other paint operation.</summary>
        private void DrawTextOnLayer(Core.Documents.Layer layer, string text)
        {
            var options = _toolbox.CurrentToolOptions as TextToolOptionsViewModel;
            var fontFamily = options?.FontFamily ?? "Segoe UI";
            var fontSize = (float)(options?.FontSize ?? 24);
            var weight = options?.IsBold == true ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
            var slant = options?.IsItalic == true ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;

            using var typeface = SKTypeface.FromFamilyName(fontFamily, weight, SKFontStyleWidth.Normal, slant);
            using var font = new SKFont(typeface, fontSize, 1f, 0f);
            using var paint = new SKPaint { Color = BrushColor, IsAntialias = true };

            // A literal newline (Shift+Enter in the overlay) starts a new line for
            // Horizontal Text, or a new column for Vertical Text - see DrawVerticalText.
            var lines = text.Replace("\r\n", "\n").Split('\n');

            WithSelectionClip(layer.PaintCanvas, canvas =>
            {
                if (_isVerticalTextEdit)
                    DrawVerticalText(canvas, lines, font, paint);
                else
                    DrawHorizontalText(canvas, lines, font, paint);
            });
        }

        private void DrawHorizontalText(SKCanvas canvas, string[] lines, SKFont font, SKPaint paint)
        {
            // Ascent is negative (distance above the baseline) in Skia's convention, so
            // subtracting it moves the first line's baseline down by that distance -
            // placing the anchor point at the text's top-left, matching where the user
            // actually clicked rather than where the first baseline would otherwise fall.
            var firstBaseline = _textEditOrigin.Y - font.Metrics.Ascent;

            for (var i = 0; i < lines.Length; i++)
                canvas.DrawText(lines[i], _textEditOrigin.X, firstBaseline + i * font.Spacing, font, paint);
        }

        /// <summary>
        /// A real, working vertical layout - not true CJK vertical typesetting (no
        /// glyph rotation or script-aware spacing rules), but genuinely renders each
        /// character stacked top-to-bottom rather than faking the tool with a horizontal
        /// fallback. Each Shift+Enter-delimited "line" becomes its own column, columns
        /// advancing left-to-right by one line's spacing.
        /// </summary>
        private void DrawVerticalText(SKCanvas canvas, string[] lines, SKFont font, SKPaint paint)
        {
            var firstBaseline = _textEditOrigin.Y - font.Metrics.Ascent;
            var columnWidth = font.Spacing;

            for (var column = 0; column < lines.Length; column++)
            {
                var x = _textEditOrigin.X + column * columnWidth;
                var y = firstBaseline;

                foreach (var character in lines[column])
                {
                    canvas.DrawText(character.ToString(), x, y, font, paint);
                    y += font.Spacing;
                }
            }
        }

        // ================= Painting =================

        /// <summary>Runs <paramref name="draw"/> against <paramref name="canvas"/>,
        /// clipped to the scene's current selection if one is active - the mechanism
        /// that makes Rectangular/Elliptical Marquee, Lasso, Polygonal Lasso, and Magic
        /// Wand/Quick Selection actually constrain painting, erasing, and gradients to
        /// the selected pixels rather than just drawing a decorative overlay. Clipping
        /// (unlike the DstOut/DstIn masking tricks) applies uniformly regardless of the
        /// paint's own blend mode, so this works correctly for the eraser too. Clips by
        /// <see cref="Core.Documents.Selection.Path"/> (not the region) so the mask gets
        /// the same anti-aliased edge as everything else drawn on the layer, rather than
        /// the region's hard-edged, integer-aligned approximation of the same shape.</summary>
        private void WithSelectionClip(SKCanvas canvas, Action<SKCanvas> draw)
        {
            var selection = Layers.Scene.Selection;
            if (!selection.HasSelection || selection.Path is not { } clipPath)
            {
                draw(canvas);
                return;
            }

            canvas.Save();
            canvas.ClipPath(clipPath, SKClipOperation.Intersect, antialias: true);
            draw(canvas);
            canvas.Restore();
        }

        /// <summary>Brush/Eraser paint with a single circular "dab", stamped once at the
        /// pointer-down point - the stroke's first mark, before any spacing has a
        /// previous dab to measure from. Pencil instead keeps its plain hard-edged single
        /// pixel (<c>DrawPoint</c>), matching its established always-aliased behavior.</summary>
        private void PaintDot(Core.Documents.Layer layer, SKPoint point)
        {
            if (_toolbox.ActiveTool.Type == ToolType.Pencil)
            {
                using var pencilPaint = CreatePencilPaint();
                WithSelectionClip(layer.PaintCanvas, canvas => canvas.DrawPoint(point, pencilPaint));
                return;
            }

            var size = Math.Max(1f, (float)((_toolbox.CurrentToolOptions as PaintToolOptionsViewModel)?.Size ?? 24));
            var radius = size / 2f;

            using var dabPaint = CreateDabPaint(radius);
            WithSelectionClip(layer.PaintCanvas, canvas => DrawDab(canvas, point, radius, dabPaint));

            // The next dab (from the first PaintLineTo call of this stroke) should land
            // one full spacing interval past this one.
            _distanceUntilNextDab = ComputeDabSpacing(size);
        }

        /// <summary>Brush/Eraser paint between two consecutive pointer-move samples: not
        /// a single stroked line (which can't vary its edge across its own width the way
        /// a soft brush needs to), but a series of circular dabs stamped at even
        /// intervals along the segment - <see cref="_distanceUntilNextDab"/> carries any
        /// leftover distance from one segment into the next so spacing stays even across
        /// the whole stroke, not just within one mouse-move sample. Pencil keeps its
        /// plain hard-edged <c>DrawLine</c>.</summary>
        private void PaintLineTo(Core.Documents.Layer layer, SKPoint from, SKPoint to)
        {
            if (_toolbox.ActiveTool.Type == ToolType.Pencil)
            {
                using var pencilPaint = CreatePencilPaint();
                WithSelectionClip(layer.PaintCanvas, canvas => canvas.DrawLine(from, to, pencilPaint));
                return;
            }

            var size = Math.Max(1f, (float)((_toolbox.CurrentToolOptions as PaintToolOptionsViewModel)?.Size ?? 24));
            var radius = size / 2f;
            var stepDistance = ComputeDabSpacing(size);

            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            var segmentLength = MathF.Sqrt(dx * dx + dy * dy);
            if (segmentLength <= 0f) return;

            using var dabPaint = CreateDabPaint(radius);

            WithSelectionClip(layer.PaintCanvas, canvas =>
            {
                var nextDabAt = _distanceUntilNextDab;
                while (nextDabAt <= segmentLength)
                {
                    var t = nextDabAt / segmentLength;
                    DrawDab(canvas, new SKPoint(from.X + dx * t, from.Y + dy * t), radius, dabPaint);
                    nextDabAt += stepDistance;
                }

                _distanceUntilNextDab = nextDabAt - segmentLength;
            });
        }

        /// <summary>Stamps one dab centered at <paramref name="center"/>. Translates the
        /// canvas rather than rebuilding <paramref name="dabPaint"/>'s shader per dab
        /// position - <see cref="CreateDabPaint"/>'s radial gradient is centered at the
        /// local origin once, and reused (via this translate) for every dab in a stroke.</summary>
        private static void DrawDab(SKCanvas canvas, SKPoint center, float radius, SKPaint dabPaint)
        {
            canvas.Save();
            canvas.Translate(center.X, center.Y);
            canvas.DrawCircle(0, 0, radius, dabPaint);
            canvas.Restore();
        }

        /// <summary>Distance (in pixels) between consecutive dabs - Spacing is a percentage
        /// of the brush's own diameter, matching how Spacing is conventionally expressed
        /// (100% spacing = dabs just touching; well under 100% = a smooth continuous
        /// stroke; well over 100% = a visibly dotted/dashed one).</summary>
        private float ComputeDabSpacing(float size)
        {
            var options = _toolbox.CurrentToolOptions as PaintToolOptionsViewModel;
            var spacingPercent = Math.Clamp(options?.Spacing ?? 20, 1, 500);
            return Math.Max(1f, size * (float)(spacingPercent / 100.0));
        }

        /// <summary>One circular dab's fill paint: solid out to Hardness% of its radius,
        /// then fading linearly to fully transparent at the edge - a real soft/hard brush
        /// edge via a radial-gradient alpha falloff, rather than a flat hard-edged circle
        /// with no Hardness control at all. Hardness 100 skips the gradient entirely (a
        /// plain solid fill) since a fade ending exactly at the edge is indistinguishable
        /// from none, and the eraser gets the same falloff (through its established
        /// DstOut blend mode) so it can un-paint just as softly as the brush paints.</summary>
        private SKPaint CreateDabPaint(float radius)
        {
            var isEraser = _toolbox.ActiveTool.Type == ToolType.Eraser;
            var options = _toolbox.CurrentToolOptions as PaintToolOptionsViewModel;
            var hardness = Math.Clamp(options?.Hardness ?? 100, 0, 100);
            var opacityPercent = options?.Opacity ?? 100;
            var alpha = (byte)Math.Clamp(opacityPercent / 100.0 * 255, 0, 255);

            // Color only matters for its alpha channel when erasing - DstOut (not Clear)
            // removes destination alpha in proportion to the source's, so the eraser's
            // Opacity slider genuinely does partial erasing instead of always punching
            // fully through regardless of it; RGB is irrelevant. See PaintDot/PaintLineTo's
            // callers for why this is shared with the brush's own coloring.
            var color = isEraser ? SKColors.Black.WithAlpha(alpha) : BrushColor.WithAlpha(alpha);

            var paint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                IsAntialias = true,
                BlendMode = isEraser ? SKBlendMode.DstOut : SKBlendMode.SrcOver,
            };

            if (hardness >= 100 || radius <= 0f)
            {
                paint.Color = color;
            }
            else
            {
                // Centered at the local origin (0,0), not the dab's actual canvas
                // position - DrawDab translates the canvas per dab instead of rebuilding
                // this shader for every dab's position.
                paint.Shader = SKShader.CreateRadialGradient(
                    new SKPoint(0, 0), radius,
                    new[] { color, color, color.WithAlpha(0) },
                    new[] { 0f, (float)(hardness / 100.0), 1f },
                    SKShaderTileMode.Clamp);
            }

            return paint;
        }

        /// <summary>Pencil's plain hard-edged, non-antialiased stroke paint - unaffected
        /// by Hardness/Spacing, which only apply to the Brush/Eraser's dab-based painting.</summary>
        private SKPaint CreatePencilPaint()
        {
            var options = _toolbox.CurrentToolOptions as PaintToolOptionsViewModel;
            var size = (float)(options?.Size ?? 24);
            var opacityPercent = options?.Opacity ?? 100;
            var alpha = (byte)Math.Clamp(opacityPercent / 100.0 * 255, 0, 255);

            return new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(1f, size),
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                IsAntialias = false,
                BlendMode = SKBlendMode.SrcOver,
                Color = BrushColor.WithAlpha(alpha),
            };
        }

        private void FloodFill(Core.Documents.Layer layer, SKPoint point)
        {
            var bitmap = layer.PaintBitmap;
            var x0 = (int)point.X;
            var y0 = (int)point.Y;
            if (x0 < 0 || y0 < 0 || x0 >= bitmap.Width || y0 >= bitmap.Height)
                return;

            var selection = Layers.Scene.Selection;
            if (!selection.Contains(x0, y0))
                return;

            var options = _toolbox.CurrentToolOptions as FillToolOptionsViewModel;
            var tolerance = (float)(options?.Tolerance ?? 32);
            var contiguous = options?.Contiguous ?? true;

            var width = bitmap.Width;
            var height = bitmap.Height;
            var pixels = bitmap.Pixels;
            var targetColor = pixels[y0 * width + x0];
            var fillColor = BrushColor;

            if (ColorTolerance.IsWithin(targetColor, fillColor, 0))
                return;

            if (contiguous)
                FloodFillContiguous(pixels, width, height, x0, y0, targetColor, fillColor, tolerance, selection);
            else
                FloodFillGlobal(pixels, width, height, targetColor, fillColor, tolerance, selection);

            bitmap.Pixels = pixels;
        }

        /// <summary>
        /// Stack-based flood fill, seeded from (x0, y0). Each pixel is marked visited
        /// the moment it's pushed - not when it's popped - so it can only ever enter the
        /// stack once, bounding the stack to one entry per pixel instead of letting
        /// duplicates pile up on a large uniform region (a fresh, blank layer is exactly
        /// that).
        /// </summary>
        private static void FloodFillContiguous(
            SKColor[] pixels, int width, int height, int x0, int y0,
            SKColor targetColor, SKColor fillColor, float tolerance, Selection selection)
        {
            var visited = new bool[width * height];
            var stack = new Stack<(int X, int Y)>();

            visited[y0 * width + x0] = true;
            stack.Push((x0, y0));

            while (stack.Count > 0)
            {
                var (x, y) = stack.Pop();
                pixels[y * width + x] = fillColor;

                TryVisit(x + 1, y);
                TryVisit(x - 1, y);
                TryVisit(x, y + 1);
                TryVisit(x, y - 1);
            }

            void TryVisit(int x, int y)
            {
                if (x < 0 || x >= width || y < 0 || y >= height)
                    return;

                var index = y * width + x;
                if (visited[index] || !selection.Contains(x, y) || !ColorTolerance.IsWithin(pixels[index], targetColor, tolerance))
                    return;

                visited[index] = true;
                stack.Push((x, y));
            }
        }

        private static void FloodFillGlobal(SKColor[] pixels, int width, int height, SKColor targetColor, SKColor fillColor, float tolerance, Selection selection)
        {
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = y * width + x;
                    if (selection.Contains(x, y) && ColorTolerance.IsWithin(pixels[index], targetColor, tolerance))
                        pixels[index] = fillColor;
                }
            }
        }

        private void ApplyGradient(Core.Documents.Layer layer, SKPoint point)
        {
            var options = _toolbox.CurrentToolOptions as GradientToolOptionsViewModel;
            var angleDegrees = options?.Angle ?? 90;
            var reverse = options?.Reverse ?? false;

            var startColor = reverse ? SKColors.Transparent : BrushColor;
            var endColor = reverse ? BrushColor : SKColors.Transparent;

            var radians = angleDegrees * Math.PI / 180.0;
            var directionX = (float)Math.Cos(radians);
            var directionY = (float)Math.Sin(radians);
            var reach = Math.Max(layer.Bitmap.Width, layer.Bitmap.Height);

            var start = new SKPoint(point.X - directionX * reach, point.Y - directionY * reach);
            var end = new SKPoint(point.X + directionX * reach, point.Y + directionY * reach);

            using var shader = SKShader.CreateLinearGradient(start, end, new[] { startColor, endColor }, null, SKShaderTileMode.Clamp);
            using var paint = new SKPaint { Shader = shader };

            WithSelectionClip(layer.PaintCanvas, canvas =>
                canvas.DrawRect(new SKRect(0, 0, layer.Bitmap.Width, layer.Bitmap.Height), paint));
        }

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

        // ================= Free Transform =================

        /// <summary>The active layer, while Free Transform or Warp is live-previewing
        /// it rather than its own committed bitmap - <see cref="Rendering.CanvasRenderer"/>
        /// substitutes <see cref="BuildLivePreviewBitmap"/>'s result for exactly this
        /// one layer (see <see cref="Rendering.SceneCompositor.DrawLayers"/>'s
        /// preview-layer overload) so it composites correctly against the layers above
        /// and below it while mid-gesture.</summary>
        public Core.Documents.Layer? LivePreviewLayer => _transformLayer ?? _warpLayer;

        /// <summary>Bakes the current Free Transform/Warp gesture's live preview into a
        /// fresh bitmap for <see cref="Rendering.CanvasRenderer"/> to draw in place of
        /// <see cref="LivePreviewLayer"/>'s own committed content - null when neither is
        /// active. Rebuilt fresh on every call rather than cached: it's only ever
        /// called once per repaint, and a repaint only happens when something (a drag)
        /// actually changed, so there's nothing to gain from caching it.</summary>
        public SKBitmap? BuildLivePreviewBitmap()
        {
            if (_isTransforming && _transformLayer is not null && _transformOriginalBitmap is not null)
            {
                var matrix = BuildLiveTransformMatrix();
                return Core.Transform.LayerTransformer.Bake(_transformOriginalBitmap, matrix, _transformLayer.Bitmap.Width, _transformLayer.Bitmap.Height);
            }

            if (_isWarping && _warpMesh is not null && _warpOriginalBitmap is not null)
                return _warpMesh.Bake(_warpOriginalBitmap);

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
            if (layer is null) return;

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
        /// records it as one undo/redo entry - a no-op if Free Transform isn't active.</summary>
        public void CommitTransform()
        {
            if (!_isTransforming || _transformLayer is null || _transformOriginalBitmap is null)
            {
                EndTransform();
                return;
            }

            var matrix = BuildLiveTransformMatrix();
            var before = _transformLayer.Bitmap.Pixels;
            using (var baked = Core.Transform.LayerTransformer.Bake(_transformOriginalBitmap, matrix, _transformLayer.Bitmap.Width, _transformLayer.Bitmap.Height))
                _transformLayer.Bitmap.Pixels = baked.Pixels;

            Layers.History.Push(new LayerPixelsCommand(_transformLayer.Bitmap, before, _transformLayer.Bitmap.Pixels));
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

        // ================= Warp =================

        /// <summary>The live control-point grid, or null while the Warp tool isn't
        /// active - <see cref="Rendering.CanvasRenderer"/> draws the grid lines/points
        /// from this alongside <see cref="LivePreviewLayer"/>'s warped preview.</summary>
        public Core.Transform.MeshWarp? WarpMesh => _isWarping ? _warpMesh : null;

        private void StartWarp()
        {
            var layer = Layers.ActiveLayer?.Model;
            if (layer is null) return;

            var gridSize = Math.Clamp((_toolbox.CurrentToolOptions as WarpToolOptionsViewModel)?.GridSize ?? 3, 2, 4);

            _warpLayer = layer;
            _warpOriginalBitmap?.Dispose();
            _warpOriginalBitmap = layer.Bitmap.Copy();
            _warpMesh = new Core.Transform.MeshWarp(gridSize, gridSize, layer.Bitmap.Width, layer.Bitmap.Height);
            _isWarping = true;
        }

        /// <summary>Bakes the live-warped mesh into the active layer's actual bitmap
        /// (see <see cref="Core.Transform.MeshWarp.Bake"/>) and records it as one
        /// undo/redo entry - a no-op if Warp isn't active.</summary>
        public void CommitWarp()
        {
            if (!_isWarping || _warpLayer is null || _warpOriginalBitmap is null || _warpMesh is null)
            {
                EndWarp();
                return;
            }

            var before = _warpLayer.Bitmap.Pixels;
            using (var baked = _warpMesh.Bake(_warpOriginalBitmap))
                _warpLayer.Bitmap.Pixels = baked.Pixels;

            Layers.History.Push(new LayerPixelsCommand(_warpLayer.Bitmap, before, _warpLayer.Bitmap.Pixels));
            EndWarp();
            RaiseInvalidate();
        }

        /// <summary>Discards the in-progress warp - the active layer's actual bitmap
        /// was never touched (only ever live-previewed), so this just drops the
        /// preview state, not undoing a write.</summary>
        public void CancelWarp()
        {
            EndWarp();
            RaiseInvalidate();
        }

        private void EndWarp()
        {
            _isWarping = false;
            _warpLayer = null;
            _warpOriginalBitmap?.Dispose();
            _warpOriginalBitmap = null;
            _warpMesh = null;
            _warpDragRow = -1;
            _warpDragCol = -1;
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
            OnPropertyChanged(nameof(TextEditScreenX));
            OnPropertyChanged(nameof(TextEditScreenY));
        }

        public void Dispose()
        {
            if (_disposed) return;

            _toolbox.PropertyChanged -= OnToolboxPropertyChanged;
            Layers.InvalidateRequested -= OnLayersInvalidateRequested;
            _disposed = true;
        }
    }
}
