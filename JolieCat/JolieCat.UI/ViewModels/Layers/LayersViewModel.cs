using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Documents;
using JolieCat.Core.History;
using SkiaSharp;

namespace JolieCat.UI.ViewModels.Layers
{
    /// <summary>
    /// Owns the open <see cref="Core.Documents.Document"/> and drives the Layers panel:
    /// the bindable, front-to-back-ordered list the ListBox shows, which layer is active
    /// (what painting tools draw onto - see <see cref="CanvasViewModel"/>), and the
    /// standard layer operations (add/delete/reorder/merge down). <see cref="Rendering.CanvasRenderer"/>
    /// reads <see cref="Scene"/> directly to composite every visible layer back-to-front.
    /// </summary>
    public partial class LayersViewModel : ObservableObject
    {
        /// <summary>Starting canvas size for a brand new document - only used before the
        /// first layer exists (after that, <see cref="DocumentWidth"/>/<see cref="DocumentHeight"/>
        /// are read from it directly, so they can never drift out of sync with the
        /// scene's actual layers, including across a canvas-resize undo/redo).</summary>
        private const int DefaultDocumentWidth = 1600;
        private const int DefaultDocumentHeight = 1200;

        private Document _document;

        /// <summary>The open document's canvas size - every layer's bitmap is exactly
        /// this size. Computed from the first layer rather than tracked separately, so a
        /// canvas resize (see <see cref="ResizeDocument"/>) - including undoing/redoing
        /// one - can never leave this reporting a stale size.</summary>
        public int DocumentWidth => Scene.Layers.Count > 0 ? Scene.Layers[0].Bitmap.Width : DefaultDocumentWidth;

        public int DocumentHeight => Scene.Layers.Count > 0 ? Scene.Layers[0].Bitmap.Height : DefaultDocumentHeight;

        /// <summary>The layers panel's list, front-to-back (topmost/foreground layer
        /// first) - the reverse of <see cref="Core.Documents.Scene.Layers"/>' back-to-
        /// front storage order, matching how a Layers panel conventionally lists them.
        /// Rebuilt wholesale on every structural change (add/remove/reorder/merge) rather
        /// than patched incrementally - simple and safe for the handful of layers a
        /// document is ever likely to have.</summary>
        public ObservableCollection<LayerViewModel> Items { get; } = new();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(DeleteLayerCommand))]
        [NotifyCanExecuteChangedFor(nameof(MoveLayerUpCommand))]
        [NotifyCanExecuteChangedFor(nameof(MoveLayerDownCommand))]
        [NotifyCanExecuteChangedFor(nameof(MergeDownCommand))]
        private LayerViewModel? activeLayer;

        /// <summary>The Core scene the active document composites from. Concrete
        /// <see cref="Core.Documents.Scene"/> (not an <c>IScene</c>) - see that class's
        /// remarks for why.</summary>
        public Scene Scene => _document.Scene;

        /// <summary>This document's undo/redo stack - shared with <see cref="CanvasViewModel"/>
        /// (pixel-level edits: strokes, fills, text) as well as this class's own
        /// structural operations (add/delete/reorder/merge), so Ctrl+Z/Ctrl+Y undo both
        /// kinds in the single chronological order the user actually performed them in.</summary>
        public HistoryManager History { get; } = new();

        /// <summary>Raised on anything the canvas should repaint for: a structural change
        /// (add/remove/reorder/merge) or a visual one bubbled up from a layer (visibility,
        /// opacity).</summary>
        public event EventHandler? InvalidateRequested;

        public LayersViewModel()
        {
            _document = new Document("Untitled");
            Scene.AddLayer("Background", DocumentWidth, DocumentHeight);
            RebuildFromScene();

            // Undo/redo (of either kind - see History's own remarks) needs the Layers
            // list rebuilt and the canvas repainted exactly like a fresh structural
            // change or paint stroke would - so drive both off this one event instead of
            // duplicating that logic at every call site that pushes a command.
            History.Changed += (_, _) =>
            {
                RebuildFromScene();
                RaiseInvalidate();
            };
        }

        partial void OnActiveLayerChanged(LayerViewModel? value) => Scene.ActiveLayer = value?.Model;

        [RelayCommand]
        private void AddLayer() => ExecuteStructuralChange(() =>
        {
            var layer = Scene.AddLayer($"Layer {Scene.Layers.Count + 1}", DocumentWidth, DocumentHeight);
            Scene.ActiveLayer = layer;
        });

        [RelayCommand(CanExecute = nameof(HasActiveLayer))]
        private void DeleteLayer()
        {
            if (ActiveLayer is null) return;

            var layer = ActiveLayer.Model;
            ExecuteStructuralChange(() => Scene.RemoveLayer(layer));
        }

        [RelayCommand(CanExecute = nameof(HasActiveLayer))]
        private void MoveLayerUp()
        {
            if (ActiveLayer is null) return;

            var layer = ActiveLayer.Model;
            ExecuteStructuralChange(() => Scene.MoveLayerUp(layer));
        }

        [RelayCommand(CanExecute = nameof(HasActiveLayer))]
        private void MoveLayerDown()
        {
            if (ActiveLayer is null) return;

            var layer = ActiveLayer.Model;
            ExecuteStructuralChange(() => Scene.MoveLayerDown(layer));
        }

        /// <summary>Composites the active layer onto the one behind it (its own opacity
        /// and blend mode apply) and discards it - a no-op if it's already the backmost layer.</summary>
        [RelayCommand(CanExecute = nameof(HasActiveLayer))]
        private void MergeDown()
        {
            if (ActiveLayer is null) return;

            var layer = ActiveLayer.Model;
            ExecuteStructuralChange(() => Scene.MergeLayerDown(layer));
        }

        /// <summary>Resizes the whole document's canvas to (<paramref name="newWidth"/>,
        /// <paramref name="newHeight"/>) - every existing layer's content is preserved,
        /// anchored at its top-left corner (cropped if shrinking, padded with
        /// transparency if growing; see <see cref="Scene.ResizeLayers"/>). A no-op if the
        /// document is already that size. Recorded as one undo/redo entry like any other
        /// layer-list operation.</summary>
        public void ResizeDocument(int newWidth, int newHeight)
        {
            if (newWidth == DocumentWidth && newHeight == DocumentHeight) return;

            ExecuteStructuralChange(() => Scene.ResizeLayers(newWidth, newHeight));
        }

        /// <summary>Imports a decoded image as a new topmost raster layer named <paramref name="name"/>,
        /// then makes it the active layer. If <paramref name="bitmap"/> doesn't already
        /// match the document's size, either the whole document is resized to the
        /// image's own size first (<paramref name="resizeDocumentToMatch"/> true), or the
        /// image is scaled down/up to fit within the document's current bounds -
        /// preserving its aspect ratio and centered, never stretched - onto a layer at
        /// the document's own size (false). Either way, every layer in the scene stays
        /// the same size as every other, the invariant the renderer and every painting
        /// tool assume.</summary>
        public void ImportImage(SKBitmap bitmap, string name, bool resizeDocumentToMatch)
        {
            ArgumentNullException.ThrowIfNull(bitmap);

            // The resize (if any) and the new layer are one single undo/redo entry, not
            // two - ExecuteStructuralChange's PushStructural clears existing history on
            // every call, so calling it twice here (once via ResizeDocument, once for the
            // layer add) would silently discard the resize's own history entry the
            // moment the layer-add's PushStructural ran right after it.
            ExecuteStructuralChange(() =>
            {
                if (resizeDocumentToMatch)
                    Scene.ResizeLayers(bitmap.Width, bitmap.Height);

                var layer = Scene.AddLayer(name, DocumentWidth, DocumentHeight);
                DrawImageIntoLayer(layer, bitmap);
                Scene.ActiveLayer = layer;
            });
        }

        /// <summary>Blits <paramref name="bitmap"/> into <paramref name="layer"/>'s
        /// buffer - directly at (0,0) if it's already exactly the layer's size (the
        /// common case once <see cref="ImportImage"/> has resized the document to
        /// match), otherwise scaled down/up to fit within the layer's bounds, preserving
        /// aspect ratio and centered rather than stretched.</summary>
        private static void DrawImageIntoLayer(Layer layer, SKBitmap bitmap)
        {
            if (bitmap.Width == layer.Bitmap.Width && bitmap.Height == layer.Bitmap.Height)
            {
                layer.Canvas.DrawBitmap(bitmap, 0, 0);
                return;
            }

            var scale = Math.Min((float)layer.Bitmap.Width / bitmap.Width, (float)layer.Bitmap.Height / bitmap.Height);
            var scaledWidth = bitmap.Width * scale;
            var scaledHeight = bitmap.Height * scale;
            var dest = SKRect.Create(
                (layer.Bitmap.Width - scaledWidth) / 2f,
                (layer.Bitmap.Height - scaledHeight) / 2f,
                scaledWidth, scaledHeight);

            using var image = SKImage.FromBitmap(bitmap);
            using var paint = new SKPaint { IsAntialias = true };
            layer.Canvas.DrawImage(image, dest, new SKSamplingOptions(SKFilterMode.Linear), paint);
        }

        /// <summary>Replaces the open document with <paramref name="scene"/> - used when
        /// loading a <c>.jolie</c> project (see <c>ProjectSerializer.Load</c>). The old
        /// history no longer describes anything meaningful (its commands reference layer
        /// objects belonging to the document being replaced), so it's discarded rather
        /// than carried over.</summary>
        public void LoadScene(Scene scene, string documentName)
        {
            ArgumentNullException.ThrowIfNull(scene);

            _document = new Document(documentName, scene);
            History.Clear();
            RebuildFromScene();
            RaiseInvalidate();
        }

        private bool HasActiveLayer() => ActiveLayer is not null;

        /// <summary>Runs a layer-list operation (add/delete/reorder/merge), recording it
        /// as one undo/redo entry via a whole-scene before/after snapshot - see
        /// <see cref="HistoryManager.PushStructural"/> for why every structural change
        /// uses the same snapshot mechanism rather than a bespoke inverse per operation.
        /// The Layers list rebuild and canvas repaint both happen via <see cref="History"/>'s
        /// own Changed subscription (see the constructor), so this doesn't need to call
        /// either itself.</summary>
        private void ExecuteStructuralChange(Action operation)
        {
            var before = Scene.CaptureLayers();
            var beforeActiveIndex = IndexOfLayer(ActiveLayer?.Model);

            operation();

            var after = Scene.CaptureLayers();
            var afterActiveIndex = IndexOfLayer(Scene.ActiveLayer);

            History.PushStructural(new SceneStructuralCommand(Scene, before, beforeActiveIndex, after, afterActiveIndex));
        }

        private int IndexOfLayer(Layer? layer)
        {
            if (layer is null) return -1;

            for (var i = 0; i < Scene.Layers.Count; i++)
                if (ReferenceEquals(Scene.Layers[i], layer)) return i;

            return -1;
        }

        private void RebuildFromScene()
        {
            // A pixel-only undo/redo (a paint stroke, fill, or text commit - pushed via
            // History.Push, not PushStructural) never touches the layer list or its
            // order at all - History's Changed event fires the same way regardless, so
            // detect that case here and skip the wholesale rebuild below. Otherwise
            // undoing/redoing a stroke would needlessly flicker/reset the Layers panel's
            // list and re-wire every item's VisualStateChanged subscription for no
            // reason - Items is front-to-back, the reverse of Scene.Layers' own order.
            if (Items.Select(item => item.Model).SequenceEqual(Scene.Layers.Reverse()))
            {
                ActiveLayer = Items.FirstOrDefault(item => item.Model == Scene.ActiveLayer) ?? Items.FirstOrDefault();
                return;
            }

            foreach (var item in Items)
                item.VisualStateChanged -= OnLayerVisualStateChanged;

            Items.Clear();

            for (var i = Scene.Layers.Count - 1; i >= 0; i--)
            {
                var itemViewModel = new LayerViewModel(Scene.Layers[i]);
                itemViewModel.VisualStateChanged += OnLayerVisualStateChanged;
                Items.Add(itemViewModel);
            }

            ActiveLayer = Items.FirstOrDefault(item => item.Model == Scene.ActiveLayer) ?? Items.FirstOrDefault();
        }

        private void OnLayerVisualStateChanged(object? sender, EventArgs e) => RaiseInvalidate();

        private void RaiseInvalidate() => InvalidateRequested?.Invoke(this, EventArgs.Empty);
    }
}
