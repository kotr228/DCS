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
        // ================= Marquee / Lasso / Polygon selection =================

        private void UpdateLiveMarqueePath(SKPoint current)
        {
            var start = _marqueeStart;

            // Sprite Sheet grid snapping: both the drag's start and current points snap
            // independently to the nearest grid line, so a marquee drawn anywhere near a
            // cell's edges lands exactly on them - the "snap selections to the grid
            // intersections" behavior for a SpriteSheet project, gated on both the
            // project type and the panel's own toggle (see LayersViewModel.SnapToSpriteSheetGrid).
            if (Layers.IsSpriteSheetProject && Layers.SnapToSpriteSheetGrid)
            {
                var grid = Layers.SpriteSheetGrid;
                start = grid.SnapPoint(start, Layers.DocumentWidth, Layers.DocumentHeight);
                current = grid.SnapPoint(current, Layers.DocumentWidth, Layers.DocumentHeight);
            }

            var x = Math.Min(start.X, current.X);
            var y = Math.Min(start.Y, current.Y);
            var width = Math.Abs(current.X - start.X);
            var height = Math.Abs(current.Y - start.Y);
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
            ApplySelectionShape(path);
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
                CaptureSelectionCombine();
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

            CaptureSelectionCombine();
            using var region = Selection.CreateRegionFromColorFlood(layer.Bitmap, (int)point.X, (int)point.Y, tolerance);
            if (region.IsEmpty)
            {
                RaiseInvalidate();
                return;
            }

            // A flood fill is never "too small" - even one pixel is a deliberate pick.
            Layers.Scene.Selection.Combine(region.GetBoundaryPath(), _selectionCombine, Layers.DocumentWidth, Layers.DocumentHeight);
            if (Layers.Scene.Selection.HasSelection && SelectionOptions is { FeatherRadius: > 0 } featherOptions)
                Layers.Scene.Selection.Feather = (float)featherOptions.FeatherRadius;
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
    }
}
