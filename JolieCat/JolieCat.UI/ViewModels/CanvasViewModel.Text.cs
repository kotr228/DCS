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

        // ----- Paragraph text: dragging a box with the Text tool -----

        private SKPoint? _textDragStart;
        private SKPoint _textDragEnd;

        /// <summary>Paragraph text's box width for the text being typed (0: point text).</summary>
        private float _textBoxWidth;

        /// <summary>The box being dragged out with the Text tool (for the canvas), or null.</summary>
        public SKRect? TextBoxDraft => _textDragStart is { } s && Math.Abs(_textDragEnd.X - s.X) >= 8
            ? new SKRect(Math.Min(s.X, _textDragEnd.X), Math.Min(s.Y, _textDragEnd.Y), Math.Max(s.X, _textDragEnd.X), Math.Max(s.Y, _textDragEnd.Y))
            : null;

        /// <summary>The paragraph box's width on screen (device pixels), 0 for point text.</summary>
        public double TextEditBoxScreenWidth => _textBoxWidth * Zoom;

        private void StartTextDrag(SKPoint doc)
        {
            CommitTextEdit();
            _textDragStart = doc;
            _textDragEnd = doc;
        }

        private bool UpdateTextDrag(SKPoint doc)
        {
            if (_textDragStart is null) return false;
            _textDragEnd = doc;
            RaiseInvalidate();
            return true;
        }

        /// <summary>Released: a click edits or starts point text; a drag starts paragraph text in the box.</summary>
        private void EndTextDrag()
        {
            if (_textDragStart is not { } start) return;
            var box = TextBoxDraft;
            _textDragStart = null;
            RaiseInvalidate();
            if (box is { } r && FindTextLayerAt(start) is null)
            {
                BeginTextAt(new SKPoint(r.Left, r.Top));
                _textBoxWidth = r.Width;
                OnPropertyChanged(nameof(TextEditBoxScreenWidth));
            }
            else
            {
                BeginTextAt(start);
            }
        }

        /// <summary>Starts paragraph text <paramref name="width"/> pixels wide at <paramref name="topLeft"/> (also for tests).</summary>
        public void BeginTextBox(SKPoint topLeft, float width)
        {
            BeginTextAt(topLeft);
            _textBoxWidth = Math.Max(1, width);
            OnPropertyChanged(nameof(TextEditBoxScreenWidth));
        }

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
            _textBoxWidth = 0;
            OnPropertyChanged(nameof(TextEditBoxScreenWidth));
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

            FlushTextRestyle();
            LoadTextOptions(text);
            PrimaryColor = System.Windows.Media.Color.FromArgb(text.Color.Alpha, text.Color.Red, text.Color.Green, text.Color.Blue);

            _editingTextLayer = layer;
            _editingTextBefore = text;
            _textBoxWidth = text.BoxWidth;
            OnPropertyChanged(nameof(TextEditBoxScreenWidth));
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

        /// <summary>Layer &gt; Type on Path: <paramref name="text"/> set along the Pen tool's
        /// path (Text tool options, foreground color), as a new text layer. False without a path.</summary>
        public bool TypeOnPenPath(string text)
        {
            if (PenPath is not { } pen || string.IsNullOrWhiteSpace(text)) return false;
            using var curve = pen.ToSKPath();
            if (curve.PointCount < 2) return false;
            var plain = new TextContent(text, "Segoe UI", 36, false, false, BrushColor, curve.Points[0], false);
            var content = (_toolbox.OptionsFor(ToolType.TextHorizontal) as TextToolOptionsViewModel)?.ApplyTo(plain) ?? plain;
            Layers.AddTextLayer(content with { Text = text, Color = BrushColor, Vertical = false, Warp = 0, PathData = curve.ToSvgPathData() });
            CancelPenPath();
            return true;
        }

        /// <summary>The text as the Text tool's options and the foreground color say.</summary>
        private TextContent CurrentTextContent(string text)
        {
            var options = _toolbox.CurrentToolOptions as TextToolOptionsViewModel;
            var content = (_editingTextBefore ?? new TextContent(text, "Segoe UI", 24, false, false, BrushColor, _textEditOrigin, _isVerticalTextEdit,
                    BoxWidth: _isVerticalTextEdit ? 0 : _textBoxWidth))
                with { Text = text, Color = BrushColor, Origin = _textEditOrigin, Vertical = _isVerticalTextEdit };
            return options is null ? content : options.ApplyTo(content);
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
            if (layer is { IsShape: true }) Layers.RasterizeShape(layer);
            if (layer is { IsFill: true }) Layers.RasterizeFill(layer);
        }
    }
}
