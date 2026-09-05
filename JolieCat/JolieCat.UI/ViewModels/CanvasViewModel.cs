using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels.ToolOptions;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    /// <summary>
    /// Owns the center canvas's interactive state: the in-memory <see cref="SKBitmap"/>
    /// painted strokes persist on, the pan/zoom transform, the marquee selection
    /// overlay, and the active foreground color. <see cref="Views.CanvasView"/> forwards
    /// raw WPF pointer events here (in device pixels, already resolved for DPI); this
    /// class turns them into the per-tool behavior described by
    /// <see cref="Core.Tools.ToolCatalog"/> and mutates the bitmap or the pan/zoom/
    /// marquee state accordingly. <see cref="CanvasRenderer"/> then reads that state back
    /// out to draw a frame - it owns no state of its own.
    /// </summary>
    public partial class CanvasViewModel : ObservableObject, IDisposable
    {
        public const int DocumentWidth = 1600;
        public const int DocumentHeight = 1200;

        private const double MinZoom = 0.1;
        private const double MaxZoom = 8.0;
        private const double ZoomWheelFactor = 1.1;

        private readonly ToolboxViewModel _toolbox;
        private readonly SKBitmap _bitmap;
        private readonly SKCanvas _bitmapCanvas;

        private bool _isPainting;
        private SKPoint _lastPaintPoint;

        private bool _isDraggingMarquee;
        private SKPoint _marqueeStart;

        private bool _isPanning;
        private SKPoint _panDragStart;
        private double _panStartX;
        private double _panStartY;

        private bool _disposed;

        [ObservableProperty]
        private double zoom = 1.0;

        [ObservableProperty]
        private double panX;

        [ObservableProperty]
        private double panY;

        [ObservableProperty]
        private bool isMarqueeActive;

        [ObservableProperty]
        private double marqueeX;

        [ObservableProperty]
        private double marqueeY;

        [ObservableProperty]
        private double marqueeWidth;

        [ObservableProperty]
        private double marqueeHeight;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PrimaryColorBrush))]
        [NotifyPropertyChangedFor(nameof(RedComponent))]
        [NotifyPropertyChangedFor(nameof(GreenComponent))]
        [NotifyPropertyChangedFor(nameof(BlueComponent))]
        private Color primaryColor = Colors.White;

        /// <summary>The persistent drawing surface. Painted strokes live here, not on screen.</summary>
        public SKBitmap Bitmap => _bitmap;

        /// <summary>
        /// <see cref="PrimaryColor"/> converted to SkiaSharp's color type - every paint/
        /// fill/gradient operation draws with this.
        /// </summary>
        public SKColor BrushColor => new(PrimaryColor.R, PrimaryColor.G, PrimaryColor.B, PrimaryColor.A);

        /// <summary>Bindable brush for the color-picker swatches (Border/Button.Background
        /// can't bind directly to a <see cref="Color"/> - there's no built-in converter).</summary>
        public SolidColorBrush PrimaryColorBrush => new(PrimaryColor);

        /// <summary>0-255 color-channel properties the picker's RGB sliders bind to; each
        /// reads/writes through <see cref="PrimaryColor"/> since WPF can't two-way bind
        /// into one field of a value-type property.</summary>
        public double RedComponent
        {
            get => PrimaryColor.R;
            set => PrimaryColor = Color.FromRgb(ToByte(value), PrimaryColor.G, PrimaryColor.B);
        }

        public double GreenComponent
        {
            get => PrimaryColor.G;
            set => PrimaryColor = Color.FromRgb(PrimaryColor.R, ToByte(value), PrimaryColor.B);
        }

        public double BlueComponent
        {
            get => PrimaryColor.B;
            set => PrimaryColor = Color.FromRgb(PrimaryColor.R, PrimaryColor.G, ToByte(value));
        }

        private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);

        /// <summary>The active tool's type, mirrored from <see cref="ToolboxViewModel.ActiveTool"/>
        /// so the view can bind the canvas cursor to it without needing the whole toolbox.</summary>
        public ToolType ActiveToolType => _toolbox.ActiveTool.Type;

        /// <summary>Raised after any change the view should repaint for - bitmap content included,
        /// which (unlike pan/zoom/marquee) isn't an observable property the view can react to directly.</summary>
        public event EventHandler? InvalidateRequested;

        public CanvasViewModel(ToolboxViewModel toolbox)
        {
            _toolbox = toolbox ?? throw new ArgumentNullException(nameof(toolbox));
            _toolbox.PropertyChanged += OnToolboxPropertyChanged;

            _bitmap = new SKBitmap(DocumentWidth, DocumentHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            _bitmapCanvas = new SKCanvas(_bitmap);
            _bitmapCanvas.Clear(SKColors.Transparent);

            // Zoomed out a little so the whole document tends to fit in a typical canvas
            // panel at startup. Anchored at the top-left (pan starts at 0,0) rather than
            // centered - true centering needs the viewport's size, which isn't known yet
            // at construction time; a reasonable follow-up once this is in daily use.
            zoom = 0.5;
        }

        private void OnToolboxPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ToolboxViewModel.ActiveTool))
                OnPropertyChanged(nameof(ActiveToolType));
        }

        [RelayCommand]
        private void SetColorFromHex(string? hex)
        {
            if (!string.IsNullOrWhiteSpace(hex) && ColorConverter.ConvertFromString(hex) is Color color)
                PrimaryColor = color;
        }

        public void OnPointerPressed(SKPoint devicePoint)
        {
            var doc = ToDocumentSpace(devicePoint);

            switch (_toolbox.ActiveTool.Type)
            {
                case ToolType.Brush:
                case ToolType.Pencil:
                case ToolType.Eraser:
                    _isPainting = true;
                    _lastPaintPoint = doc;
                    PaintDot(doc);
                    RaiseInvalidate();
                    break;

                case ToolType.RectangularMarquee:
                case ToolType.EllipticalMarquee:
                    _isDraggingMarquee = true;
                    _marqueeStart = doc;
                    IsMarqueeActive = true;
                    UpdateMarqueeRect(doc);
                    break;

                case ToolType.Hand:
                    _isPanning = true;
                    _panDragStart = devicePoint;
                    _panStartX = PanX;
                    _panStartY = PanY;
                    break;

                case ToolType.PaintBucket:
                    FloodFill(doc);
                    RaiseInvalidate();
                    break;

                case ToolType.Gradient:
                    ApplyGradient(doc);
                    RaiseInvalidate();
                    break;

                default:
                    // Every other tool (the remaining retouching tools, the other
                    // selection tools, vector/text tools, canvas rotate) has a Properties
                    // panel and an icon but no canvas interaction implemented yet - a
                    // deliberate foundation-stage gap rather than a fake stand-in.
                    break;
            }
        }

        public void OnPointerMoved(SKPoint devicePoint)
        {
            var doc = ToDocumentSpace(devicePoint);

            if (_isPainting)
            {
                PaintLineTo(_lastPaintPoint, doc);
                _lastPaintPoint = doc;
                RaiseInvalidate();
            }
            else if (_isDraggingMarquee)
            {
                UpdateMarqueeRect(doc);
            }
            else if (_isPanning)
            {
                PanX = _panStartX + (devicePoint.X - _panDragStart.X);
                PanY = _panStartY + (devicePoint.Y - _panDragStart.Y);
            }
        }

        public void OnPointerReleased(SKPoint devicePoint)
        {
            if (_isPainting)
            {
                OnPointerMoved(devicePoint);
            }

            CancelInteraction();

            // IsMarqueeActive deliberately stays true: the dashed rectangle remains as
            // "the current selection" until a new marquee drag starts, same as a real
            // selection tool - there's no Escape/deselect action yet to clear it early.
        }

        /// <summary>
        /// Ends any in-progress drag/paint/pan gesture without finalizing it (no final
        /// paint segment, no capture release - the view handles that). Used when mouse
        /// capture is lost unexpectedly (e.g. a dialog steals it mid-drag) so a gesture
        /// flag can't get stuck true forever, which would otherwise make later mouse
        /// moves keep painting/panning even with no button held.
        /// </summary>
        public void CancelInteraction()
        {
            _isPainting = false;
            _isPanning = false;
            _isDraggingMarquee = false;
        }

        /// <summary>Zooms in/out by one wheel notch, keeping the document point under the
        /// pointer fixed on screen.</summary>
        public void OnMouseWheel(SKPoint devicePoint, int wheelDelta)
        {
            var factor = wheelDelta > 0 ? ZoomWheelFactor : 1.0 / ZoomWheelFactor;
            var newZoom = Math.Clamp(Zoom * factor, MinZoom, MaxZoom);

            var doc = ToDocumentSpace(devicePoint);
            PanX = devicePoint.X - doc.X * newZoom;
            PanY = devicePoint.Y - doc.Y * newZoom;
            Zoom = newZoom;
        }

        private SKPoint ToDocumentSpace(SKPoint devicePoint) => new(
            (float)((devicePoint.X - PanX) / Zoom),
            (float)((devicePoint.Y - PanY) / Zoom));

        private void PaintDot(SKPoint point)
        {
            using var paint = CreatePaintForActiveTool();
            _bitmapCanvas.DrawPoint(point, paint);
        }

        private void PaintLineTo(SKPoint from, SKPoint to)
        {
            using var paint = CreatePaintForActiveTool();
            _bitmapCanvas.DrawLine(from, to, paint);
        }

        private SKPaint CreatePaintForActiveTool()
        {
            var toolType = _toolbox.ActiveTool.Type;
            var isEraser = toolType == ToolType.Eraser;
            var isPencil = toolType == ToolType.Pencil;

            var options = _toolbox.CurrentToolOptions as PaintToolOptionsViewModel;
            var size = (float)(options?.Size ?? 24);
            var hardness = options?.Hardness ?? 100;
            var opacityPercent = options?.Opacity ?? 100;

            var alpha = (byte)Math.Clamp(opacityPercent / 100.0 * 255, 0, 255);

            var paint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(1f, size),
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                IsAntialias = !isPencil,
                // DstOut (not Clear) removes destination alpha in proportion to the
                // source alpha, so the eraser's Opacity slider genuinely does partial
                // erasing instead of always punching fully through regardless of it.
                // Color only matters for its alpha channel here - RGB is irrelevant.
                BlendMode = isEraser ? SKBlendMode.DstOut : SKBlendMode.SrcOver,
                Color = isEraser ? SKColors.Black.WithAlpha(alpha) : BrushColor.WithAlpha(alpha),
            };

            // Softer brushes get a blurred stroke edge; pencil and the eraser stay hard-edged.
            if (!isEraser && !isPencil && hardness < 100)
            {
                var blurRadius = (float)((100 - hardness) / 100.0 * size * 0.35);
                if (blurRadius > 0.1f)
                    paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blurRadius);
            }

            return paint;
        }

        private void UpdateMarqueeRect(SKPoint current)
        {
            MarqueeX = Math.Min(_marqueeStart.X, current.X);
            MarqueeY = Math.Min(_marqueeStart.Y, current.Y);
            MarqueeWidth = Math.Abs(current.X - _marqueeStart.X);
            MarqueeHeight = Math.Abs(current.Y - _marqueeStart.Y);
        }

        private void FloodFill(SKPoint point)
        {
            var x0 = (int)point.X;
            var y0 = (int)point.Y;
            if (x0 < 0 || y0 < 0 || x0 >= _bitmap.Width || y0 >= _bitmap.Height)
                return;

            var options = _toolbox.CurrentToolOptions as FillToolOptionsViewModel;
            var tolerance = (float)(options?.Tolerance ?? 32);
            var contiguous = options?.Contiguous ?? true;

            var width = _bitmap.Width;
            var height = _bitmap.Height;
            var pixels = _bitmap.Pixels;
            var targetColor = pixels[y0 * width + x0];
            var fillColor = BrushColor;

            if (ColorsClose(targetColor, fillColor, 0))
                return;

            if (contiguous)
                FloodFillContiguous(pixels, width, height, x0, y0, targetColor, fillColor, tolerance);
            else
                FloodFillGlobal(pixels, targetColor, fillColor, tolerance);

            _bitmap.Pixels = pixels;
        }

        /// <summary>
        /// Stack-based flood fill, seeded from (x0, y0). Each pixel is marked visited
        /// the moment it's pushed - not when it's popped - so it can only ever enter the
        /// stack once. The earlier version checked "already visited?" only after
        /// popping, which let up to 4 duplicate entries pile up per pixel before being
        /// discarded; on a large uniform region (a fresh, blank 1600x1200 canvas is
        /// exactly that) the stack could balloon into the tens of millions of entries,
        /// a real, noticeable freeze that looked like the tool wasn't working at all.
        /// </summary>
        private static void FloodFillContiguous(
            SKColor[] pixels, int width, int height, int x0, int y0,
            SKColor targetColor, SKColor fillColor, float tolerance)
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
                if (visited[index] || !ColorsClose(pixels[index], targetColor, tolerance))
                    return;

                visited[index] = true;
                stack.Push((x, y));
            }
        }

        private static void FloodFillGlobal(SKColor[] pixels, SKColor targetColor, SKColor fillColor, float tolerance)
        {
            for (var i = 0; i < pixels.Length; i++)
            {
                if (ColorsClose(pixels[i], targetColor, tolerance))
                    pixels[i] = fillColor;
            }
        }

        private static bool ColorsClose(SKColor a, SKColor b, float tolerance)
        {
            var dr = a.Red - b.Red;
            var dg = a.Green - b.Green;
            var db = a.Blue - b.Blue;
            var da = a.Alpha - b.Alpha;
            return Math.Sqrt(dr * dr + dg * dg + db * db + da * da) <= tolerance;
        }

        private void ApplyGradient(SKPoint point)
        {
            var options = _toolbox.CurrentToolOptions as GradientToolOptionsViewModel;
            var angleDegrees = options?.Angle ?? 90;
            var reverse = options?.Reverse ?? false;

            var startColor = reverse ? SKColors.Transparent : BrushColor;
            var endColor = reverse ? BrushColor : SKColors.Transparent;

            var radians = angleDegrees * Math.PI / 180.0;
            var directionX = (float)Math.Cos(radians);
            var directionY = (float)Math.Sin(radians);
            var reach = Math.Max(_bitmap.Width, _bitmap.Height);

            var start = new SKPoint(point.X - directionX * reach, point.Y - directionY * reach);
            var end = new SKPoint(point.X + directionX * reach, point.Y + directionY * reach);

            using var shader = SKShader.CreateLinearGradient(start, end, new[] { startColor, endColor }, null, SKShaderTileMode.Clamp);
            using var paint = new SKPaint { Shader = shader };

            _bitmapCanvas.DrawRect(new SKRect(0, 0, _bitmap.Width, _bitmap.Height), paint);
        }

        private void RaiseInvalidate() => InvalidateRequested?.Invoke(this, EventArgs.Empty);

        partial void OnPanXChanged(double value) => RaiseInvalidate();

        partial void OnPanYChanged(double value) => RaiseInvalidate();

        partial void OnZoomChanged(double value) => RaiseInvalidate();

        partial void OnIsMarqueeActiveChanged(bool value) => RaiseInvalidate();

        partial void OnMarqueeXChanged(double value) => RaiseInvalidate();

        partial void OnMarqueeYChanged(double value) => RaiseInvalidate();

        partial void OnMarqueeWidthChanged(double value) => RaiseInvalidate();

        partial void OnMarqueeHeightChanged(double value) => RaiseInvalidate();

        public void Dispose()
        {
            if (_disposed) return;

            _toolbox.PropertyChanged -= OnToolboxPropertyChanged;
            _bitmapCanvas.Dispose();
            _bitmap.Dispose();
            _disposed = true;
        }
    }
}
