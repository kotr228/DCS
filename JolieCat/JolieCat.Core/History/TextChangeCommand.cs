using System;
using JolieCat.Core.Documents;

namespace JolieCat.Core.History
{
    /// <summary>Editing (or moving) a text layer's text. Undo/redo swap the content
    /// and redraw the layer. The layer is found by Id, so the entry survives
    /// structural undo/redo in between.</summary>
    public sealed class TextChangeCommand : IEditCommand
    {
        private readonly Scene _scene;
        private readonly Guid _layerId;
        private readonly TextContent _before;
        private readonly TextContent _after;

        public TextChangeCommand(Scene scene, Layer layer, TextContent before, TextContent after)
        {
            _scene = scene ?? throw new ArgumentNullException(nameof(scene));
            _layerId = layer?.Id ?? throw new ArgumentNullException(nameof(layer));
            _before = before ?? throw new ArgumentNullException(nameof(before));
            _after = after ?? throw new ArgumentNullException(nameof(after));
        }

        public void Undo() => Apply(_before);

        public void Redo() => Apply(_after);

        private void Apply(TextContent content)
        {
            foreach (var layer in _scene.Layers)
            {
                if (layer.Id != _layerId) continue;
                layer.Text = content;
                layer.RenderText();
                return;
            }
        }
    }
}
