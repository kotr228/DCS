using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels.ToolOptions;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    /// <summary>The Ruler tool: measure a line; Straighten Layer levels it.</summary>
    public partial class CanvasViewModel
    {
        private bool _isDrawingRuler;

        /// <summary>The measured line in image pixels, or null.</summary>
        public (SKPoint Start, SKPoint End)? RulerLine { get; private set; }

        private RulerToolOptionsViewModel RulerOptions
        {
            get
            {
                var options = (RulerToolOptionsViewModel)_toolbox.OptionsFor(ToolType.Ruler);
                options.StraightenCommand ??= new RelayCommand(() => StraightenLayerToRuler());
                options.ClearCommand ??= new RelayCommand(ClearRuler);
                return options;
            }
        }

        private bool PressRuler(SKPoint doc)
        {
            _isDrawingRuler = true;
            RulerLine = (doc, doc);
            RulerOptions.Show(doc.X, doc.Y, doc.X, doc.Y);
            RaiseInvalidate();
            return true;
        }

        private bool MoveRuler(SKPoint doc)
        {
            if (!_isDrawingRuler || RulerLine is not { } line) return false;
            var end = doc;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) end = SnapRuler(line.Start, doc);
            SetRulerLine(line.Start, end);
            return true;
        }

        /// <summary>Sets the line (for the tool, and for tests).</summary>
        public void SetRulerLine(SKPoint start, SKPoint end)
        {
            RulerLine = (start, end);
            RulerOptions.Show(start.X, start.Y, end.X, end.Y);
            RaiseInvalidate();
        }

        private void ReleaseRuler() => _isDrawingRuler = false;

        /// <summary>The end turned to the nearest 45° step, keeping its distance.</summary>
        private static SKPoint SnapRuler(SKPoint start, SKPoint end)
        {
            var d = end - start;
            var step = MathF.PI / 4;
            var angle = MathF.Round(MathF.Atan2(d.Y, d.X) / step) * step;
            var length = d.Length;
            return new SKPoint(start.X + MathF.Cos(angle) * length, start.Y + MathF.Sin(angle) * length);
        }

        public void ClearRuler()
        {
            RulerLine = null;
            _isDrawingRuler = false;
            RulerOptions.Clear();
            RaiseInvalidate();
        }

        /// <summary>
        /// Turns the active layer around the image's middle so the measured line becomes
        /// level - or upright, if it is closer to vertical. One undo step. Returns the turn
        /// in degrees (clockwise positive), or null if nothing was done.
        /// </summary>
        public double? StraightenLayerToRuler()
        {
            if (RulerLine is not { } line || Layers.ActiveLayer?.Model is not { IsGroup: false, Adjustment: null } layer) return null;
            var d = line.End - line.Start;
            if (d.Length < 1) return null;
            var angle = Math.Atan2(d.Y, d.X) * 180 / Math.PI;          // screen: clockwise from the right
            // The smallest turn that makes the line horizontal or vertical.
            var turn = -(angle - Math.Round(angle / 90) * 90);
            if (Math.Abs(turn) < 0.01) return 0;
            if (layer.IsText || layer.IsShape || layer.IsFill) RasterizeIfText(layer);
            var target = Layers.ActiveLayer!.Model;
            var center = new SKPoint(Layers.DocumentWidth / 2f, Layers.DocumentHeight / 2f);
            using var turned = Core.Transform.LayerTransformer.Bake(target.Bitmap,
                SKMatrix.CreateRotationDegrees((float)turn, center.X, center.Y), Layers.DocumentWidth, Layers.DocumentHeight);
            Layers.ReplaceLayerPixels(target, turned, "Straighten");
            ClearRuler();
            return turn;
        }
    }
}
