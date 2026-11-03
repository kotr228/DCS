using System;
using System.Linq;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    /// <summary>
    /// The Perspective Crop tool: four corner handles (starting at the document's
    /// corners) that you drag onto the corners of something seen at an angle - a page,
    /// a sign, a painting. Enter straightens that area into a rectangle and crops to
    /// it; Esc puts the corners back. Dragging outside the handles draws a new
    /// rectangle to start from.
    /// </summary>
    public partial class CanvasViewModel
    {
        private SKPoint[]? _perspectiveQuad;
        private int _perspectiveCorner = -1;
        private bool _perspectiveDrawing;
        private SKPoint _perspectiveStart;

        /// <summary>The four corners (top-left, top-right, bottom-right, bottom-left) while
        /// the tool is active - drawn by the renderer.</summary>
        public SKPoint[]? PerspectiveQuad => _perspectiveQuad;

        private void StartPerspectiveCrop()
        {
            float w = Layers.DocumentWidth, h = Layers.DocumentHeight;
            _perspectiveQuad = new[] { new SKPoint(0, 0), new SKPoint(w, 0), new SKPoint(w, h), new SKPoint(0, h) };
            _perspectiveCorner = -1;
            _perspectiveDrawing = false;
            ToolHintText = "Drag the corners onto the corners of what to straighten, then press Enter";
            RaiseInvalidate();
        }

        private bool BeginPerspectiveDrag(SKPoint doc)
        {
            if (_perspectiveQuad is null) StartPerspectiveCrop();
            var quad = _perspectiveQuad!;
            var reach = 10 / (float)Math.Max(Zoom, 0.01);
            _perspectiveCorner = Enumerable.Range(0, 4)
                .Where(i => SKPoint.Distance(quad[i], doc) <= reach)
                .OrderBy(i => SKPoint.Distance(quad[i], doc))
                .DefaultIfEmpty(-1)
                .First();
            if (_perspectiveCorner < 0)
            {
                _perspectiveDrawing = true;
                _perspectiveStart = doc;
            }
            return true;
        }

        private bool UpdatePerspectiveDrag(SKPoint doc)
        {
            if (_perspectiveQuad is null || (_perspectiveCorner < 0 && !_perspectiveDrawing)) return false;
            if (_perspectiveCorner >= 0)
            {
                _perspectiveQuad[_perspectiveCorner] = doc;
            }
            else
            {
                var r = SKRect.Create(_perspectiveStart, new SKSize(doc.X - _perspectiveStart.X, doc.Y - _perspectiveStart.Y)).Standardized;
                _perspectiveQuad = new[] { new SKPoint(r.Left, r.Top), new SKPoint(r.Right, r.Top), new SKPoint(r.Right, r.Bottom), new SKPoint(r.Left, r.Bottom) };
            }
            RaiseInvalidate();
            return true;
        }

        private void EndPerspectiveDrag()
        {
            _perspectiveCorner = -1;
            _perspectiveDrawing = false;
        }

        /// <summary>Straightens and crops (Enter). A no-op unless the tool is active and
        /// its corners make a usable shape.</summary>
        public void CommitPerspectiveCrop()
        {
            if (_perspectiveQuad is not { } quad) return;
            _perspectiveQuad = null;
            EndPerspectiveDrag();

            // Corners still on the document's own corners: nothing to do.
            float w = Layers.DocumentWidth, h = Layers.DocumentHeight;
            var untouched = quad[0] == new SKPoint(0, 0) && quad[1] == new SKPoint(w, 0) && quad[2] == new SKPoint(w, h) && quad[3] == new SKPoint(0, h);
            if (!untouched && IsUsableQuad(quad))
            {
                var size = Core.Documents.Scene.PerspectiveCropSize(quad);
                Layers.PerspectiveCropDocument(quad, size.Width, size.Height);
            }

            ToolHintText = string.Empty;

            // Still on the tool (Enter, not a tool switch): ready for the next one.
            if (_toolbox.ActiveTool.Type == Shared.Enums.ToolType.PerspectiveCrop) StartPerspectiveCrop();
            RaiseInvalidate();
        }

        /// <summary>Esc: back to the document's corners.</summary>
        public void CancelPerspectiveCrop()
        {
            if (_perspectiveQuad is null) return;
            StartPerspectiveCrop();
        }

        /// <summary>The corners must make a convex shape with some area (not crossed or flat).</summary>
        internal static bool IsUsableQuad(SKPoint[] q)
        {
            var sign = 0;
            for (var i = 0; i < 4; i++)
            {
                var a = q[i];
                var b = q[(i + 1) % 4];
                var c = q[(i + 2) % 4];
                var cross = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
                if (Math.Abs(cross) < 1) return false;
                var s = Math.Sign(cross);
                if (sign == 0) sign = s;
                else if (s != sign) return false;
            }
            return true;
        }
    }
}
