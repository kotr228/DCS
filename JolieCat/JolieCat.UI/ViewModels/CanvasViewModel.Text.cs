using System;
using System.Linq;
using JolieCat.Core.Documents;
using JolieCat.Core.History;
using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels.ToolOptions;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    public partial class CanvasViewModel
    {
        // ================= Text =================
        //
        // Text goes on its own editable text layer, like in Photoshop: clicking empty
        // canvas with a Text tool starts a new one; clicking existing text re-opens it
        // (the layer's pixels are hidden while the overlay textbox shows the text).
        // Enter / clicking elsewhere commits, Esc cancels.

        /// <summary>The text layer being re-edited, or null for a new text.</summary>
        private Layer? _editingTextLayer;
        private TextContent? _editingTextBefore;

        /// <summary>Starts a Text tool edit at <paramref name="doc"/>: re-opens the text
        /// there if there is one, else starts a new text.</summary>
        private void BeginTextAt(SKPoint doc)
        {
            // A previous edit still open is finalized first.
            CommitTextEdit();

            if (FindTextLayerAt(doc) is { } existing)
            {
                EditTextLayer(existing);
                return;
            }

            _editingTextLayer = null;
            _editingTextBefore = null;
            _textEditOrigin = doc;
            _isVerticalTextEdit = _toolbox.ActiveTool.Type == ToolType.TextVertical;
            TextEditContent = string.Empty;
            IsTextEditing = true;
            OnPropertyChanged(nameof(TextEditScreenX));
            OnPropertyChanged(nameof(TextEditScreenY));
        }

        /// <summary>The frontmost visible text layer whose text covers <paramref name="doc"/>
        /// (the active layer wins if it's one).</summary>
        private Layer? FindTextLayerAt(SKPoint doc)
        {
            static bool Hits(Layer layer, SKPoint p)
            {
                var bounds = layer.Text!.Bounds;
                bounds.Inflate(4, 4);
                return bounds.Contains(p);
            }

            var scene = Layers.Scene;
            if (Layers.ActiveLayer?.Model is { IsText: true, IsLocked: false } active && Hits(active, doc)) return active;

            for (var i = scene.Layers.Count - 1; i >= 0; i--)
            {
                var layer = scene.Layers[i];
                if (layer.IsText && !layer.IsLocked && scene.IsEffectivelyVisible(layer) && Hits(layer, doc)) return layer;
            }
            return null;
        }

        /// <summary>Re-opens a text layer for editing (also: double-clicking its
        /// thumbnail). The Text tool's options and the foreground color switch to the
        /// text's own settings.</summary>
        public void EditTextLayer(Layer layer)
        {
            if (layer.Text is not { } text) return;
            if (IsTextEditing) CommitTextEdit();

            if (Layers.Items.FirstOrDefault(item => item.Model == layer) is { } row)
                Layers.ActiveLayer = row;

            if (_toolbox.CurrentToolOptions is TextToolOptionsViewModel options)
            {
                options.FontFamily = text.FontFamily;
                options.FontSize = text.FontSize;
                options.IsBold = text.Bold;
                options.IsItalic = text.Italic;
            }
            PrimaryColor = System.Windows.Media.Color.FromArgb(text.Color.Alpha, text.Color.Red, text.Color.Green, text.Color.Blue);

            _editingTextLayer = layer;
            _editingTextBefore = text;
            _textEditOrigin = text.Origin;
            _isVerticalTextEdit = text.Vertical;
            TextEditContent = text.Text;

            // The overlay shows the text while editing; hide the layer's own copy.
            layer.Canvas.Clear(SKColors.Transparent);
            Layers.NotifyContentChanged();

            IsTextEditing = true;
            OnPropertyChanged(nameof(TextEditScreenX));
            OnPropertyChanged(nameof(TextEditScreenY));
        }

        /// <summary>The text as the Text tool's options and the foreground color say.</summary>
        private TextContent CurrentTextContent(string text)
        {
            var options = _toolbox.CurrentToolOptions as TextToolOptionsViewModel;
            return new TextContent(
                text,
                options?.FontFamily ?? _editingTextBefore?.FontFamily ?? "Segoe UI",
                (float)(options?.FontSize ?? _editingTextBefore?.FontSize ?? 24),
                options?.IsBold ?? _editingTextBefore?.Bold ?? false,
                options?.IsItalic ?? _editingTextBefore?.Italic ?? false,
                BrushColor,
                _textEditOrigin,
                _isVerticalTextEdit);
        }

        /// <summary>Ends the edit, keeping what was typed: a new text becomes a new text
        /// layer above the active one; a re-opened one is updated (one undo step each).
        /// Empty text adds nothing (and leaves a re-opened layer as it was).</summary>
        public void CommitTextEdit()
        {
            if (!IsTextEditing) return;

            var text = TextEditContent;
            var layer = _editingTextLayer;
            var before = _editingTextBefore;

            if (layer is not null && before is not null)
            {
                var after = string.IsNullOrEmpty(text) ? before : CurrentTextContent(text);
                layer.Text = after;
                layer.RenderText();
                if (after != before)
                    Layers.History.Push(new TextChangeCommand(Layers.Scene, layer, before, after), "Edit Text");
                else
                    Layers.NotifyContentChanged();
            }
            else if (!string.IsNullOrEmpty(text))
            {
                Layers.AddTextLayer(CurrentTextContent(text));
            }

            EndTextEdit();
            RaiseInvalidate();
        }

        /// <summary>Esc: ends the edit without changing anything.</summary>
        public void CancelTextEdit()
        {
            if (_editingTextLayer is { } layer)
            {
                layer.RenderText();
                Layers.NotifyContentChanged();
            }
            EndTextEdit();
            RaiseInvalidate();
        }

        private void EndTextEdit()
        {
            _editingTextLayer = null;
            _editingTextBefore = null;
            IsTextEditing = false;
            TextEditContent = string.Empty;
        }

        /// <summary>Pixel tools, filters and transforms can't work on live text: like
        /// Photoshop, the text layer is rasterized first (a separate, undoable step).</summary>
        private void RasterizeIfText(Layer? layer)
        {
            if (layer is { IsText: true }) Layers.RasterizeType(layer);
        }
    }
}
