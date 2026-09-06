using System;
using JolieCat.Core.Documents;
using SkiaSharp;

namespace JolieCat.Core.History
{
    /// <summary>
    /// Undoes/redoes a Smart Object layer's placement - its own
    /// <see cref="Layer.SmartObjectTransform"/> matrix, before and after one Free
    /// Transform commit - by restoring that matrix and re-rendering the layer's cached
    /// <see cref="Layer.Bitmap"/> fresh from its pristine source (see
    /// <see cref="Layer.RenderSmartObject"/>), rather than restoring a plain pixel
    /// snapshot the way <see cref="LayerPixelsCommand"/> does for an ordinary raster
    /// layer. Needed because undoing a Smart Object transform has to roll back the
    /// transform itself, not just its rendered pixels - otherwise a later "Edit
    /// Contents" re-render would silently redo whatever transform was just undone.
    /// </summary>
    public sealed class SmartObjectTransformCommand : IEditCommand
    {
        private readonly Layer _layer;
        private readonly SKMatrix _before;
        private readonly SKMatrix _after;

        public SmartObjectTransformCommand(Layer layer, SKMatrix before, SKMatrix after)
        {
            _layer = layer ?? throw new ArgumentNullException(nameof(layer));
            _before = before;
            _after = after;
        }

        public void Undo() => Apply(_before);

        public void Redo() => Apply(_after);

        private void Apply(SKMatrix matrix)
        {
            if (_layer.SmartObjectTransform is null) return;

            _layer.SmartObjectTransform.Matrix = matrix;
            _layer.RenderSmartObject();
        }
    }
}
