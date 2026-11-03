using System;
using JolieCat.Core.Documents;
using SkiaSharp;

namespace JolieCat.Core.History
{
    /// <summary>A change to a Smart Filter layer's untouched original (e.g. moving it);
    /// undo/redo swap the original's pixels and run the filters again. The layer is
    /// found by Id, so the entry survives structural undo/redo in between.</summary>
    public sealed class SmartFilterSourceCommand : IEditCommand, ISizedEditCommand
    {
        private readonly Scene _scene;
        private readonly Guid _layerId;
        private readonly SKColor[] _before;
        private readonly SKColor[] _after;

        public SmartFilterSourceCommand(Scene scene, Layer layer, SKColor[] before, SKColor[] after)
        {
            _scene = scene ?? throw new ArgumentNullException(nameof(scene));
            _layerId = layer?.Id ?? throw new ArgumentNullException(nameof(layer));
            _before = before ?? throw new ArgumentNullException(nameof(before));
            _after = after ?? throw new ArgumentNullException(nameof(after));
        }

        public long ByteSize => (_before.LongLength + _after.LongLength) * 4;

        public void Undo() => Apply(_before);

        public void Redo() => Apply(_after);

        private void Apply(SKColor[] pixels)
        {
            foreach (var layer in _scene.Layers)
            {
                if (layer.Id != _layerId || layer.SmartFilterSource is not { } source) continue;
                source.Pixels = pixels;
                layer.RenderSmartFilters();
                return;
            }
        }
    }
}
