using System;
using System.Collections.Generic;
using JolieCat.Core.Documents;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Shared.Enums;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    /// <summary>
    /// Rulers, grid and guides (Photoshop's View menu): rulers along the top and left
    /// of the view, a grid over the document, and guide lines that marquee, crop,
    /// shape and gradient drags snap to. Drag from a ruler to make a guide; with the
    /// Move tool, drag a guide to move it or back onto a ruler to remove it.
    /// </summary>
    public partial class CanvasViewModel
    {
        /// <summary>Ruler thickness, in device pixels.</summary>
        public const float RulerSize = 20;

        /// <summary>How close (in device pixels) a point must be to a guide to snap.</summary>
        public const float SnapDistance = 8;

        [ObservableProperty]
        private bool showRulers;

        [ObservableProperty]
        private bool showGrid;

        /// <summary>Grid spacing in document pixels.</summary>
        [ObservableProperty]
        private int gridSize = 50;

        [ObservableProperty]
        private bool showGuides = true;

        [ObservableProperty]
        private bool snapToGuides = true;

        /// <summary>The document's guides - stored on the scene, so they're saved with it.</summary>
        public IList<Guide> Guides => Layers.Scene.Guides;

        /// <summary>The guide being dragged (shown while dragging), if any.</summary>
        public Guide? DraggedGuide { get; private set; }

        partial void OnShowRulersChanged(bool value) => RaiseInvalidate();
        partial void OnShowGridChanged(bool value) => RaiseInvalidate();
        partial void OnGridSizeChanged(int value) => RaiseInvalidate();
        partial void OnShowGuidesChanged(bool value) => RaiseInvalidate();

        [RelayCommand]
        private void ToggleRulers() => ShowRulers = !ShowRulers;

        [RelayCommand]
        private void ToggleGrid() => ShowGrid = !ShowGrid;

        [RelayCommand]
        private void ToggleGuides() => ShowGuides = !ShowGuides;

        [RelayCommand]
        private void ClearGuides()
        {
            Guides.Clear();
            RaiseInvalidate();
        }

        public void AddGuide(bool isVertical, float position)
        {
            var limit = isVertical ? Layers.DocumentWidth : Layers.DocumentHeight;
            Guides.Add(new Guide(isVertical, Math.Clamp(position, 0, limit)));
            ShowGuides = true;
            RaiseInvalidate();
        }

        // ----- Snapping -----

        private static bool SnapsForTool(ToolType tool) => tool is
            ToolType.RectangularMarquee or ToolType.EllipticalMarquee or ToolType.Crop or ToolType.Shape or ToolType.Gradient;

        /// <summary>Pulls <paramref name="doc"/> onto a nearby guide, grid line (when the
        /// grid is shown) or document edge - each axis on its own.</summary>
        public SKPoint Snap(SKPoint doc)
        {
            if (!SnapToGuides) return doc;
            var reach = SnapDistance / (float)Math.Max(Zoom, 0.01);

            float SnapAxis(float value, bool vertical, int documentSize)
            {
                var best = value;
                var bestDistance = reach;
                void Try(float candidate)
                {
                    var d = Math.Abs(candidate - value);
                    if (d < bestDistance) { bestDistance = d; best = candidate; }
                }

                if (ShowGuides)
                    foreach (var guide in Guides)
                        if (guide.IsVertical == vertical) Try(guide.Position);
                if (ShowGrid && GridSize > 0)
                    Try((float)(Math.Round(value / GridSize) * GridSize));
                Try(0);
                Try(documentSize);
                return best;
            }

            return new SKPoint(SnapAxis(doc.X, true, Layers.DocumentWidth), SnapAxis(doc.Y, false, Layers.DocumentHeight));
        }

        /// <summary>Move tool: nudges <paramref name="delta"/> so the moved content's left,
        /// center or right edge (and top, middle or bottom) lands on a nearby guide, grid
        /// line, document edge or document center. Only while there are guides or the
        /// grid is shown, so ordinary moves aren't pulled around.</summary>
        internal SKPointI SnapMoveDelta(SKRect content, SKPointI delta)
        {
            if (!SnapToGuides || content.IsEmpty || !((ShowGuides && Guides.Count > 0) || ShowGrid)) return delta;
            var reach = SnapDistance / (float)Math.Max(Zoom, 0.01);

            int SnapAxis(bool vertical, float low, float high, int shift, int documentSize)
            {
                var targets = new List<float> { 0, documentSize, documentSize / 2f };
                if (ShowGuides) targets.AddRange(Guides.Where(g => g.IsVertical == vertical).Select(g => g.Position));
                var best = shift;
                var bestDistance = reach;
                foreach (var edge in new[] { low, (low + high) / 2, high })
                {
                    var moved = edge + shift;
                    var candidates = targets.AsEnumerable();
                    if (ShowGrid && GridSize > 0) candidates = candidates.Append(MathF.Round(moved / GridSize) * GridSize);
                    foreach (var target in candidates)
                    {
                        var d = Math.Abs(target - moved);
                        if (d < bestDistance)
                        {
                            bestDistance = d;
                            best = shift + (int)MathF.Round(target - moved);
                        }
                    }
                }
                return best;
            }

            return new SKPointI(
                SnapAxis(true, content.Left, content.Right, delta.X, Layers.DocumentWidth),
                SnapAxis(false, content.Top, content.Bottom, delta.Y, Layers.DocumentHeight));
        }

        private SKPoint SnapForActiveTool(SKPoint doc) =>
            (ShowGuides && Guides.Count > 0) || ShowGrid ? (SnapsForTool(_toolbox.ActiveTool.Type) ? Snap(doc) : doc) : doc;

        // ----- Dragging guides -----

        /// <summary>Starts dragging a new guide out of a ruler, or (Move tool) an existing
        /// guide. Returns true if the press was used.</summary>
        private bool TryBeginGuideDrag(SKPoint devicePoint)
        {
            if (ShowRulers && !IsViewRotated && (devicePoint.Y < RulerSize || devicePoint.X < RulerSize))
            {
                if (devicePoint.Y < RulerSize && devicePoint.X < RulerSize) return true; // the corner
                // From the top ruler comes a horizontal guide, from the left one a vertical.
                var vertical = devicePoint.X < RulerSize;
                var doc = ToDocumentSpace(devicePoint);
                DraggedGuide = new Guide(vertical, vertical ? doc.X : doc.Y);
                RaiseInvalidate();
                return true;
            }

            if (ShowGuides && _toolbox.ActiveTool.Type == ToolType.Move && GuideNear(devicePoint) is { } guide)
            {
                Guides.Remove(guide);
                DraggedGuide = guide;
                RaiseInvalidate();
                return true;
            }

            return false;
        }

        private Guide? GuideNear(SKPoint devicePoint)
        {
            var doc = ToDocumentSpace(devicePoint);
            var reach = 4 / (float)Math.Max(Zoom, 0.01);
            return Guides
                .Select(g => (Guide: g, Distance: Math.Abs((g.IsVertical ? doc.X : doc.Y) - g.Position)))
                .Where(x => x.Distance <= reach)
                .OrderBy(x => x.Distance)
                .Select(x => x.Guide)
                .FirstOrDefault();
        }

        private bool UpdateGuideDrag(SKPoint devicePoint)
        {
            if (DraggedGuide is null) return false;
            var doc = ToDocumentSpace(devicePoint);
            var position = DraggedGuide.IsVertical ? doc.X : doc.Y;
            // Guides snap to whole pixels, and to the grid when it's shown.
            position = ShowGrid && GridSize > 0 && Math.Abs(position - MathF.Round(position / GridSize) * GridSize) * Zoom < SnapDistance
                ? MathF.Round(position / GridSize) * GridSize
                : MathF.Round(position);
            DraggedGuide = DraggedGuide with { Position = position };
            ToolHintText = $"{(DraggedGuide.IsVertical ? "Vertical" : "Horizontal")} guide at {position:0} px - drop it on a ruler to remove it";
            RaiseInvalidate();
            return true;
        }

        private bool EndGuideDrag(SKPoint devicePoint)
        {
            if (DraggedGuide is null) return false;
            UpdateGuideDrag(devicePoint);
            var guide = DraggedGuide;
            DraggedGuide = null;

            // Dropped back on a ruler (or outside the document): removed.
            var onRuler = ShowRulers && (devicePoint.X < RulerSize || devicePoint.Y < RulerSize);
            var limit = guide.IsVertical ? Layers.DocumentWidth : Layers.DocumentHeight;
            if (!onRuler && guide.Position >= 0 && guide.Position <= limit)
                Guides.Add(guide);
            ToolHintText = string.Empty;
            RaiseInvalidate();
            return true;
        }
    }
}
