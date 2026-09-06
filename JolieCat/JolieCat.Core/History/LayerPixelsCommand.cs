using System;
using SkiaSharp;

namespace JolieCat.Core.History
{
    /// <summary>
    /// Undoes/redoes one bitmap's full pixel content - the history entry for a paint
    /// stroke, an eraser stroke, a Paint Bucket fill, a Gradient fill, or a Text commit,
    /// against either a layer's own color content or its mask (see
    /// <see cref="Documents.Layer.PaintBitmap"/>, which every paint tool already targets -
    /// this simply needs to be handed that same bitmap rather than the <c>Layer</c>
    /// itself, so it replays correctly regardless of which one was actually painted on).
    /// Holds two full <see cref="SKColor"/> snapshots (before/after) rather than a pixel
    /// diff - simple and correct, at the cost of memory proportional to the document size
    /// per entry (bounded by <see cref="HistoryManager"/>'s stack depth cap). A smarter
    /// dirty-rectangle-only diff is a reasonable follow-up once this is in daily use.
    /// </summary>
    public sealed class LayerPixelsCommand : IEditCommand
    {
        private readonly SKBitmap _target;
        private readonly SKColor[] _before;
        private readonly SKColor[] _after;

        public LayerPixelsCommand(SKBitmap target, SKColor[] before, SKColor[] after)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _before = before ?? throw new ArgumentNullException(nameof(before));
            _after = after ?? throw new ArgumentNullException(nameof(after));
        }

        public void Undo() => _target.Pixels = _before;

        public void Redo() => _target.Pixels = _after;
    }
}
