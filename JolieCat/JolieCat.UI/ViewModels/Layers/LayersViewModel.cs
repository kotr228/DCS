using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Documents;

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
        public const int DocumentWidth = 1600;
        public const int DocumentHeight = 1200;

        private readonly Document _document;

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

        /// <summary>Raised on anything the canvas should repaint for: a structural change
        /// (add/remove/reorder/merge) or a visual one bubbled up from a layer (visibility,
        /// opacity).</summary>
        public event EventHandler? InvalidateRequested;

        public LayersViewModel()
        {
            _document = new Document("Untitled");
            Scene.AddLayer("Background", DocumentWidth, DocumentHeight);
            RebuildFromScene();
        }

        partial void OnActiveLayerChanged(LayerViewModel? value) => Scene.ActiveLayer = value?.Model;

        [RelayCommand]
        private void AddLayer()
        {
            var layer = Scene.AddLayer($"Layer {Scene.Layers.Count + 1}", DocumentWidth, DocumentHeight);
            Scene.ActiveLayer = layer;
            RebuildFromScene();
            RaiseInvalidate();
        }

        [RelayCommand(CanExecute = nameof(HasActiveLayer))]
        private void DeleteLayer()
        {
            if (ActiveLayer is null) return;

            Scene.RemoveLayer(ActiveLayer.Model);
            RebuildFromScene();
            RaiseInvalidate();
        }

        [RelayCommand(CanExecute = nameof(HasActiveLayer))]
        private void MoveLayerUp()
        {
            if (ActiveLayer is null) return;

            Scene.MoveLayerUp(ActiveLayer.Model);
            RebuildFromScene();
            RaiseInvalidate();
        }

        [RelayCommand(CanExecute = nameof(HasActiveLayer))]
        private void MoveLayerDown()
        {
            if (ActiveLayer is null) return;

            Scene.MoveLayerDown(ActiveLayer.Model);
            RebuildFromScene();
            RaiseInvalidate();
        }

        /// <summary>Composites the active layer onto the one behind it (its own opacity
        /// and blend mode apply) and discards it - a no-op if it's already the backmost layer.</summary>
        [RelayCommand(CanExecute = nameof(HasActiveLayer))]
        private void MergeDown()
        {
            if (ActiveLayer is null) return;

            Scene.MergeLayerDown(ActiveLayer.Model);
            RebuildFromScene();
            RaiseInvalidate();
        }

        private bool HasActiveLayer() => ActiveLayer is not null;

        private void RebuildFromScene()
        {
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
