using System;
using System.Collections.Generic;
using SkiaSharp;

namespace JolieCat.Core.Documents
{
    /// <summary>
    /// An ordered stack of layers that compose a single canvas, plus which one is
    /// currently active (what painting tools draw onto).
    /// </summary>
    /// <remarks>
    /// Deliberately concrete rather than implementing <c>JolieCat.Shared.Documents.IScene</c>:
    /// that interface's <c>Layers</c>/<c>ActiveLayer</c> are typed against the pure
    /// <c>ILayer</c> contract, but every real caller (the compositor, painting tools) needs
    /// the concrete <see cref="Documents.Layer"/> for its <see cref="Layer.Bitmap"/> - and
    /// nothing in this codebase has ever implemented <c>IScene</c> a second way, so chasing
    /// that abstraction here would only add casts for no present benefit. <c>ILayer</c>
    /// itself is still honored - <see cref="Documents.Layer"/> implements it - and a real
    /// second <c>IScene</c>/<c>IDocument</c> implementation (e.g. a serialized-project
    /// loader) is still free to reintroduce that layer of indirection when it exists.
    /// </remarks>
    public sealed class Scene
    {
        private readonly List<Layer> _layers = new();

        public Guid Id { get; } = Guid.NewGuid();

        public string Name { get; set; }

        /// <summary>Layers ordered back-to-front (index 0 renders first, i.e. is furthest back).</summary>
        public IReadOnlyList<Layer> Layers => _layers;

        /// <summary>The layer painting tools draw onto. Null only when the scene has no layers.</summary>
        public Layer? ActiveLayer { get; set; }

        /// <summary>The scene's current selection - constrains where painting, erasing,
        /// and filling can affect <see cref="ActiveLayer"/>. Always present (starts with
        /// no selection, not null) so callers never need a null check before reading it.</summary>
        public Selection Selection { get; } = new();

        public Scene(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Scene name cannot be empty.", nameof(name));

            Name = name;
        }

        public void AddLayer(Layer layer)
        {
            ArgumentNullException.ThrowIfNull(layer);
            _layers.Add(layer);
            ActiveLayer ??= layer;
        }

        /// <summary>Creates a new raster layer sized to the document and adds it as the
        /// new topmost (frontmost) layer.</summary>
        public Layer AddLayer(string name, int width, int height, Shared.Enums.LayerType type = Shared.Enums.LayerType.Raster)
        {
            var layer = new Layer(name, width, height, type);
            AddLayer(layer);
            return layer;
        }

        /// <summary>Removes and disposes <paramref name="layer"/>. If it was the active
        /// layer, a neighboring layer (preferring the one that took its position, else the
        /// new topmost) becomes active instead.</summary>
        public bool RemoveLayer(Layer layer)
        {
            var index = _layers.IndexOf(layer);
            if (index < 0) return false;

            _layers.RemoveAt(index);

            if (ActiveLayer == layer)
                ActiveLayer = _layers.Count == 0 ? null : _layers[Math.Min(index, _layers.Count - 1)];

            layer.Dispose();
            return true;
        }

        /// <summary>Moves a layer one step toward the front (up in a Layers panel that
        /// shows the frontmost layer first).</summary>
        public bool MoveLayerUp(Layer layer)
        {
            var index = _layers.IndexOf(layer);
            if (index < 0 || index >= _layers.Count - 1) return false;

            (_layers[index], _layers[index + 1]) = (_layers[index + 1], _layers[index]);
            return true;
        }

        /// <summary>Moves a layer one step toward the back (down in a Layers panel that
        /// shows the frontmost layer first).</summary>
        public bool MoveLayerDown(Layer layer)
        {
            var index = _layers.IndexOf(layer);
            if (index <= 0) return false;

            (_layers[index], _layers[index - 1]) = (_layers[index - 1], _layers[index]);
            return true;
        }

        /// <summary>
        /// Composites <paramref name="layer"/> onto the layer directly behind it - using
        /// its own opacity and blend mode, exactly as it would render normally - then
        /// removes and disposes it. No-op (returns false) if there's no layer beneath it.
        /// </summary>
        public bool MergeLayerDown(Layer layer)
        {
            var index = _layers.IndexOf(layer);
            if (index <= 0) return false;

            var below = _layers[index - 1];
            var alpha = (byte)Math.Clamp(layer.Opacity * 255.0, 0, 255);

            using (var paint = new SKPaint { Color = SKColors.White.WithAlpha(alpha), BlendMode = Layer.ToSkiaBlendMode(layer.BlendMode) })
            {
                below.Canvas.DrawBitmap(layer.Bitmap, 0, 0, paint);
            }

            return RemoveLayer(layer);
        }
    }
}
