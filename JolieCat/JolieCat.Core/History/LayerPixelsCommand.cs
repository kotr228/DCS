using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace JolieCat.Core.History
{
    /// <summary>
    /// Undoes/redoes one bitmap's pixel change - the history entry for a paint stroke,
    /// an eraser stroke, a fill, a gradient, a text commit, a transform/warp bake, or a
    /// filter. Targets whatever bitmap was painted on (a layer's color content or its
    /// mask, see <see cref="Documents.Layer.PaintBitmap"/>).
    /// <para>
    /// Callers still hand in full before/after snapshots, but only the 64×64 tiles that
    /// actually differ are kept. A small brush stroke on a 4000×4000 layer now costs a
    /// few hundred KB instead of ~128 MB, so history can be much deeper.
    /// </para>
    /// </summary>
    public sealed class LayerPixelsCommand : IEditCommand, ISizedEditCommand
    {
        public const int TileSize = 64;

        private readonly SKBitmap _target;
        private readonly List<Tile> _tiles = new();

        // When set, the target bitmap is looked up by layer Id at undo/redo time, because
        // structural undo/redo rebuilds Layer objects (same Id, new bitmaps).
        private readonly Documents.Scene? _scene;
        private readonly Guid _layerId;
        private readonly bool _isMask;
        private readonly int _width;
        private readonly int _height;

        private sealed record Tile(int X, int Y, int Width, int Height, SKColor[] Before, SKColor[] After);

        /// <summary>Records a pixel edit on <paramref name="layer"/>'s content (or its
        /// mask when <paramref name="isMask"/>), resolved by layer Id at undo time so the
        /// entry survives structural undo/redo of the layer stack.</summary>
        public LayerPixelsCommand(Documents.Scene scene, Documents.Layer layer, bool isMask, SKColor[] before, SKColor[] after)
            : this(isMask && layer.Mask is not null ? layer.Mask.Bitmap : layer.Bitmap, before, after)
        {
            _scene = scene ?? throw new ArgumentNullException(nameof(scene));
            _layerId = layer.Id;
            _isMask = isMask && layer.Mask is not null;
        }

        public LayerPixelsCommand(SKBitmap target, SKColor[] before, SKColor[] after)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _width = target.Width;
            _height = target.Height;
            ArgumentNullException.ThrowIfNull(before);
            ArgumentNullException.ThrowIfNull(after);

            var width = target.Width;
            var height = target.Height;
            if (before.Length != width * height || after.Length != width * height)
                throw new ArgumentException("Snapshots must match the target bitmap's size.");

            var beforeBits = MemoryMarshal.Cast<SKColor, uint>(before);
            var afterBits = MemoryMarshal.Cast<SKColor, uint>(after);

            for (var ty = 0; ty < height; ty += TileSize)
            {
                var th = Math.Min(TileSize, height - ty);
                for (var tx = 0; tx < width; tx += TileSize)
                {
                    var tw = Math.Min(TileSize, width - tx);
                    if (!TileDiffers(beforeBits, afterBits, width, tx, ty, tw, th)) continue;

                    _tiles.Add(new Tile(tx, ty, tw, th,
                        CopyTile(before, width, tx, ty, tw, th),
                        CopyTile(after, width, tx, ty, tw, th)));
                }
            }

            ByteSize = 0;
            foreach (var tile in _tiles)
                ByteSize += (long)tile.Width * tile.Height * 4 * 2;
        }

        /// <summary>True when the edit changed nothing (e.g. a click outside the selection).</summary>
        public bool IsEmpty => _tiles.Count == 0;

        /// <summary>The layer whose pixels this edit changed (null for a bare bitmap).</summary>
        public Guid? LayerId => _scene is null ? null : _layerId;

        /// <summary>Number of changed tiles kept - exposed for tests and diagnostics.</summary>
        public int TileCount => _tiles.Count;

        public long ByteSize { get; }

        public void Undo() => Apply(useBefore: true);

        public void Redo() => Apply(useBefore: false);

        private void Apply(bool useBefore)
        {
            if (_tiles.Count == 0) return;

            var target = ResolveTarget();
            if (target is null || target.Width != _width || target.Height != _height) return;

            using var canvas = new SKCanvas(target);
            using var paint = new SKPaint { BlendMode = SKBlendMode.Src };

            foreach (var tile in _tiles)
            {
                // Pixels are written through a small unpremultiplied BGRA bitmap, the same
                // format SKBitmap.Pixels uses, so the round trip matches the snapshot exactly.
                using var tileBitmap = new SKBitmap(new SKImageInfo(tile.Width, tile.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
                tileBitmap.Pixels = useBefore ? tile.Before : tile.After;
                canvas.DrawBitmap(tileBitmap, tile.X, tile.Y, paint);
            }

            canvas.Flush();
            target.NotifyPixelsChanged();
        }

        private SKBitmap? ResolveTarget()
        {
            if (_scene is null) return _target;

            foreach (var layer in _scene.Layers)
            {
                if (layer.Id != _layerId) continue;
                return _isMask ? layer.Mask?.Bitmap : layer.Bitmap;
            }

            return null;
        }

        private static bool TileDiffers(ReadOnlySpan<uint> a, ReadOnlySpan<uint> b, int stride, int x, int y, int w, int h)
        {
            for (var row = 0; row < h; row++)
            {
                var offset = (y + row) * stride + x;
                if (!a.Slice(offset, w).SequenceEqual(b.Slice(offset, w)))
                    return true;
            }

            return false;
        }

        private static SKColor[] CopyTile(SKColor[] source, int stride, int x, int y, int w, int h)
        {
            var tile = new SKColor[w * h];
            for (var row = 0; row < h; row++)
                Array.Copy(source, (y + row) * stride + x, tile, row * w, w);
            return tile;
        }
    }
}
