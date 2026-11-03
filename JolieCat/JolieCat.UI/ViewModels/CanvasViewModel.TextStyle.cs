using System;
using System.ComponentModel;
using System.Windows.Threading;
using JolieCat.Core.Documents;
using JolieCat.Core.History;
using JolieCat.UI.ViewModels.ToolOptions;

namespace JolieCat.UI.ViewModels
{
    /// <summary>
    /// Like Photoshop's Character panel: with a text layer selected (and the Text tool
    /// active), changing a text setting restyles that layer at once. A quick run of
    /// changes (dragging a slider) becomes one undo step.
    /// </summary>
    public partial class CanvasViewModel
    {
        private TextToolOptionsViewModel? _watchedTextOptions;
        private ShapeToolOptionsViewModel? _watchedShapeOptions;
        private ShapeContent? _reshapeBefore;
        private bool _loadingTextOptions;
        private Layer? _restyleLayer;
        private TextContent? _restyleBefore;
        private DispatcherTimer? _restyleTimer;

        /// <summary>Follows the Text tool's options while that tool is active.</summary>
        private void WatchTextOptions()
        {
            if (_watchedTextOptions is not null) _watchedTextOptions.PropertyChanged -= OnTextOptionChanged;
            _watchedTextOptions = _toolbox.CurrentToolOptions as TextToolOptionsViewModel;
            if (_watchedTextOptions is not null) _watchedTextOptions.PropertyChanged += OnTextOptionChanged;

            if (_watchedShapeOptions is not null) _watchedShapeOptions.PropertyChanged -= OnShapeOptionChanged;
            _watchedShapeOptions = _toolbox.CurrentToolOptions as ShapeToolOptionsViewModel;
            if (_watchedShapeOptions is not null) _watchedShapeOptions.PropertyChanged += OnShapeOptionChanged;
        }

        /// <summary>Shows <paramref name="text"/>'s settings in the Text tool's options
        /// without restyling anything.</summary>
        private void LoadTextOptions(TextContent text)
        {
            if (_toolbox.CurrentToolOptions is not TextToolOptionsViewModel options) return;
            _loadingTextOptions = true;
            try { options.LoadFrom(text); }
            finally { _loadingTextOptions = false; }
        }

        /// <summary>Shows <paramref name="shape"/>'s settings in the Shape tool's options.</summary>
        private void LoadShapeOptions(ShapeContent shape)
        {
            if (_toolbox.CurrentToolOptions is not ShapeToolOptionsViewModel options) return;
            _loadingTextOptions = true;
            try { options.LoadFrom(shape); }
            finally { _loadingTextOptions = false; }
        }

        private void OnShapeOptionChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_loadingTextOptions || sender is not ShapeToolOptionsViewModel options) return;
            if (e.PropertyName == nameof(ShapeToolOptionsViewModel.Mode)) return;
            if (Layers.ActiveLayer?.Model is not { Shape: { } shape } layer) return;

            var reshaped = options.ApplyTo(shape);
            if (reshaped == shape) return;

            if (!ReferenceEquals(_restyleLayer, layer))
            {
                FlushTextRestyle();
                _restyleLayer = layer;
                _reshapeBefore = shape;
            }

            layer.Shape = reshaped;
            layer.RenderShape();
            Layers.NotifyContentChanged();
            RaiseInvalidate();

            _restyleTimer ??= CreateRestyleTimer();
            _restyleTimer.Stop();
            _restyleTimer.Start();
        }

        private void OnTextOptionChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_loadingTextOptions || IsTextEditing || sender is not TextToolOptionsViewModel options) return;
            if (Layers.ActiveLayer?.Model is not { Text: { } text } layer) return;

            var restyled = options.ApplyTo(text);
            if (restyled == text) return;

            if (!ReferenceEquals(_restyleLayer, layer))
            {
                FlushTextRestyle();
                _restyleLayer = layer;
                _restyleBefore = text;
            }

            layer.Text = restyled;
            layer.RenderText();
            Layers.NotifyContentChanged();
            RaiseInvalidate();

            // One undo step once the changes settle.
            _restyleTimer ??= CreateRestyleTimer();
            _restyleTimer.Stop();
            _restyleTimer.Start();
        }

        private DispatcherTimer CreateRestyleTimer()
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            timer.Tick += (_, _) => FlushTextRestyle();
            return timer;
        }

        /// <summary>Records the pending restyle as one undo step (also called before any
        /// other edit, so steps stay in order).</summary>
        internal void FlushTextRestyle()
        {
            _restyleTimer?.Stop();
            if (_restyleLayer is { Text: { } after } layer && _restyleBefore is { } before && after != before)
                Layers.History.Push(new TextChangeCommand(Layers.Scene, layer, before, after), "Text Style");
            if (_restyleLayer is { Shape: { } shapeAfter } shapeLayer && _reshapeBefore is { } shapeBefore && shapeAfter != shapeBefore)
                Layers.History.Push(new ShapeChangeCommand(Layers.Scene, shapeLayer, shapeBefore, shapeAfter), "Shape Style");
            _restyleLayer = null;
            _restyleBefore = null;
            _reshapeBefore = null;
        }
    }
}
