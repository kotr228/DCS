using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    /// <summary>
    /// Color Sampler tool: up to <see cref="MaxColorSamplers"/> fixed points whose colors
    /// the Info panel keeps showing while you edit. Click to add one, drag one to move
    /// it, Alt+click one to remove it. They belong to the view (not saved in the file).
    /// </summary>
    public partial class CanvasViewModel
    {
        public const int MaxColorSamplers = 10;

        public ObservableCollection<SKPoint> ColorSamplers { get; } = new();

        private int _draggingSampler = -1;

        /// <summary>The sampler within a few screen pixels of <paramref name="doc"/>, or -1.</summary>
        private int SamplerAt(SKPoint doc)
        {
            var reach = 8 / (float)Math.Max(Zoom, 0.01);
            for (var i = ColorSamplers.Count - 1; i >= 0; i--)
            {
                var p = ColorSamplers[i];
                if (Math.Abs(p.X - doc.X) <= reach && Math.Abs(p.Y - doc.Y) <= reach) return i;
            }
            return -1;
        }

        private static SKPoint PixelCenter(SKPoint doc) => new(MathF.Floor(doc.X) + 0.5f, MathF.Floor(doc.Y) + 0.5f);

        private bool InsideDocument(SKPoint doc) =>
            doc.X >= 0 && doc.Y >= 0 && doc.X < Layers.DocumentWidth && doc.Y < Layers.DocumentHeight;

        private void PressColorSampler(SKPoint doc)
        {
            var hit = SamplerAt(doc);
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0)
            {
                if (hit >= 0) ColorSamplers.RemoveAt(hit);
            }
            else if (hit >= 0)
            {
                _draggingSampler = hit;
            }
            else if (!InsideDocument(doc))
            {
                return;
            }
            else if (ColorSamplers.Count >= MaxColorSamplers)
            {
                ToolHintText = $"Up to {MaxColorSamplers} color samplers - Alt+click one to remove it";
                return;
            }
            else
            {
                ColorSamplers.Add(PixelCenter(doc));
                _draggingSampler = ColorSamplers.Count - 1;
            }
            ToolHintText = "Drag a sampler to move it, Alt+click to remove it; the INFO panel shows their colors";
            RaiseInvalidate();
        }

        private bool MoveColorSampler(SKPoint doc)
        {
            if (_draggingSampler < 0 || _draggingSampler >= ColorSamplers.Count) return false;
            var p = new SKPoint(Math.Clamp(doc.X, 0, Layers.DocumentWidth - 1), Math.Clamp(doc.Y, 0, Layers.DocumentHeight - 1));
            ColorSamplers[_draggingSampler] = PixelCenter(p);
            RaiseInvalidate();
            return true;
        }

        private void ReleaseColorSampler() => _draggingSampler = -1;

        /// <summary>Removes every color sampler (Info panel button).</summary>
        public void ClearColorSamplers()
        {
            ColorSamplers.Clear();
            RaiseInvalidate();
        }

        /// <summary>Samplers past the document's edge after a crop or resize are dropped.</summary>
        private void DropSamplersOutsideDocument()
        {
            foreach (var p in ColorSamplers.Where(p => !InsideDocument(p)).ToList()) ColorSamplers.Remove(p);
        }
    }
}
