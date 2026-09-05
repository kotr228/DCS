using System;
using JolieCat.Core.Documents;
using SkiaSharp;

namespace JolieCat.Core.History
{
    /// <summary>
    /// Undoes/redoes a single layer's full pixel content - the history entry for a paint
    /// stroke, an eraser stroke, a Paint Bucket fill, a Gradient fill, or a Text commit.
    /// Holds two full <see cref="SKColor"/> snapshots (before/after) rather than a pixel
    /// diff - simple and correct, at the cost of memory proportional to the document size
    /// per entry (bounded by <see cref="HistoryManager"/>'s stack depth cap). A smarter
    /// dirty-rectangle-only diff is a reasonable follow-up once this is in daily use.
    /// </summary>
    public sealed class LayerPixelsCommand : IEditCommand
    {
        private readonly Layer _layer;
        private readonly SKColor[] _before;
        private readonly SKColor[] _after;

        public LayerPixelsCommand(Layer layer, SKColor[] before, SKColor[] after)
        {
            _layer = layer ?? throw new ArgumentNullException(nameof(layer));
            _before = before ?? throw new ArgumentNullException(nameof(before));
            _after = after ?? throw new ArgumentNullException(nameof(after));
        }

        public void Undo() => _layer.Bitmap.Pixels = _before;

        public void Redo() => _layer.Bitmap.Pixels = _after;
    }
}
