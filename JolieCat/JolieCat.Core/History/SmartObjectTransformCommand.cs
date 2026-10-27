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
        private readonly Scene? _scene;
        private readonly SKMatrix _before;
        private readonly SKMatrix _after;

        public SmartObjectTransformCommand(Layer layer, SKMatrix before, SKMatrix after, Scene? scene = null)
        {
            _layer = layer ?? throw new ArgumentNullException(nameof(layer));
            _scene = scene;
            _before = before;
            _after = after;
        }

        public void Undo() => Apply(_before);

        public void Redo() => Apply(_after);

        private void Apply(SKMatrix matrix)
        {
            // Structural undo/redo may have rebuilt the layer (same Id, new object).
            var layer = _layer;
            if (_scene is not null)
            {
                foreach (var candidate in _scene.Layers)
                    if (candidate.Id == _layer.Id) { layer = candidate; break; }
            }

            if (layer.SmartObjectTransform is null) return;

            layer.SmartObjectTransform.Matrix = matrix;
            layer.RenderSmartObject();
        }
    }
}
