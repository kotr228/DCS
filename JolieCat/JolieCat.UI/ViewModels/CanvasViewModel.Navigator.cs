using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    /// <summary>What the Navigator panel needs from the view: the visible part of the
    /// document, and moving the view to a point.</summary>
    public partial class CanvasViewModel
    {
        public double ViewportWidth => _viewportWidth;

        public double ViewportHeight => _viewportHeight;

        /// <summary>The part of the document the canvas shows now (document pixels).</summary>
        public SKRect VisibleDocumentArea
        {
            get
            {
                var a = ToDocumentSpace(new SKPoint(0, 0));
                var b = ToDocumentSpace(new SKPoint((float)_viewportWidth, (float)_viewportHeight));
                return new SKRect(System.Math.Min(a.X, b.X), System.Math.Min(a.Y, b.Y), System.Math.Max(a.X, b.X), System.Math.Max(a.Y, b.Y));
            }
        }

        /// <summary>Pans so <paramref name="doc"/> is in the middle of the view (Navigator click/drag).</summary>
        public void CenterViewOn(SKPoint doc)
        {
            if (_viewportWidth <= 0 || _viewportHeight <= 0) return;
            MarkViewChangedByUser();
            PanX = _viewportWidth / 2 - doc.X * Zoom;
            PanY = _viewportHeight / 2 - doc.Y * Zoom;
            RaiseInvalidate();
        }

        /// <summary>Zooms around the view's center (Navigator zoom slider).</summary>
        public void ZoomFromNavigator(double zoom)
        {
            MarkViewChangedByUser();
            SetZoomAroundViewportCenter(System.Math.Clamp(zoom, 0.01, 32));
        }
    }
}
