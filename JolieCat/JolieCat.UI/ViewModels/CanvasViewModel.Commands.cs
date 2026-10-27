using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.History;
using JolieCat.UI.ViewModels.ToolOptions;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    /// <summary>
    /// Menu/keyboard commands that act on the canvas as a whole: selection
    /// (all/none/invert/delete/fill), view (zoom in/out, fit, 100%), and brush size.
    /// </summary>
    public partial class CanvasViewModel
    {
        /// <summary>Zoom steps used by Zoom In/Out, matching common editor presets.</summary>
        private static readonly double[] ZoomSteps =
            { 0.05, 0.1, 0.125, 0.167, 0.25, 0.333, 0.5, 0.667, 1, 1.5, 2, 3, 4, 6, 8, 12, 16, 24, 32 };

        private const double FitMargin = 24;

        private double _viewportWidth;
        private double _viewportHeight;

        /// <summary>True until the canvas knows its own size and has fitted the document
        /// into it once - set again by <see cref="ResetView"/>.</summary>
        private bool _fitPending = true;

        /// <summary>"50%" etc. for the status bar.</summary>
        public string ZoomText => $"{Zoom * 100:0.#}%";

        /// <summary>Called by the view with the canvas surface's size in device pixels.
        /// Performs the pending "fit to window" the first time a size is known.</summary>
        public void SetViewportSize(double width, double height)
        {
            if (width <= 0 || height <= 0) return;

            var changed = Math.Abs(width - _viewportWidth) > 0.5 || Math.Abs(height - _viewportHeight) > 0.5;
            _viewportWidth = width;
            _viewportHeight = height;

            // Re-fit while the view is still the automatic fit (the window is often
            // still settling its size right after opening); once the user zooms or
            // pans, their view is left alone.
            if (_fitPending || (changed && _isAutoFitView))
            {
                _fitPending = false;
                ZoomToFit();
            }
        }

        /// <summary>True while the current view came from <see cref="ZoomToFit"/> and
        /// the user hasn't zoomed or panned since.</summary>
        private bool _isAutoFitView;

        /// <summary>Called by every user-driven zoom/pan.</summary>
        private void MarkViewChangedByUser() => _isAutoFitView = false;

        [RelayCommand]
        public void ZoomToFit()
        {
            if (_viewportWidth <= 0 || _viewportHeight <= 0)
            {
                _fitPending = true;
                return;
            }

            var docW = Layers.DocumentWidth;
            var docH = Layers.DocumentHeight;
            var fit = Math.Min((_viewportWidth - FitMargin * 2) / docW, (_viewportHeight - FitMargin * 2) / docH);
            SetZoomCentered(fit);
            _isAutoFitView = true;
        }

        [RelayCommand]
        public void ZoomActualPixels()
        {
            MarkViewChangedByUser();
            SetZoomAroundViewportCenter(1.0);
        }

        [RelayCommand]
        public void ZoomIn()
        {
            MarkViewChangedByUser();
            foreach (var step in ZoomSteps)
            {
                if (step > Zoom + 1e-6)
                {
                    SetZoomAroundViewportCenter(step);
                    return;
                }
            }
        }

        [RelayCommand]
        public void ZoomOut()
        {
            MarkViewChangedByUser();
            for (var i = ZoomSteps.Length - 1; i >= 0; i--)
            {
                if (ZoomSteps[i] < Zoom - 1e-6)
                {
                    SetZoomAroundViewportCenter(ZoomSteps[i]);
                    return;
                }
            }
        }

        /// <summary>Sets the zoom and centers the whole document in the viewport.</summary>
        private void SetZoomCentered(double zoom)
        {
            zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
            Zoom = zoom;
            PanX = SnapToPixel((_viewportWidth - Layers.DocumentWidth * zoom) / 2);
            PanY = SnapToPixel((_viewportHeight - Layers.DocumentHeight * zoom) / 2);
        }

        /// <summary>Changes zoom while keeping the document point at the viewport's
        /// center fixed on screen.</summary>
        private void SetZoomAroundViewportCenter(double zoom)
        {
            zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
            var center = new SKPoint((float)(_viewportWidth / 2), (float)(_viewportHeight / 2));
            var doc = ToDocumentSpace(center);
            Zoom = zoom;
            PanX = SnapToPixel(center.X - doc.X * zoom);
            PanY = SnapToPixel(center.Y - doc.Y * zoom);
        }

        // ================= Temporary panning =================

        /// <summary>True while Space is held: a left-drag then pans instead of using the
        /// active tool, the same as Photoshop.</summary>
        public bool IsSpacePanArmed { get; set; }

        /// <summary>Starts a pan gesture regardless of the active tool (middle mouse
        /// button, or Space + left drag). Ends on pointer release.</summary>
        public void BeginPan(SKPoint devicePoint)
        {
            MarkViewChangedByUser();
            _isPanning = true;
            _panDragStart = devicePoint;
            _panStartX = PanX;
            _panStartY = PanY;
        }

        public void EndPan() => _isPanning = false;

        // ================= Selection commands =================

        /// <summary>Select &gt; Color Range: selects every pixel of the visible image
        /// close to <paramref name="color"/> (fuzziness 0-255 as color distance).</summary>
        public void SelectColorRange(SKColor color, float fuzziness)
        {
            using var flat = Core.Rendering.SceneCompositor.Flatten(Layers.Scene, Layers.DocumentWidth, Layers.DocumentHeight);
            var region = Core.Documents.Selection.CreateRegionFromColor(flat.Pixels, flat.Width, flat.Height, color.WithAlpha(255), fuzziness);
            if (region.IsEmpty)
                Layers.Scene.Selection.Clear();
            else
                Layers.Scene.Selection.SetRegion(region);
            RaiseInvalidate();
        }

        [RelayCommand]
        public void SelectAll()
        {
            Layers.Scene.Selection.SelectAll(Layers.DocumentWidth, Layers.DocumentHeight);
            RaiseInvalidate();
        }

        [RelayCommand]
        public void Deselect()
        {
            if (Layers.Scene.Selection.Path is { } path)
                _lastSelectionPath = new SKPath(path);

            Layers.Scene.Selection.Clear();
            RaiseInvalidate();
        }

        /// <summary>The selection that was active before the last Deselect.</summary>
        private SKPath? _lastSelectionPath;

        /// <summary>Select &gt; Reselect (Ctrl+Shift+D): brings back the last selection.</summary>
        [RelayCommand]
        public void Reselect()
        {
            if (_lastSelectionPath is null) return;

            Layers.Scene.Selection.SetPath(new SKPath(_lastSelectionPath), Layers.DocumentWidth, Layers.DocumentHeight);
            RaiseInvalidate();
        }

        /// <summary>Select &gt; Modify: Expand, Contract, Border or Smooth by
        /// <paramref name="pixels"/>. No-op without a selection.</summary>
        public void ModifySelection(SelectionModifyKind kind, float pixels)
        {
            var selection = Layers.Scene.Selection;
            if (!selection.HasSelection || pixels <= 0) return;

            int w = Layers.DocumentWidth, h = Layers.DocumentHeight;
            switch (kind)
            {
                case SelectionModifyKind.Expand: selection.Expand(pixels, w, h); break;
                case SelectionModifyKind.Contract: selection.Contract(pixels, w, h); break;
                case SelectionModifyKind.Border: selection.Border(pixels, w, h); break;
                case SelectionModifyKind.Smooth: selection.Smooth(pixels, w, h); break;
                case SelectionModifyKind.Feather: selection.Feather = pixels; break;
            }

            RaiseInvalidate();
        }

        public bool HasSelection => Layers.Scene.Selection.HasSelection;

        [RelayCommand]
        public void InvertSelection()
        {
            Layers.Scene.Selection.Invert(Layers.DocumentWidth, Layers.DocumentHeight);
            RaiseInvalidate();
        }

        /// <summary>Clears the selected pixels of the active layer (or its mask) to
        /// transparent - the whole layer when nothing is selected. Undoable.</summary>
        [RelayCommand]
        public void DeleteSelectedPixels() => EditActiveLayerPixels("Clear", canvas =>
        {
            using var paint = new SKPaint { BlendMode = SKBlendMode.Clear };
            canvas.DrawPaint(paint);
        });

        /// <summary>Fills the selection (or whole layer) with the primary color. Undoable.</summary>
        [RelayCommand]
        public void FillSelection() => EditActiveLayerPixels("Fill", canvas =>
        {
            using var paint = new SKPaint { Color = BrushColor, BlendMode = SKBlendMode.SrcOver };
            canvas.DrawPaint(paint);
        });

        /// <summary>Ctrl+Backspace: fills the selection (or layer) with the background color.</summary>
        [RelayCommand]
        public void FillSelectionWithBackground() => EditActiveLayerPixels("Fill", canvas =>
        {
            using var paint = new SKPaint { Color = SecondaryBrushColor, BlendMode = SKBlendMode.SrcOver };
            canvas.DrawPaint(paint);
        });

        // ================= Foreground / background colors =================

        /// <summary>The background color (Photoshop's second swatch): used by
        /// Ctrl+Backspace fills and the gradient's end color.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SecondaryColorBrush))]
        private System.Windows.Media.Color secondaryColor = System.Windows.Media.Colors.White;

        public System.Windows.Media.SolidColorBrush SecondaryColorBrush => new(SecondaryColor);

        public SKColor SecondaryBrushColor => new(SecondaryColor.R, SecondaryColor.G, SecondaryColor.B, SecondaryColor.A);

        /// <summary>X: swaps the foreground and background colors.</summary>
        [RelayCommand]
        public void SwapColors() => (PrimaryColor, SecondaryColor) = (SecondaryColor, PrimaryColor);

        /// <summary>D: default colors - black foreground, white background.</summary>
        [RelayCommand]
        public void ResetColors()
        {
            PrimaryColor = System.Windows.Media.Colors.Black;
            SecondaryColor = System.Windows.Media.Colors.White;
        }

        private void EditActiveLayerPixels(string historyName, Action<SKCanvas> draw)
        {
            var layer = Layers.ActiveLayer?.Model;
            if (layer is null || layer.IsLocked || (layer.IsGroup && !layer.IsMaskActive)) return;
            RasterizeIfText(layer);

            var before = layer.PaintBitmap.Pixels;
            WithSelectionClip(layer.PaintCanvas, draw);
            Layers.History.Push(PixelEdit(layer, before), historyName);
            RaiseInvalidate();
        }

        /// <summary>The undo entry for a pixel edit already made to <paramref name="layer"/>:
        /// its mask if the mask is the active paint target (unless
        /// <paramref name="contentOnly"/>), else its own content. Resolved by layer Id at
        /// undo time, so it survives structural undo/redo.</summary>
        private LayerPixelsCommand PixelEdit(Core.Documents.Layer layer, SKColor[] before, bool contentOnly = false)
        {
            var isMask = !contentOnly && layer.IsMaskActive && layer.Mask is not null;
            var target = isMask ? layer.Mask!.Bitmap : layer.Bitmap;
            return new LayerPixelsCommand(Layers.Scene, layer, isMask, before, target.Pixels);
        }

        // ================= Brush size =================

        /// <summary>[ and ] : brush size steps that scale with the current size, so
        /// both a 3 px and a 300 px brush change at a useful rate.</summary>
        public void AdjustBrushSize(int direction)
        {
            if (_toolbox.CurrentToolOptions is not PaintToolOptionsViewModel options) return;

            var step = options.Size < 10 ? 1 : options.Size < 50 ? 5 : options.Size < 100 ? 10 : 25;
            options.Size = Math.Clamp(options.Size + direction * step, 1, 500);
        }
    }

    public enum SelectionModifyKind
    {
        Expand,
        Contract,
        Border,
        Smooth,
        Feather,
    }
}
