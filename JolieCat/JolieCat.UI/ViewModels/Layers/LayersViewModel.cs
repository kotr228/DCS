using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Documents;
using JolieCat.Core.History;
using JolieCat.Shared.Enums;
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
    public partial class LayersViewModel : ObservableObject, IDisposable
    {
        private bool _disposed;


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

        /// <summary>Chosen once when the document was created (or loaded) - see
        /// <see cref="Shared.Enums.ProjectType"/>'s own remarks for what each mode
        /// changes about the workspace. Never changes afterward, so this needs no
        /// change notification of its own - only <see cref="LoadScene"/> (a whole new
        /// <see cref="Document"/>) or the constructor ever sets it.</summary>
        public ProjectType ProjectType => _document.ProjectType;

        public bool IsSpriteSheetProject => ProjectType == ProjectType.SpriteSheet;

        public bool IsClipbarAnimationProject => ProjectType == ProjectType.ClipbarAnimation;

        /// <summary>This project's Sprite Sheet slicing grid (see <see cref="Core.Documents.SpriteSheetGrid"/>) -
        /// present regardless of <see cref="ProjectType"/>, meaningful only for
        /// <see cref="IsSpriteSheetProject"/>. Consumed directly (not through the
        /// bindable <see cref="SpriteSheetColumns"/>-and-friends mirror below) by
        /// <see cref="Rendering.CanvasRenderer"/>'s overlay and by the "Slice &amp;
        /// Export Frames" command's call into <c>ImageExportService.ExportSpriteSheetCellsAsync</c>.</summary>
        public Core.Documents.SpriteSheetGrid SpriteSheetGrid => _document.SpriteSheetGrid;

        /// <summary>Whether the Rectangular/Elliptical Marquee tools snap their drag to
        /// the Sprite Sheet grid's own lines (see <see cref="Core.Documents.SpriteSheetGrid.SnapPoint"/>) -
        /// a plain per-session toggle, not persisted, meaningful only for
        /// <see cref="IsSpriteSheetProject"/>.</summary>
        [ObservableProperty]
        private bool snapToSpriteSheetGrid;

        // ================= Sprite Sheet grid - bindable mirror =================
        //
        // Core.Documents.SpriteSheetGrid is a plain data/math class (no property-
        // changed notifications of its own, so CanvasRenderer/ImageExportService can
        // consume it with no WPF dependency) - these six properties are this class's
        // own bindable front for editing it from the Sprite Sheet panel, writing
        // straight through to SpriteSheetGrid on every change and requesting a
        // repaint, the same "thin ObservableObject mirror over a plain Core model"
        // pattern CanvasViewModel's Hue/Saturation/Brightness already use for
        // PrimaryColor. _isSyncingSpriteSheetGrid guards SyncSpriteSheetGridFields
        // (loading a project) from redundantly writing the just-loaded values back
        // into the very same SpriteSheetGrid instance they came from.

        private bool _isSyncingSpriteSheetGrid;

        [ObservableProperty]
        private int spriteSheetColumns = 4;

        [ObservableProperty]
        private int spriteSheetRows = 4;

        [ObservableProperty]
        private int spriteSheetPaddingX;

        [ObservableProperty]
        private int spriteSheetPaddingY;

        [ObservableProperty]
        private int spriteSheetMarginX;

        [ObservableProperty]
        private int spriteSheetMarginY;

        partial void OnSpriteSheetColumnsChanged(int value) => PushSpriteSheetGridField(() => SpriteSheetGrid.Columns = Math.Max(1, value));
        partial void OnSpriteSheetRowsChanged(int value) => PushSpriteSheetGridField(() => SpriteSheetGrid.Rows = Math.Max(1, value));
        partial void OnSpriteSheetPaddingXChanged(int value) => PushSpriteSheetGridField(() => SpriteSheetGrid.PaddingX = Math.Max(0, value));
        partial void OnSpriteSheetPaddingYChanged(int value) => PushSpriteSheetGridField(() => SpriteSheetGrid.PaddingY = Math.Max(0, value));
        partial void OnSpriteSheetMarginXChanged(int value) => PushSpriteSheetGridField(() => SpriteSheetGrid.MarginX = Math.Max(0, value));
        partial void OnSpriteSheetMarginYChanged(int value) => PushSpriteSheetGridField(() => SpriteSheetGrid.MarginY = Math.Max(0, value));

        private void PushSpriteSheetGridField(Action apply)
        {
            if (_isSyncingSpriteSheetGrid) return;

            apply();
            RaiseInvalidate();
        }

        /// <summary>Mirrors <see cref="SpriteSheetGrid"/>'s current values into the six
        /// bindable properties above - called once up front and again whenever
        /// <see cref="_document"/> is replaced wholesale (<see cref="LoadScene"/>), so
        /// the Sprite Sheet panel always reflects whichever document is actually open
        /// rather than the previous one's settings.</summary>
        private void SyncSpriteSheetGridFields()
        {
            _isSyncingSpriteSheetGrid = true;

            var grid = SpriteSheetGrid;
            SpriteSheetColumns = grid.Columns;
            SpriteSheetRows = grid.Rows;
            SpriteSheetPaddingX = grid.PaddingX;
            SpriteSheetPaddingY = grid.PaddingY;
            SpriteSheetMarginX = grid.MarginX;
            SpriteSheetMarginY = grid.MarginY;

            _isSyncingSpriteSheetGrid = false;
        }

        public LayersViewModel(ProjectType projectType = ProjectType.StandardImage)
        {
            _document = new Document("Untitled") { ProjectType = projectType };
            Scene.AddLayer("Background", DocumentWidth, DocumentHeight);
            RebuildFromScene();
            SyncSpriteSheetGridFields();

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

        /// <summary>Attaches a fresh, fully-visible mask to the active layer - a no-op
        /// if it already has one (see <see cref="Layer.AddMask"/>). Recorded as one
        /// undo/redo entry like any other layer-list operation.</summary>
        [RelayCommand(CanExecute = nameof(HasActiveLayer))]
        private void AddMask()
        {
            if (ActiveLayer is null) return;

            var layer = ActiveLayer.Model;
            ExecuteStructuralChange(() => layer.AddMask());
            ActiveLayer?.RefreshMaskThumbnail();
        }

        /// <summary>Removes the active layer's mask - a no-op if it has none (see
        /// <see cref="Layer.RemoveMask"/>). Recorded as one undo/redo entry like any
        /// other layer-list operation.</summary>
        [RelayCommand(CanExecute = nameof(HasActiveLayer))]
        private void RemoveMask()
        {
            if (ActiveLayer is null) return;

            var layer = ActiveLayer.Model;
            ExecuteStructuralChange(() => layer.RemoveMask());
            ActiveLayer?.RefreshMaskThumbnail();
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

        /// <summary>Crops the whole document to <paramref name="cropRect"/> (see
        /// <see cref="Scene.CropLayers"/>) - the Crop tool's commit step. One undo/redo
        /// entry, like every other structural change.</summary>
        public void CropDocument(SKRectI cropRect, float rotationDegrees) =>
            ExecuteStructuralChange(() => Scene.CropLayers(cropRect, rotationDegrees));

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

        /// <summary>Places a decoded image as a new topmost Smart Object layer (see
        /// <see cref="Layer.CreateSmartObject"/>) - non-destructively re-sampled from
        /// its own pristine source, unlike <see cref="ImportImage"/>'s plain raster
        /// layer, so scaling/rotating it later via Free Transform never compounds
        /// quality loss. Always wrapped in a tiny one-layer embedded <see cref="Scene"/>
        /// (not just a bare bitmap), so "Edit Contents" (see <c>MainViewModel.EditSmartObjectContents</c>)
        /// has a real sub-document to open for every Smart Object placed this way.
        /// Always adopts the document's current size for the new layer's own canvas
        /// (never resizes the document, unlike <see cref="ImportImage"/>) - a Smart
        /// Object's placement/size is meant to be adjusted afterward via Free
        /// Transform, not fixed up-front by resizing the whole canvas around it.</summary>
        public void PlaceSmartObject(SKBitmap sourceBitmap, string name)
        {
            ArgumentNullException.ThrowIfNull(sourceBitmap);

            ExecuteStructuralChange(() =>
            {
                var embeddedScene = new Scene(name);
                var embeddedLayer = embeddedScene.AddLayer("Content", sourceBitmap.Width, sourceBitmap.Height);
                embeddedLayer.Canvas.DrawBitmap(sourceBitmap, 0, 0);

                var layer = Layer.CreateSmartObject(name, DocumentWidth, DocumentHeight, sourceBitmap.Copy(), embeddedScene);
                Scene.AddLayer(layer);
                Scene.ActiveLayer = layer;
            });
        }

        /// <summary>Pastes <paramref name="content"/> (see <see cref="Core.Clipboard.PixelClipboard"/>)
        /// as a new topmost layer, drawn at (<paramref name="x"/>, <paramref name="y"/>) -
        /// not merged onto the active layer, so a paste can never overwrite existing
        /// content and is always its own undo/redo entry, exactly like
        /// <see cref="ImportImage"/>.</summary>
        public void PasteAsNewLayer(SKBitmap content, int x, int y)
        {
            ArgumentNullException.ThrowIfNull(content);

            ExecuteStructuralChange(() =>
            {
                var layer = Scene.AddLayer($"Pasted {Scene.Layers.Count}", DocumentWidth, DocumentHeight);
                layer.Canvas.DrawBitmap(content, x, y);
                Scene.ActiveLayer = layer;
            });
        }

        /// <summary>Blits <paramref name="bitmap"/> into <paramref name="layer"/>'s
        /// buffer, always anchored at (0,0) - directly, if it's already exactly the
        /// layer's size (the common case once <see cref="ImportImage"/> has resized the
        /// document to match), otherwise scaled down/up to fit within the layer's
        /// bounds, preserving aspect ratio rather than stretched.</summary>
        private static void DrawImageIntoLayer(Layer layer, SKBitmap bitmap)
        {
            if (bitmap.Width == layer.Bitmap.Width && bitmap.Height == layer.Bitmap.Height)
            {
                layer.Canvas.DrawBitmap(bitmap, 0, 0);
                return;
            }

            var scale = Math.Min((float)layer.Bitmap.Width / bitmap.Width, (float)layer.Bitmap.Height / bitmap.Height);

            // Rounded to whole pixels, not left as fractional values - every other
            // bitmap placement in this codebase (Scene.ResizeLayers, ProjectSerializer's
            // own mismatch handling, the same-size branch just above) lands on an exact
            // pixel boundary, and a fractional destination rect here would be the one
            // exception: combined with antialiasing, a sub-pixel edge softens into a
            // faint blended line along whichever side lands off-grid - most visible at
            // the top, where the checkerboard's contrast against it is strongest.
            var scaledWidth = (float)Math.Round(bitmap.Width * scale);
            var scaledHeight = (float)Math.Round(bitmap.Height * scale);

            // Anchored at (0,0) - not centered. Every other layer/bitmap placement in
            // this app (including the same-size branch above) is top-left anchored;
            // centering here was the one exception, and its offset (however small)
            // reads as this image having shifted relative to everything else in the
            // scene, which is always measured from the same (0,0) origin.
            var dest = SKRect.Create(0, 0, scaledWidth, scaledHeight);

            using var image = SKImage.FromBitmap(bitmap);
            using var paint = new SKPaint { IsAntialias = false };
            layer.Canvas.DrawImage(image, dest, new SKSamplingOptions(SKFilterMode.Linear), paint);
        }

        /// <summary>Replaces the open document with <paramref name="scene"/> - used when
        /// loading a <c>.jolie</c> project (see <c>ProjectSerializer.Load</c>). The old
        /// history no longer describes anything meaningful (its commands reference layer
        /// objects belonging to the document being replaced), so it's discarded rather
        /// than carried over. <paramref name="projectType"/>/<paramref name="spriteSheetGrid"/>
        /// default to a plain <see cref="Shared.Enums.ProjectType.StandardImage"/>
        /// project's own defaults - the right choice for a caller that isn't restoring
        /// a saved project's own settings at all (e.g. opening a Smart Object layer's
        /// embedded scene for "Edit Contents" - see <c>MainViewModel.EditSmartObjectContents</c>,
        /// which never wants that embedded editing session mistaken for a Sprite Sheet
        /// or Clipbar Animation project just because the parent project happened to be
        /// one).</summary>
        public void LoadScene(Scene scene, string documentName, ProjectType projectType = ProjectType.StandardImage, Core.Documents.SpriteSheetGrid? spriteSheetGrid = null)
        {
            ArgumentNullException.ThrowIfNull(scene);

            _document = new Document(documentName, scene) { ProjectType = projectType };
            if (spriteSheetGrid is not null)
                _document.SpriteSheetGrid = spriteSheetGrid;

            History.Clear();
            RebuildFromScene();
            SyncSpriteSheetGridFields();
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
            // DocumentWidth/DocumentHeight are computed from Scene.Layers[0], not
            // fields, so replacing the scene wholesale (LoadScene) or resizing/adding/
            // removing its first layer (including via undo/redo) never touches a
            // setter that would otherwise raise this - every path that can change
            // either value runs through this method (directly from LoadScene, or via
            // History.Changed after ExecuteStructuralChange/undo/redo), so this is the
            // one place both need to be (re-)announced from. A no-op for a viewer that
            // isn't bound to either, so it's raised unconditionally rather than only
            // when the value actually changed.
            OnPropertyChanged(nameof(DocumentWidth));
            OnPropertyChanged(nameof(DocumentHeight));

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

        /// <summary>Public counterpart of <see cref="RaiseInvalidate"/> for a caller
        /// outside this class that just mutated a <see cref="Layer"/> directly (not
        /// through <see cref="Layers.LayerViewModel"/>, so nothing here would otherwise
        /// notice) and needs the canvas repainted - currently only
        /// <c>DocumentViewModel</c>, wiring <c>Timeline.FrameVisibilityChanged</c> to
        /// this whenever the Timeline's own flipbook logic flips a frame layer's
        /// visibility.</summary>
        public void RequestRepaint() => RaiseInvalidate();

        /// <summary>Disposes <see cref="Scene"/> - and so every layer's (and mask's)
        /// unmanaged <see cref="SKBitmap"/>s - needed now that closing a document tab
        /// (see <c>MainViewModel.CloseDocument</c>) is a real, reachable action rather
        /// than something that only ever happened at process exit.</summary>
        public void Dispose()
        {
            if (_disposed) return;

            Scene.Dispose();
            _disposed = true;
        }
    }
}
