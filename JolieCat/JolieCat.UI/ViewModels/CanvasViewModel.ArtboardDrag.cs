using System;
using System.Collections.Generic;
using System.Linq;
using JolieCat.Core.Documents;
using JolieCat.Shared.Enums;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    /// <summary>Moving an artboard with the Move tool by dragging its name label.</summary>
    public partial class CanvasViewModel
    {
        private int _draggedArtboard = -1;
        private SKPoint _artboardGrab;
        private SKRectI _artboardStart;
        private List<Artboard>? _artboardsBefore;

        /// <summary>Where an artboard's name label is drawn, in document pixels (shared with the renderer).</summary>
        public static SKRect ArtboardLabelRect(Artboard artboard, double zoom)
        {
            var s = 1f / (float)Math.Max(zoom, 0.01);
            using var font = new SKFont(SKTypeface.Default, 12 * s);
            var width = font.MeasureText($"{artboard.Name}  {artboard.Bounds.Width}×{artboard.Bounds.Height}");
            float left = artboard.Bounds.Left, top = artboard.Bounds.Top;
            var baseline = top - 5 * s;
            if (baseline - 12 * s < 0) baseline = top + 15 * s;
            return new SKRect(left, baseline - 12 * s, left + width + 8 * s, baseline + 4 * s);
        }

        private bool TryBeginArtboardDrag(SKPoint devicePoint)
        {
            if (_toolbox.ActiveTool.Type != ToolType.Move) return false;
            var doc = ToDocumentSpace(devicePoint);
            var artboards = Layers.Scene.Artboards;
            for (var i = artboards.Count - 1; i >= 0; i--)
            {
                if (!ArtboardLabelRect(artboards[i], Zoom).Contains(doc)) continue;
                _draggedArtboard = i;
                _artboardGrab = doc;
                _artboardStart = artboards[i].Bounds;
                _artboardsBefore = artboards.ToList();
                ToolHintText = $"Moving artboard \"{artboards[i].Name}\"";
                return true;
            }
            return false;
        }

        private bool UpdateArtboardDrag(SKPoint devicePoint)
        {
            if (_draggedArtboard < 0) return false;
            var doc = ToDocumentSpace(devicePoint);
            var artboards = Layers.Scene.Artboards;
            if (_draggedArtboard >= artboards.Count) return true;
            var start = _artboardStart;
            // Whole pixels, and kept on the canvas.
            var x = Math.Clamp((int)Math.Round(start.Left + doc.X - _artboardGrab.X), 0, Math.Max(0, Layers.DocumentWidth - start.Width));
            var y = Math.Clamp((int)Math.Round(start.Top + doc.Y - _artboardGrab.Y), 0, Math.Max(0, Layers.DocumentHeight - start.Height));
            artboards[_draggedArtboard] = artboards[_draggedArtboard] with { Bounds = SKRectI.Create(x, y, start.Width, start.Height) };
            RaiseInvalidate();
            return true;
        }

        private bool EndArtboardDrag()
        {
            if (_draggedArtboard < 0) return false;
            _draggedArtboard = -1;
            if (_artboardsBefore is { } before && !before.SequenceEqual(Layers.Scene.Artboards))
            {
                Layers.History.Push(new Core.History.ArtboardsCommand(Layers.Scene, before, Layers.Scene.Artboards), "Move Artboard");
            }
            _artboardsBefore = null;
            RaiseInvalidate();
            return true;
        }
    }
}
