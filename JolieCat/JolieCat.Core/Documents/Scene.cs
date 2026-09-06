using System;
using System.Collections.Generic;
using System.Linq;
using JolieCat.Core.History;
using JolieCat.Core.Rendering;
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

            // Delegates to SceneCompositor rather than drawing layer.Bitmap directly, so
            // a masked layer merges down showing exactly what it already looked like
            // composited on screen - not its full, unmasked content suddenly reappearing
            // the moment it merges into the layer below.
            SceneCompositor.DrawLayer(below.Canvas, layer, layer.Bitmap.Width, layer.Bitmap.Height);

            return RemoveLayer(layer);
        }

        /// <summary>
        /// Resizes every layer's bitmap to (<paramref name="newWidth"/>, <paramref name="newHeight"/>),
        /// preserving each layer's existing content anchored at its top-left corner -
        /// cropped if shrinking, padded with transparency if growing. Used when the
        /// document's own canvas size changes (e.g. importing an image and resizing the
        /// canvas to match it - see <c>JolieCat.UI.ViewModels.Layers.LayersViewModel.ResizeDocument</c>).
        /// Rebuilds a fresh <see cref="Layer"/> per resized layer (disposing the old one)
        /// rather than resizing a bitmap in place - <see cref="SKBitmap"/> has no resize-
        /// in-place operation, and this keeps every layer's identity-churn-on-structural-
        /// change behavior consistent with <see cref="RestoreLayers"/>.
        /// </summary>
        public void ResizeLayers(int newWidth, int newHeight)
        {
            if (newWidth <= 0) throw new ArgumentOutOfRangeException(nameof(newWidth));
            if (newHeight <= 0) throw new ArgumentOutOfRangeException(nameof(newHeight));

            for (var i = 0; i < _layers.Count; i++)
            {
                var old = _layers[i];
                if (old.Bitmap.Width == newWidth && old.Bitmap.Height == newHeight) continue;

                var resized = new Layer(old.Name, newWidth, newHeight, old.Type)
                {
                    IsVisible = old.IsVisible,
                    IsLocked = old.IsLocked,
                    Opacity = old.Opacity,
                    BlendMode = old.BlendMode,
                };
                resized.Canvas.DrawBitmap(old.Bitmap, 0, 0);

                // A mask is resized the same way as the layer's own content - anchored
                // at its top-left corner, cropped or padded-with-white (fully visible,
                // matching a freshly added mask's own default) rather than left behind.
                if (old.Mask is { } oldMask)
                {
                    var resizedMask = resized.AddMask();
                    resizedMask.IsEnabled = oldMask.IsEnabled;
                    resizedMask.Canvas.Clear(SKColors.White);
                    resizedMask.Canvas.DrawBitmap(oldMask.Bitmap, 0, 0);
                    resized.IsMaskActive = old.IsMaskActive;
                }

                _layers[i] = resized;
                if (ActiveLayer == old) ActiveLayer = resized;

                old.Dispose();
            }
        }

        /// <summary>Freezes every layer's metadata and pixel content - the History
        /// system's before/after state for a structural change (see
        /// <see cref="History.SceneStructuralCommand"/>).</summary>
        public IReadOnlyList<LayerSnapshot> CaptureLayers() => _layers
            .Select(layer => new LayerSnapshot(
                layer.Name, layer.Bitmap.Width, layer.Bitmap.Height, layer.Type,
                layer.IsVisible, layer.IsLocked, layer.Opacity, layer.BlendMode, layer.Bitmap.Pixels,
                layer.Mask is not null, layer.Mask?.IsEnabled ?? false, layer.Mask?.Bitmap.Pixels))
            .ToList();

        /// <summary>
        /// Replaces every layer with fresh ones reconstructed from <paramref name="snapshot"/> -
        /// the History system's structural undo/redo (see <see cref="History.SceneStructuralCommand"/>).
        /// Always disposes the current layers and builds brand new <see cref="Layer"/>
        /// instances from the snapshot rather than trying to resurrect exactly the ones
        /// that existed before - a deleted layer's bitmap/canvas are already gone by the
        /// time an undo could run, so there's nothing to resurrect; reconstructing an
        /// equivalent layer from its frozen pixels is the only option, and is
        /// indistinguishable to the user (its Id is new, but nothing outside a single
        /// Undo/Redo call keeps a Layer's Id across a structural change anyway).
        /// </summary>
        /// <param name="activeIndex">Index into <paramref name="snapshot"/> (and so, after
        /// reconstruction, into <see cref="Layers"/>) that should become active; out-of-range
        /// falls back to the new topmost layer.</param>
        public void RestoreLayers(IReadOnlyList<LayerSnapshot> snapshot, int activeIndex)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            foreach (var layer in _layers)
                layer.Dispose();
            _layers.Clear();

            foreach (var entry in snapshot)
            {
                var layer = new Layer(entry.Name, entry.Width, entry.Height, entry.Type)
                {
                    IsVisible = entry.IsVisible,
                    IsLocked = entry.IsLocked,
                    Opacity = entry.Opacity,
                    BlendMode = entry.BlendMode,
                };
                layer.Bitmap.Pixels = entry.Pixels;

                if (entry.HasMask)
                {
                    var mask = layer.AddMask();
                    mask.IsEnabled = entry.IsMaskEnabled;
                    if (entry.MaskPixels is { } maskPixels)
                        mask.Bitmap.Pixels = maskPixels;
                }

                _layers.Add(layer);
            }

            ActiveLayer = activeIndex >= 0 && activeIndex < _layers.Count
                ? _layers[activeIndex]
                : _layers.Count > 0 ? _layers[^1] : null;
        }
    }
}
