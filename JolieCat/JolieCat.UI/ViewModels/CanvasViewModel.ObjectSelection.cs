using System;
using System.Windows.Input;
using JolieCat.Core.Documents;
using JolieCat.Core.Rendering;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    /// <summary>The Object Selection tool: drag a box around a thing; the thing inside is selected.</summary>
    public partial class CanvasViewModel
    {
        private SKPoint? _objectBoxStart;
        private SKPoint _objectBoxEnd;

        /// <summary>The box being dragged with the Object Selection tool (for the canvas), or null.</summary>
        public SKRect? ObjectBoxDraft => _objectBoxStart is { } s
            ? new SKRect(Math.Min(s.X, _objectBoxEnd.X), Math.Min(s.Y, _objectBoxEnd.Y), Math.Max(s.X, _objectBoxEnd.X), Math.Max(s.Y, _objectBoxEnd.Y))
            : null;

        private bool PressObjectSelection(SKPoint doc)
        {
            _objectBoxStart = doc;
            _objectBoxEnd = doc;
            return true;
        }

        private bool MoveObjectSelection(SKPoint doc)
        {
            if (_objectBoxStart is null) return false;
            _objectBoxEnd = doc;
            RaiseInvalidate();
            return true;
        }

        private void ReleaseObjectSelection()
        {
            if (ObjectBoxDraft is not { } box) return;
            _objectBoxStart = null;
            RaiseInvalidate();
            var mode = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? SelectionCombine.Add
                : (Keyboard.Modifiers & ModifierKeys.Alt) != 0 ? SelectionCombine.Subtract
                : SelectionCombine.New;
            if (!SelectObjectIn(box, mode)) ToolHintText = "Nothing stood out in that box - draw it a little bigger, around the whole thing";
        }

        /// <summary>
        /// Selects the thing inside <paramref name="box"/> (image pixels): the box's edges are
        /// taken as background, so draw it loosely around the whole thing. Combined with the
        /// selection by <paramref name="mode"/>. False if nothing stood out.
        /// </summary>
        public bool SelectObjectIn(SKRect box, SelectionCombine mode = SelectionCombine.New)
        {
            var area = SKRectI.Round(box);
            area.Intersect(new SKRectI(0, 0, Layers.DocumentWidth, Layers.DocumentHeight));
            if (area.Width < 8 || area.Height < 8) return false;
            using var flat = SceneCompositor.Flatten(Layers.Scene, Layers.DocumentWidth, Layers.DocumentHeight);
            using var crop = SpriteSheetTools.Cell(flat, area);
            if (SubjectSelect.Find(crop) is not { } mask) return false;
            var path = SelectionOps.FromMask(crop.Width, crop.Height, i => mask[i] >= 128);
            if (path is null) return false;
            path.Transform(SKMatrix.CreateTranslation(area.Left, area.Top));
            ApplySelectionPath(path, mode);
            return true;
        }
    }
}
