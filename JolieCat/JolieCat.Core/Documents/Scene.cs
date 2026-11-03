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
    public sealed partial class Scene : IDisposable
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

        /// <summary>Select > Save Selection: named selections, as SVG path data (saved
        /// with the project).</summary>
        public Dictionary<string, string> SavedSelections { get; } = new();

        /// <summary>The document's guides (saved with the project).</summary>
        public List<Guide> Guides { get; } = new();

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

        /// <summary>Inserts <paramref name="layer"/> at <paramref name="index"/> (0 = backmost),
        /// clamped to the stack.</summary>
        public void InsertLayer(int index, Layer layer)
        {
            ArgumentNullException.ThrowIfNull(layer);
            _layers.Insert(Math.Clamp(index, 0, _layers.Count), layer);
            ActiveLayer ??= layer;
        }

        /// <summary>Removes and disposes <paramref name="layer"/>. If it was the active
        /// layer, a neighboring layer (preferring the one that took its position, else the
        /// new topmost) becomes active instead.</summary>
        public bool RemoveLayer(Layer layer)
        {
            var index = _layers.IndexOf(layer);
            if (index < 0) return false;

            // A group goes together with everything inside it.
            var (start, end) = BlockOf(layer);
            var removed = CutBlock(start, end);

            if (removed.Contains(ActiveLayer!))
                ActiveLayer = _layers.Count == 0 ? null : _layers[Math.Min(start, _layers.Count - 1)];

            foreach (var gone in removed) gone.Dispose();
            return true;
        }

        /// <summary>Copies <paramref name="layer"/> (pixels, mask and settings) into a new
        /// layer directly above it and makes the copy active. Smart Objects are copied
        /// as their current rendered pixels. Returns null if the layer isn't in this scene.</summary>
        public Layer? DuplicateLayer(Layer layer)
        {
            var index = _layers.IndexOf(layer);
            if (index < 0) return null;
            if (layer.IsGroup) return DuplicateGroup(layer);

            var copy = new Layer($"{layer.Name} copy", layer.Bitmap.Width, layer.Bitmap.Height,
                layer.IsAdjustment ? Shared.Enums.LayerType.Adjustment : Shared.Enums.LayerType.Raster)
            {
                IsVisible = layer.IsVisible,
                Opacity = layer.Opacity,
                BlendMode = layer.BlendMode,
                Adjustment = layer.Adjustment,
                Style = layer.Style,
                ParentId = layer.ParentId,
                IsClipped = layer.IsClipped,
                Text = layer.Text,
                Shape = layer.Shape,
                Fill = layer.Fill,
            };
            copy.Canvas.DrawBitmap(layer.Bitmap, 0, 0);
            if (layer.SmartFilterSource is { } smartSource)
                copy.SetSmartFilters(smartSource.Copy(), layer.SmartFilters);

            if (layer.Mask is { } mask)
            {
                var copyMask = copy.AddMask();
                copyMask.IsEnabled = mask.IsEnabled;
                copyMask.Canvas.Clear(SKColors.Transparent);
                copyMask.Canvas.DrawBitmap(mask.Bitmap, 0, 0);
                copy.IsMaskActive = layer.IsMaskActive;
            }

            _layers.Insert(index + 1, copy);
            ActiveLayer = copy;
            return copy;
        }

        /// <summary>Moves a layer one step toward the front (up in a Layers panel that
        /// shows the frontmost layer first).</summary>
        public bool MoveLayerUp(Layer layer) => _layers.Contains(layer) && MoveUnitUp(layer);

        /// <summary>Moves a layer one step toward the back (down in a Layers panel that
        /// shows the frontmost layer first).</summary>
        public bool MoveLayerDown(Layer layer) => _layers.Contains(layer) && MoveUnitDown(layer);

        /// <summary>
        /// Composites <paramref name="layer"/> onto the layer directly behind it - using
        /// its own opacity and blend mode, exactly as it would render normally - then
        /// removes and disposes it. No-op (returns false) if there's no layer beneath it.
        /// </summary>
        public bool MergeLayerDown(Layer layer)
        {
            var index = _layers.IndexOf(layer);
            if (layer.IsGroup) return MergeGroup(layer) is not null; // Merge Group
            if (index <= 0) return false;

            var below = _layers[index - 1];
            if (below.IsAdjustment) return false; // nothing to merge pixels into
            // Only into a plain layer in the same group (not a group, nor out of one).
            if (below.IsGroup || below.ParentId != layer.ParentId) return false;

            // Delegates to SceneCompositor rather than drawing layer.Bitmap directly, so
            // a masked layer merges down showing exactly what it already looked like
            // composited on screen - not its full, unmasked content suddenly reappearing
            // the moment it merges into the layer below.
            if (layer.IsClipped && !below.IsClipped)
            {
                // Clipped to `below`: only what lies on its pixels survives.
                using var baseShape = below.Bitmap.Copy();
                var bounds = new SKRect(0, 0, below.Bitmap.Width, below.Bitmap.Height);
                below.Canvas.SaveLayer(bounds, null);
                SceneCompositor.DrawLayer(below.Canvas, layer, layer.Bitmap, layer.Bitmap.Width, layer.Bitmap.Height,
                    default, () => SKImage.FromBitmap(below.Bitmap));
                using (var cut = new SKPaint { BlendMode = SKBlendMode.DstIn })
                    below.Canvas.DrawBitmap(baseShape, 0, 0, cut);
                below.Canvas.Restore();
            }
            else
            {
                SceneCompositor.DrawLayer(below.Canvas, layer, layer.Bitmap, layer.Bitmap.Width, layer.Bitmap.Height,
                    default, () => SKImage.FromBitmap(below.Bitmap));
            }

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
        public void ResizeLayers(int newWidth, int newHeight) => ResizeLayers(newWidth, newHeight, 0, 0);

        /// <summary>Canvas Size with the old content placed at (<paramref name="offsetX"/>,
        /// <paramref name="offsetY"/>) on the new canvas - e.g. half the size difference
        /// to keep it centered.</summary>
        public void ResizeLayers(int newWidth, int newHeight, int offsetX, int offsetY)
        {
            if (newWidth <= 0) throw new ArgumentOutOfRangeException(nameof(newWidth));
            if (newHeight <= 0) throw new ArgumentOutOfRangeException(nameof(newHeight));
            ShiftGuides(offsetX, offsetY, newWidth, newHeight);

            for (var i = 0; i < _layers.Count; i++)
            {
                var old = _layers[i];
                if (old.Bitmap.Width == newWidth && old.Bitmap.Height == newHeight && offsetX == 0 && offsetY == 0) continue;

                var resized = new Layer(old.Name, newWidth, newHeight, old.Type, old.Id)
                {
                    IsVisible = old.IsVisible,
                    IsLocked = old.IsLocked,
                    Opacity = old.Opacity,
                    BlendMode = old.BlendMode,
                    Adjustment = old.Adjustment,
                    Style = old.Style,
                    ParentId = old.ParentId,
                    IsExpanded = old.IsExpanded,
                    IsClipped = old.IsClipped,
                };
                resized.Canvas.DrawBitmap(old.Bitmap, offsetX, offsetY);
                resized.Text = old.Text?.MovedBy(offsetX, offsetY);
                resized.Shape = old.Shape?.MovedBy(offsetX, offsetY);

                // A mask is resized the same way as the layer's own content - anchored
                // at its top-left corner, cropped or padded-with-white (fully visible,
                // matching a freshly added mask's own default) rather than left behind.
                if (old.Mask is { } oldMask)
                {
                    var resizedMask = resized.AddMask();
                    resizedMask.IsEnabled = oldMask.IsEnabled;
                    resizedMask.Canvas.Clear(SKColors.White);
                    resizedMask.Canvas.DrawBitmap(oldMask.Bitmap, offsetX, offsetY);
                    resized.IsMaskActive = old.IsMaskActive;
                }

                CarrySmartFilters(old, resized, (canvas, source) => canvas.DrawBitmap(source, offsetX, offsetY));

                KeepFill(old, resized);

                _layers[i] = resized;
                if (ActiveLayer == old) ActiveLayer = resized;

                old.Dispose();
            }
        }

        /// <summary>
        /// Crops every layer to <paramref name="cropRect"/> (document-space, axis-
        /// aligned) - first rotating each layer's entire content around the document's
        /// own center by -<paramref name="rotationDegrees"/> ("straighten") if
        /// non-zero. Every resulting layer (and mask) is exactly <paramref name="cropRect"/>'s
        /// own size, anchored so content that was at <paramref name="cropRect"/>'s
        /// top-left now sits at (0,0) - the same top-left-anchored convention every
        /// other layer placement in this codebase already uses. Rebuilds a fresh
        /// <see cref="Layer"/> per layer (disposing the old one), exactly like
        /// <see cref="ResizeLayers"/>.
        /// </summary>
        public void CropLayers(SKRectI cropRect, float rotationDegrees)
        {
            if (cropRect.Width <= 0 || cropRect.Height <= 0) throw new ArgumentOutOfRangeException(nameof(cropRect));
            ShiftGuides(-cropRect.Left, -cropRect.Top, cropRect.Width, cropRect.Height);

            for (var i = 0; i < _layers.Count; i++)
            {
                var old = _layers[i];
                var center = new SKPoint(old.Bitmap.Width / 2f, old.Bitmap.Height / 2f);

                var cropped = new Layer(old.Name, cropRect.Width, cropRect.Height, old.Type, old.Id)
                {
                    IsVisible = old.IsVisible,
                    IsLocked = old.IsLocked,
                    Opacity = old.Opacity,
                    BlendMode = old.BlendMode,
                    Adjustment = old.Adjustment,
                    Style = old.Style,
                    ParentId = old.ParentId,
                    IsExpanded = old.IsExpanded,
                    IsClipped = old.IsClipped,
                };

                RotateAndCropInto(cropped.Canvas, old.Bitmap, center, rotationDegrees, cropRect, SKColors.Transparent);

                if (old.Mask is { } oldMask)
                {
                    var croppedMask = cropped.AddMask();
                    croppedMask.IsEnabled = oldMask.IsEnabled;
                    RotateAndCropInto(croppedMask.Canvas, oldMask.Bitmap, center, rotationDegrees, cropRect, SKColors.White);
                    cropped.IsMaskActive = old.IsMaskActive;
                }

                CarrySmartFilters(old, cropped, (canvas, source) =>
                    RotateAndCropInto(canvas, source, center, rotationDegrees, cropRect, SKColors.Transparent));

                KeepFill(old, cropped);

                _layers[i] = cropped;
                if (ActiveLayer == old) ActiveLayer = cropped;

                old.Dispose();
            }
        }

        /// <summary>Rotates <paramref name="source"/> around <paramref name="center"/>
        /// by -<paramref name="rotationDegrees"/> onto a same-size intermediate
        /// surface, then trims that to <paramref name="cropRect"/> and draws the
        /// result onto <paramref name="destCanvas"/> at (0,0). Deliberately two
        /// passes rather than one combined transform: rotating and cropping via a
        /// single concatenated matrix is easy to get subtly backwards (which half of
        /// the composition applies "first" to a point); two separate, individually
        /// obvious passes are not, and this is a rare, user-initiated, one-time
        /// operation where the extra intermediate bitmap's cost is irrelevant.</summary>
        private static void RotateAndCropInto(SKCanvas destCanvas, SKBitmap source, SKPoint center, float rotationDegrees, SKRectI cropRect, SKColor backgroundColor)
        {
            using var rotated = new SKBitmap(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var rotateCanvas = new SKCanvas(rotated))
            {
                rotateCanvas.Clear(backgroundColor);
                if (rotationDegrees != 0f)
                {
                    rotateCanvas.Translate(center.X, center.Y);
                    rotateCanvas.RotateDegrees(-rotationDegrees);
                    rotateCanvas.Translate(-center.X, -center.Y);
                }
                rotateCanvas.DrawBitmap(source, 0, 0);
            }

            destCanvas.Clear(backgroundColor);
            destCanvas.DrawBitmap(rotated, -cropRect.Left, -cropRect.Top);
        }

        /// <summary>Image Size: rescales every layer (and mask) to the new dimensions.
        /// Downscaling uses mipmapped linear filtering (no aliasing/moire); upscaling uses
        /// bicubic (Mitchell) so edges stay smooth without ringing.</summary>
        public void ResampleLayers(int newWidth, int newHeight)
        {
            if (newWidth <= 0) throw new ArgumentOutOfRangeException(nameof(newWidth));
            if (newHeight <= 0) throw new ArgumentOutOfRangeException(nameof(newHeight));
            if (_layers.Count == 0) return;

            var old = _layers[0].Bitmap;
            ScaleGuides((float)newWidth / old.Width, (float)newHeight / old.Height, newWidth, newHeight);
            var downscale = newWidth < old.Width || newHeight < old.Height;
            var sampling = downscale
                ? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
                : new SKSamplingOptions(SKCubicResampler.Mitchell);
            var dest = SKRect.Create(0, 0, newWidth, newHeight);
            float sx = (float)newWidth / old.Width, sy = (float)newHeight / old.Height;

            RebuildLayers(newWidth, newHeight, (canvas, source) =>
            {
                using var image = SKImage.FromBitmap(source);
                canvas.DrawImage(image, dest, sampling);
            }, new VectorMap(p => new SKPoint(p.X * sx, p.Y * sy), (sx + sy) / 2, TurnsOrMirrors: false));
        }

        /// <summary>Rotates the whole image by <paramref name="quarterTurns"/> × 90°
        /// clockwise (negative = counter-clockwise). Odd turns swap width and height.</summary>
        public void RotateLayers(int quarterTurns)
        {
            if (_layers.Count == 0) return;

            var turns = ((quarterTurns % 4) + 4) % 4;
            if (turns == 0) return;

            var oldWidth = _layers[0].Bitmap.Width;
            var oldHeight = _layers[0].Bitmap.Height;
            var newWidth = turns % 2 == 1 ? oldHeight : oldWidth;
            var newHeight = turns % 2 == 1 ? oldWidth : oldHeight;
            RotateGuides(turns, oldWidth, oldHeight);

            RebuildLayers(newWidth, newHeight, (canvas, source) =>
            {
                canvas.Save();
                switch (turns)
                {
                    case 1: canvas.Translate(newWidth, 0); break;
                    case 2: canvas.Translate(newWidth, newHeight); break;
                    case 3: canvas.Translate(0, newHeight); break;
                }

                canvas.RotateDegrees(90 * turns);
                canvas.DrawBitmap(source, 0, 0);
                canvas.Restore();
            }, new VectorMap(p => turns switch
            {
                1 => new SKPoint(oldHeight - p.Y, p.X),
                2 => new SKPoint(oldWidth - p.X, oldHeight - p.Y),
                _ => new SKPoint(p.Y, oldWidth - p.X),
            }, 1, TurnsOrMirrors: true));
        }

        /// <summary>Mirrors the whole image (every layer and mask).</summary>
        public void FlipLayers(bool horizontal)
        {
            if (_layers.Count == 0) return;

            var width = _layers[0].Bitmap.Width;
            var height = _layers[0].Bitmap.Height;
            FlipGuides(horizontal, width, height);
            RebuildLayers(width, height, (canvas, source) => DrawFlipped(canvas, source, horizontal),
                new VectorMap(p => horizontal ? new SKPoint(width - p.X, p.Y) : new SKPoint(p.X, height - p.Y), 1, TurnsOrMirrors: true));
        }

        /// <summary>Mirrors one layer's pixels in place (Edit &gt; Transform &gt; Flip).
        /// Only the layer content - not its mask - like Photoshop when the mask is unlinked.</summary>
        public static void FlipLayerContent(Layer layer, bool horizontal)
        {
            ArgumentNullException.ThrowIfNull(layer);

            using var copy = layer.Bitmap.Copy();
            layer.Canvas.Clear(SKColors.Transparent);
            DrawFlipped(layer.Canvas, copy, horizontal);
        }

        private static void DrawFlipped(SKCanvas canvas, SKBitmap source, bool horizontal)
        {
            canvas.Save();
            if (horizontal)
            {
                canvas.Translate(source.Width, 0);
                canvas.Scale(-1, 1);
            }
            else
            {
                canvas.Translate(0, source.Height);
                canvas.Scale(1, -1);
            }

            canvas.DrawBitmap(source, 0, 0);
            canvas.Restore();
        }

        /// <summary>Flatten Image: merges every visible layer into a single layer named
        /// "Background" (hidden layers are discarded, as in Photoshop). Transparency is kept.</summary>
        public void Flatten()
        {
            if (_layers.Count == 0) return;

            var width = _layers[0].Bitmap.Width;
            var height = _layers[0].Bitmap.Height;
            var flat = new Layer("Background", width, height);
            SceneCompositor.DrawLayersInto(flat.Bitmap, this);

            foreach (var layer in _layers) layer.Dispose();
            _layers.Clear();
            _layers.Add(flat);
            ActiveLayer = flat;
        }

        /// <summary>Merge Visible: merges every visible layer into the lowest visible
        /// one's position, leaving hidden layers untouched. Returns false if fewer than
        /// two layers are visible.</summary>
        public bool MergeVisible()
        {
            // Everything that shows (groups included); hidden layers and hidden groups stay.
            var visible = _layers.Where(IsEffectivelyVisible).ToList();
            if (visible.Count(l => !l.IsGroup) < 2) return false;
            var bottomPixel = visible.First(l => !l.IsGroup);
            visible.Remove(bottomPixel);
            visible.Insert(0, bottomPixel);

            var width = _layers[0].Bitmap.Width;
            var height = _layers[0].Bitmap.Height;
            var bottom = visible[0];
            var merged = new Layer(bottom.Name, width, height, Shared.Enums.LayerType.Raster, bottom.Id) { ParentId = bottom.ParentId };
            SceneCompositor.DrawLayersInto(merged.Bitmap, this);

            var index = _layers.IndexOf(bottom);
            _layers[index] = merged;
            foreach (var layer in visible)
            {
                if (!ReferenceEquals(layer, bottom)) _layers.Remove(layer);
                layer.Dispose();
            }

            NormalizeGroups();
            ActiveLayer = merged;
            return true;
        }

        /// <summary>Replaces every layer with a fresh one of the new size whose content
        /// (and mask, if any) is produced by <paramref name="draw"/> from the old bitmap.
        /// Ids and settings carry over, so history entries keep pointing at the right layer.</summary>
        /// <summary>How a canvas change moves points (and scales sizes), so text and shape
        /// layers can stay editable. <paramref name="TurnsOrMirrors"/>: text can't be
        /// turned or mirrored (and polygons/stars wouldn't keep their look), so those
        /// become pixels.</summary>
        private sealed record VectorMap(Func<SKPoint, SKPoint> Point, float Scale, bool TurnsOrMirrors);

        /// <summary>Keeps <paramref name="old"/>'s text / shape on <paramref name="rebuilt"/>
        /// where <paramref name="map"/> allows, redrawn crisply.</summary>
        private static void KeepVectors(Layer old, Layer rebuilt, VectorMap? map)
        {
            if (map is null) return;
            if (old.Shape is { } shape && !(map.TurnsOrMirrors && shape.Kind is VectorShapeKind.Polygon or VectorShapeKind.Star))
            {
                rebuilt.Shape = shape with
                {
                    Start = map.Point(shape.Start),
                    End = map.Point(shape.End),
                    StrokeWidth = shape.StrokeWidth * map.Scale,
                    CornerRadius = shape.CornerRadius * map.Scale,
                };
                rebuilt.RenderShape();
            }
            if (old.Text is { } text && !map.TurnsOrMirrors)
            {
                rebuilt.Text = text with { Origin = map.Point(text.Origin), FontSize = text.FontSize * map.Scale };
                rebuilt.RenderText();
            }
        }

        private void RebuildLayers(int newWidth, int newHeight, Action<SKCanvas, SKBitmap> draw, VectorMap? vectors = null)
        {
            for (var i = 0; i < _layers.Count; i++)
            {
                var old = _layers[i];
                var rebuilt = new Layer(old.Name, newWidth, newHeight, old.Type == Shared.Enums.LayerType.SmartObject ? Shared.Enums.LayerType.Raster : old.Type, old.Id)
                {
                    IsVisible = old.IsVisible,
                    IsLocked = old.IsLocked,
                    Opacity = old.Opacity,
                    BlendMode = old.BlendMode,
                    Adjustment = old.Adjustment,
                    Style = old.Style,
                    ParentId = old.ParentId,
                    IsExpanded = old.IsExpanded,
                    IsClipped = old.IsClipped,
                };
                draw(rebuilt.Canvas, old.Bitmap);

                if (old.Mask is { } oldMask)
                {
                    var mask = rebuilt.AddMask();
                    mask.IsEnabled = oldMask.IsEnabled;
                    mask.Canvas.Clear(SKColors.White);
                    draw(mask.Canvas, oldMask.Bitmap);
                    rebuilt.IsMaskActive = old.IsMaskActive;
                }

                CarrySmartFilters(old, rebuilt, draw);

                KeepFill(old, rebuilt);
                KeepVectors(old, rebuilt, vectors);

                _layers[i] = rebuilt;
                if (ActiveLayer == old) ActiveLayer = rebuilt;
                old.Dispose();
            }
        }

        /// <summary>A fill layer stays a fill when the canvas changes: redrawn at the new size.</summary>
        private static void KeepFill(Layer old, Layer rebuilt)
        {
            if (old.Fill is null) return;
            rebuilt.Fill = old.Fill;
            rebuilt.RenderFill();
        }

        /// <summary>When a canvas change rebuilds a Smart Filter layer, its untouched
        /// original goes through the same change and the filters run again on it.</summary>
        private static void CarrySmartFilters(Layer old, Layer rebuilt, Action<SKCanvas, SKBitmap> draw)
        {
            if (old.SmartFilterSource is not { } oldSource) return;
            var source = new SKBitmap(rebuilt.Bitmap.Width, rebuilt.Bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            source.Erase(SKColors.Transparent);
            using (var canvas = new SKCanvas(source))
                draw(canvas, oldSource);
            rebuilt.SetSmartFilters(source, old.SmartFilters);
        }

        /// <summary>Freezes every layer's metadata and pixel content - the History
        /// system's before/after state for a structural change (see
        /// <see cref="History.SceneStructuralCommand"/>).</summary>
        public IReadOnlyList<LayerSnapshot> CaptureLayers() => _layers
            .Select(layer => new LayerSnapshot(
                layer.Name, layer.Bitmap.Width, layer.Bitmap.Height, layer.Type,
                layer.IsVisible, layer.IsLocked, layer.Opacity, layer.BlendMode, layer.Bitmap.Pixels,
                layer.Mask is not null, layer.Mask?.IsEnabled ?? false, layer.Mask?.Bitmap.Pixels,
                layer.Id, layer.SmartObject, layer.SmartObjectTransform?.Matrix, layer.Adjustment, layer.Style, layer.ParentId, layer.IsExpanded, layer.IsClipped, layer.Text,
                layer.SmartFilterSource?.Pixels, layer.HasSmartFilters ? layer.SmartFilters : null, layer.Shape, layer.Fill))
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
                var layer = new Layer(entry.Name, entry.Width, entry.Height, entry.Type, entry.Id == Guid.Empty ? null : entry.Id)
                {
                    IsVisible = entry.IsVisible,
                    IsLocked = entry.IsLocked,
                    Opacity = entry.Opacity,
                    BlendMode = entry.BlendMode,
                    Style = entry.Style,
                    ParentId = entry.ParentId,
                    IsExpanded = entry.IsExpanded,
                    IsClipped = entry.IsClipped,
                    Text = entry.Text,
                    Shape = entry.Shape,
                    Fill = entry.Fill,
                };
                layer.Bitmap.Pixels = entry.Pixels;

                if (entry.SmartFilterSource is { } sourcePixels)
                {
                    var source = new SKBitmap(entry.Width, entry.Height, SKColorType.Rgba8888, SKAlphaType.Premul) { Pixels = sourcePixels };
                    layer.SetSmartFilters(source, entry.SmartFilters ?? Array.Empty<Effects.SmartFilter>());
                }

                if (entry.SmartObject is { } smartObject && entry.SmartObjectMatrix is { } matrix)
                    layer.AttachSmartObject(smartObject, matrix);

                if (entry.HasMask)
                {
                    var mask = layer.AddMask();
                    mask.IsEnabled = entry.IsMaskEnabled;
                    if (entry.MaskPixels is { } maskPixels)
                        mask.Bitmap.Pixels = maskPixels;
                }

                if (entry.Adjustment is { } adjustment)
                {
                    layer.Adjustment = adjustment;
                    layer.IsMaskActive = layer.Mask is not null; // an adjustment layer is painted through its mask
                }

                _layers.Add(layer);
            }

            ActiveLayer = activeIndex >= 0 && activeIndex < _layers.Count
                ? _layers[activeIndex]
                : _layers.Count > 0 ? _layers[^1] : null;
        }

        /// <summary>Disposes every layer (and its mask, if any) - each one's
        /// <see cref="SKBitmap"/>s are unmanaged native memory that needs an explicit
        /// release rather than waiting on the GC. Needed now that a document (and so
        /// its Scene) can actually be closed mid-session - see
        /// <c>JolieCat.UI.ViewModels.DocumentViewModel</c> - rather than only ever
        /// going away at process exit.</summary>
        public void Dispose()
        {
            foreach (var layer in _layers)
                layer.Dispose();
            _layers.Clear();
        }
    }
}
