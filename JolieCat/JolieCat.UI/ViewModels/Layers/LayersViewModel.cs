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
            : this(projectType, DefaultDocumentWidth, DefaultDocumentHeight, SKColors.White)
        {
        }

        /// <param name="background">Fill for the "Background" layer; null or
        /// transparent leaves it transparent.</param>
        public LayersViewModel(ProjectType projectType, int width, int height, SKColor? background)
        {
            _document = new Document("Untitled") { ProjectType = projectType };
            var backgroundLayer = Scene.AddLayer("Background", Math.Max(1, width), Math.Max(1, height));
            if (background is { Alpha: > 0 } fill)
                backgroundLayer.Canvas.Clear(fill);
            RebuildFromScene();
            SyncSpriteSheetGridFields();

            // Undo/redo (of either kind - see History's own remarks) needs the Layers
            // list rebuilt and the canvas repainted exactly like a fresh structural
            // change or paint stroke would - so drive both off this one event instead of
            // duplicating that logic at every call site that pushes a command.
            History.Changed += (_, _) =>
            {
                RebuildFromScene();
                RefreshThumbnails(all: History.LastChangeWasUndoOrRedo);
                RaiseInvalidate();
            };
        }

        partial void OnActiveLayerChanged(LayerViewModel? value)
        {
            Scene.ActiveLayer = value?.Model;
            UngroupCommand.NotifyCanExecuteChanged();
            ToggleClippingMaskCommand.NotifyCanExecuteChanged();
            RasterizeActiveTypeCommand.NotifyCanExecuteChanged();
        }

        /// <summary>A new edit only ever changes the active layer, so only its thumbnail
        /// is rebuilt then (cheap); undo/redo can touch any layer, so all are.</summary>
        private void RefreshThumbnails(bool all)
        {
            if (all)
            {
                foreach (var item in Items) item.RefreshThumbnail();
            }
            else
            {
                ActiveLayer?.RefreshThumbnail();
            }
        }

        [RelayCommand]
        private void AddLayer()
        {
            AddLayerCore();
            Recorded();
        }

        private void AddLayerCore() => ExecuteStructuralChange(() =>
        {
            // Like Photoshop: directly above the active layer (or group), in the same group.
            var layer = new Layer($"Layer {Scene.Layers.Count(l => !l.IsGroup) + 1}", DocumentWidth, DocumentHeight);
            if (Scene.ActiveLayer is { } active && Scene.IndexOf(active) >= 0)
            {
                layer.ParentId = active.ParentId;
                Scene.InsertLayer(Scene.BlockOf(active).End + 1, layer);
            }
            else
            {
                Scene.AddLayer(layer);
            }
            Scene.ActiveLayer = layer;
        });

        // ================= Text layers =================

        /// <summary>A new text layer above the active layer (in its group). One undo step.</summary>
        public void AddTextLayer(TextContent content)
        {
            ExecuteStructuralChange(() =>
            {
                var firstLine = content.Text.Replace("\r", "").Split('\n')[0].Trim();
                var name = string.IsNullOrEmpty(firstLine) ? "Text" : firstLine.Length > 30 ? firstLine[..30] + "…" : firstLine;
                var layer = new Layer(name, DocumentWidth, DocumentHeight, LayerType.Text) { Text = content };
                layer.RenderText();

                if (Scene.ActiveLayer is { } active && Scene.IndexOf(active) >= 0)
                {
                    layer.ParentId = active.ParentId;
                    Scene.InsertLayer(Scene.BlockOf(active).End + 1, layer);
                }
                else
                {
                    Scene.AddLayer(layer);
                }
                Scene.ActiveLayer = layer;
            });
        }

        private bool CanRasterizeType() => ActiveLayer?.Model.IsText == true;

        /// <summary>Layer &gt; Rasterize Type: the text layer becomes ordinary pixels.</summary>
        [RelayCommand(CanExecute = nameof(CanRasterizeType))]
        private void RasterizeActiveType()
        {
            if (ActiveLayer?.Model is { IsText: true } layer) RasterizeType(layer);
        }

        /// <summary>Turns a text layer into a plain pixel layer (same pixels). Undoable.</summary>
        /// <summary>A new editable shape layer above the active layer (in its group).</summary>
        public void AddShapeLayer(ShapeContent shape)
        {
            ExecuteStructuralChange(() =>
            {
                var layer = new Layer($"{System.Text.RegularExpressions.Regex.Replace(shape.Kind.ToString(), "(?<=[a-z])(?=[A-Z])", " ")} {Scene.Layers.Count(l => l.IsShape) + 1}",
                    DocumentWidth, DocumentHeight) { Shape = shape };
                layer.RenderShape();
                if (Scene.ActiveLayer is { } active && Scene.IndexOf(active) >= 0)
                {
                    layer.ParentId = active.ParentId;
                    Scene.InsertLayer(Scene.BlockOf(active).End + 1, layer);
                }
                else
                {
                    Scene.AddLayer(layer);
                }
                Scene.ActiveLayer = layer;
            });
        }

        /// <summary>A new fill layer above the active layer; with a selection, its mask
        /// shows the fill only there (like Photoshop).</summary>
        public void AddFillLayer(FillContent fill)
        {
            ExecuteStructuralChange(() =>
            {
                var layer = new Layer($"{fill.Title} {Scene.Layers.Count(l => l.IsFill) + 1}", DocumentWidth, DocumentHeight) { Fill = fill };
                layer.RenderFill();
                if (Scene.Selection is { HasSelection: true, Path: { } path })
                {
                    var mask = layer.AddMask();
                    mask.Canvas.Clear(SKColors.Black);
                    using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
                    mask.Canvas.DrawPath(path, paint);
                }
                if (Scene.ActiveLayer is { } active && Scene.IndexOf(active) >= 0)
                {
                    layer.ParentId = active.ParentId;
                    Scene.InsertLayer(Scene.BlockOf(active).End + 1, layer);
                }
                else
                {
                    Scene.AddLayer(layer);
                }
                Scene.ActiveLayer = layer;
            });
            ActiveLayer?.RefreshMaskThumbnail();
        }

        /// <summary>Turns a fill layer into ordinary pixels (before painting on it).</summary>
        public void RasterizeFill(Layer layer)
        {
            if (!layer.IsFill) return;
            ExecuteStructuralChange(() => layer.Fill = null);
        }

        /// <summary>Turns a shape layer into ordinary pixels (before painting on it).</summary>
        public void RasterizeShape(Layer layer)
        {
            if (!layer.IsShape) return;
            ExecuteStructuralChange(() => layer.Shape = null);
        }

        public void RasterizeType(Layer layer)
        {
            if (!layer.IsText) return;
            ExecuteStructuralChange(() => layer.Text = null);
        }

        // ================= Groups =================

        /// <summary>Layer &gt; New &gt; Group: an empty group above the active layer.</summary>
        [RelayCommand]
        private void NewGroup() => ExecuteStructuralChange(() =>
            Scene.NewGroup(Scene.ActiveLayer, DocumentWidth, DocumentHeight, NextGroupName()));

        /// <summary>Ctrl+G: puts the selected layers (or just the active one) into a new group.</summary>
        [RelayCommand(CanExecute = nameof(HasActiveLayer))]
        private void GroupLayers()
        {
            if (ActiveLayer is null) return;
            var layers = SelectionForCommand();
            ExecuteStructuralChange(() => Scene.GroupLayers(layers, NextGroupName()));
        }

        // ================= Multi-selection =================

        private List<Layer> _selectedLayers = new();

        /// <summary>Layers picked in the Layers panel with Ctrl/Shift+click (the active
        /// layer is always one of them). Set by the view.</summary>
        public IReadOnlyList<Layer> SelectedLayers => _selectedLayers;

        public void SetSelectedLayers(IEnumerable<Layer> layers)
        {
            _selectedLayers = layers.Distinct().ToList();
            MergeDownCommand.NotifyCanExecuteChanged();
        }

        /// <summary>What a layer command acts on: the selection, if the active layer
        /// is part of a multi-selection, else just the active layer.</summary>
        private List<Layer> SelectionForCommand()
        {
            var active = ActiveLayer?.Model;
            if (active is null) return new List<Layer>();
            var live = _selectedLayers.Where(l => Scene.IndexOf(l) >= 0).ToList();
            return live.Count > 1 && live.Contains(active) ? live : new List<Layer> { active };
        }

        /// <summary>Drag and drop in the Layers panel: moves the selection (or the
        /// dragged layer) next to - or into - <paramref name="target"/>. One undo step.</summary>
        public bool MoveLayers(Layer dragged, Layer target, LayerDropPosition position)
        {
            var layers = _selectedLayers.Contains(dragged) && _selectedLayers.Count > 1 ? _selectedLayers.ToList() : new List<Layer> { dragged };
            var moved = false;
            var before = Scene.CaptureLayers();
            var beforeActive = IndexOfLayer(ActiveLayer?.Model);
            moved = Scene.MoveLayers(layers, target, position);
            if (!moved) return false;
            Scene.ActiveLayer = dragged;
            History.PushStructural(new SceneStructuralCommand(Scene, before, beforeActive, Scene.CaptureLayers(), IndexOfLayer(Scene.ActiveLayer)),
                layers.Count > 1 ? "Move Layers" : "Move Layer");
            return true;
        }

        private bool CanUngroup() => ActiveLayer?.Model.IsGroup == true;

        /// <summary>Ctrl+Shift+G: removes the active group, keeping its members.</summary>
        [RelayCommand(CanExecute = nameof(CanUngroup))]
        private void Ungroup()
        {
            if (ActiveLayer?.Model is not { IsGroup: true } group) return;
            ExecuteStructuralChange(() => Scene.Ungroup(group));
        }

        private bool CanToggleClippingMask() => ActiveLayer?.Model is { IsGroup: false } layer && Scene.IndexOf(layer) > 0;

        /// <summary>Ctrl+Alt+G: clips the active layer to the layer below it (Create
        /// Clipping Mask), or releases it if it already is.</summary>
        [RelayCommand(CanExecute = nameof(CanToggleClippingMask))]
        private void ToggleClippingMask()
        {
            if (ActiveLayer?.Model is not { IsGroup: false } layer) return;
            ExecuteStructuralChange(() => layer.IsClipped = !layer.IsClipped);
        }

        private string NextGroupName() => $"Group {Scene.Layers.Count(l => l.IsGroup) + 1}";

        /// <summary>A group was expanded/collapsed: rebuild the rows. Collapsing the
        /// group that holds the active layer selects the group instead.</summary>
        private void OnLayerExpandedChanged(object? sender, EventArgs e)
        {
            if (sender is LayerViewModel { Model: { IsGroup: true, IsExpanded: false } group }
                && Scene.ActiveLayer is { } active && Scene.IsDescendantOf(active, group))
                Scene.ActiveLayer = group;

            History.MarkModified();
            RebuildFromScene();
        }

        [RelayCommand(CanExecute = nameof(HasActiveLayer))]
        private void DeleteLayer()
        {
            DeleteLayerCore();
            Recorded();
        }

        private void DeleteLayerCore()
        {
            if (ActiveLayer is null) return;

            var layers = SelectionForCommand();
            ExecuteStructuralChange(() => Scene.RemoveLayers(layers));
        }

        /// <summary>Ctrl+J: a copy of the active layer, directly above it.</summary>
        [RelayCommand(CanExecute = nameof(HasActiveLayer))]
        private void DuplicateLayer()
        {
            DuplicateLayerCore();
            Recorded();
        }

        private void DuplicateLayerCore()
        {
            if (ActiveLayer is null) return;

            var layers = Scene.TopUnits(SelectionForCommand());
            ExecuteStructuralChange(() =>
            {
                // Top down, so each copy lands right above its original.
                foreach (var layer in Enumerable.Reverse(layers)) Scene.DuplicateLayer(layer);
            });
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
            MergeDownCore();
            Recorded();
        }

        private void MergeDownCore()
        {
            if (ActiveLayer is null) return;

            var layers = SelectionForCommand();
            if (layers.Count > 1)
            {
                // Several selected: Merge Layers.
                ExecuteStructuralChange(() => Scene.MergeLayers(layers));
                return;
            }

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
            // A group has nothing else to paint on: brushes go straight to its mask.
            if (layer.IsGroup) layer.IsMaskActive = true;
            ActiveLayer?.RefreshMaskThumbnail();
        }

        /// <summary>Layer > Layer Mask > Reveal Selection / Hide Selection: a new mask that
        /// shows (or hides) only the selected area.</summary>
        [RelayCommand(CanExecute = nameof(HasActiveLayer))]
        private void AddMaskFromSelection(string? mode)
        {
            if (ActiveLayer is null || Scene.Selection is not { HasSelection: true, Path: { } path }) return;
            var reveal = mode != "Hide";
            var layer = ActiveLayer.Model;
            ExecuteStructuralChange(() =>
            {
                var mask = layer.Mask ?? layer.AddMask();
                mask.Canvas.Clear(reveal ? SKColors.Black : SKColors.White);
                using var paint = new SKPaint { Color = reveal ? SKColors.White : SKColors.Black, IsAntialias = true };
                mask.Canvas.DrawPath(path, paint);
                mask.IsEnabled = true;
            });
            if (layer.IsGroup) layer.IsMaskActive = true;
            ActiveLayer?.RefreshMaskThumbnail();
        }

        /// <summary>Image > Crop: crops the document to the selection's bounds.</summary>
        [RelayCommand]
        private void CropToSelection()
        {
            if (Scene.Selection is not { HasSelection: true, Path: { } path }) return;
            var b = path.Bounds;
            var rect = SKRectI.Intersect(new SKRectI(0, 0, DocumentWidth, DocumentHeight),
                new SKRectI((int)Math.Floor(b.Left), (int)Math.Floor(b.Top), (int)Math.Ceiling(b.Right), (int)Math.Ceiling(b.Bottom)));
            if (rect.Width <= 0 || rect.Height <= 0) return;
            CropDocument(rect, 0);
            Scene.Selection.Clear();
        }

        /// <summary>Image > Trim: crops away transparent edges (<paramref name="mode"/>
        /// "Transparent") or edges the color of the top-left pixel ("TopLeft").</summary>
        [RelayCommand]
        private void Trim(string? mode)
        {
            using var flat = Core.Rendering.SceneCompositor.Flatten(Scene, DocumentWidth, DocumentHeight);
            SKColor? border = mode == "TopLeft" ? flat.GetPixel(0, 0) : null;
            var rect = Core.Documents.SelectionOps.TrimBounds(flat, border);
            if (rect.IsEmpty || (rect.Width == DocumentWidth && rect.Height == DocumentHeight)) return;
            CropDocument(rect, 0);
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
        public void ResizeDocument(int newWidth, int newHeight, bool centered = false)
        {
            if (newWidth == DocumentWidth && newHeight == DocumentHeight) return;

            var offsetX = centered ? (newWidth - DocumentWidth) / 2 : 0;
            var offsetY = centered ? (newHeight - DocumentHeight) / 2 : 0;
            ExecuteStructuralChange(() =>
            {
                Scene.ResizeLayers(newWidth, newHeight, offsetX, offsetY);
                Scene.Selection.Clear();
            });
        }

        /// <summary>Image &gt; Image Size: rescales every layer. Clears the selection,
        /// which no longer lines up with the content.</summary>
        public void ResampleDocument(int newWidth, int newHeight)
        {
            if (newWidth == DocumentWidth && newHeight == DocumentHeight) return;

            ExecuteStructuralChange(() =>
            {
                Scene.ResampleLayers(newWidth, newHeight);
                Scene.Selection.Clear();
            });
        }

        /// <summary>Image &gt; Image Rotation: 90° steps, clockwise for positive turns.</summary>
        [RelayCommand] private void Rotate90Clockwise()
        {
            Rotate90ClockwiseCore();
            Recorded();
        }

        private void Rotate90ClockwiseCore() => RotateDocument(1);
        [RelayCommand] private void Rotate90CounterClockwise()
        {
            Rotate90CounterClockwiseCore();
            Recorded();
        }

        private void Rotate90CounterClockwiseCore() => RotateDocument(-1);
        [RelayCommand] private void Rotate180()
        {
            Rotate180Core();
            Recorded();
        }

        private void Rotate180Core() => RotateDocument(2);
        [RelayCommand] private void FlipCanvasHorizontal()
        {
            FlipCanvasHorizontalCore();
            Recorded();
        }

        private void FlipCanvasHorizontalCore() => FlipDocument(true);
        [RelayCommand] private void FlipCanvasVertical()
        {
            FlipCanvasVerticalCore();
            Recorded();
        }

        private void FlipCanvasVerticalCore() => FlipDocument(false);
        [RelayCommand] private void FlipLayerHorizontal()
        {
            FlipLayerHorizontalCore();
            Recorded();
        }

        private void FlipLayerHorizontalCore() => FlipActiveLayer(true);
        [RelayCommand] private void FlipLayerVertical()
        {
            FlipLayerVerticalCore();
            Recorded();
        }

        private void FlipLayerVerticalCore() => FlipActiveLayer(false);

        public void RotateDocument(int quarterTurns) => ExecuteStructuralChange(() =>
        {
            Scene.RotateLayers(quarterTurns);
            Scene.Selection.Clear();
        });

        /// <summary>Image &gt; Image Rotation &gt; Flip Canvas Horizontal/Vertical.</summary>
        public void FlipDocument(bool horizontal) => ExecuteStructuralChange(() =>
        {
            Scene.FlipLayers(horizontal);
            Scene.Selection.Clear();
        });

        /// <summary>Edit &gt; Transform &gt; Flip Horizontal/Vertical for the active layer
        /// only - recorded as a cheap pixel edit, not a whole-scene snapshot.</summary>
        public void FlipActiveLayer(bool horizontal)
        {
            if (ActiveLayer?.Model is not { } layer || layer.IsLocked) return;

            var before = layer.Bitmap.Pixels;
            Scene.FlipLayerContent(layer, horizontal);
            History.Push(new LayerPixelsCommand(Scene, layer, false, before, layer.Bitmap.Pixels), "Flip Layer");
        }

        /// <summary>Layer &gt; Flatten Image.</summary>
        [RelayCommand]
        private void FlattenImage()
        {
            FlattenImageCore();
            Recorded();
        }

        private void FlattenImageCore()
        {
            if (Scene.Layers.Count < 2 && Scene.Layers.All(l => l.IsVisible)) return;
            ExecuteStructuralChange(Scene.Flatten);
        }

        /// <summary>Layer &gt; Merge Visible.</summary>
        [RelayCommand]
        private void MergeVisible()
        {
            MergeVisibleCore();
            Recorded();
        }

        private void MergeVisibleCore()
        {
            if (Scene.Layers.Count(l => l.IsVisible) < 2) return;
            ExecuteStructuralChange(() => Scene.MergeVisible());
        }

        /// <summary>Crops the whole document to <paramref name="cropRect"/> (see
        /// <see cref="Scene.CropLayers"/>) - the Crop tool's commit step. One undo/redo
        /// entry, like every other structural change.</summary>
        /// <summary>Filter > Convert for Smart Filters: filters applied to the layer from
        /// now on stay editable (see <c>CanvasViewModel.CommitAsSmartFilter</c>).</summary>
        [RelayCommand]
        private void ConvertForSmartFilters()
        {
            var layer = ActiveLayer?.Model;
            if (layer is null || layer.IsGroup || layer.IsAdjustment || layer.HasSmartFilters) return;
            ExecuteStructuralChange(() =>
            {
                if (layer.IsText) { layer.Text = null; }
                layer.SetSmartFilters(layer.Bitmap.Copy(), System.Array.Empty<Core.Effects.SmartFilter>());
            });
            RefreshActiveLayerBadges();
        }

        /// <summary>Layer > Smart Filters > Rasterize: keeps what the layer shows now as
        /// ordinary pixels and drops the filter list.</summary>
        [RelayCommand]
        private void RasterizeSmartFilters()
        {
            var layer = ActiveLayer?.Model;
            if (layer is not { HasSmartFilters: true }) return;
            ExecuteStructuralChange(layer.ClearSmartFilters);
            RefreshActiveLayerBadges();
        }

        private void RefreshActiveLayerBadges()
        {
            foreach (var item in Items) item.RefreshStyleState();
        }

        public void ContentAwareScaleDocument(Core.Filters.SeamCarvingPlan plan) =>
            ExecuteStructuralChange(() =>
            {
                Scene.ContentAwareScaleLayers(plan);
                Scene.Selection.Clear();
            });

        public void PerspectiveCropDocument(SKPoint[] quad, int width, int height) =>
            ExecuteStructuralChange(() => Scene.PerspectiveCropLayers(quad, width, height));

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

        // ================= Adjustment layers =================
        //
        // A new adjustment layer is added right away (so its dialog can preview live on
        // the canvas) but only recorded in history when the dialog is confirmed;
        // cancelling removes it again as if nothing happened.

        private IReadOnlyList<LayerSnapshot>? _pendingAdjustmentBefore;
        private int _pendingAdjustmentBeforeActive;
        private Layer? _pendingAdjustmentLayer;

        /// <summary>Adds an adjustment layer (with an all-white mask, which painting
        /// tools then edit) directly above the active layer and makes it active.</summary>
        public Layer BeginNewAdjustmentLayer(Core.Adjustments.AdjustmentSettings settings)
        {
            CancelNewAdjustmentLayer();

            _pendingAdjustmentBefore = Scene.CaptureLayers();
            _pendingAdjustmentBeforeActive = IndexOf(ActiveLayer?.Model);

            var layer = new Layer(settings.DisplayName, DocumentWidth, DocumentHeight, LayerType.Adjustment) { Adjustment = settings };
            layer.AddMask();
            layer.IsMaskActive = true;

            var index = _pendingAdjustmentBeforeActive < 0 ? Scene.Layers.Count : _pendingAdjustmentBeforeActive + 1;
            Scene.InsertLayer(index, layer);
            Scene.ActiveLayer = layer;
            _pendingAdjustmentLayer = layer;

            RebuildFromScene();
            RaiseInvalidate();
            return layer;
        }

        /// <summary>Records the pending new adjustment layer as one undo step.</summary>
        public void CommitNewAdjustmentLayer()
        {
            if (_pendingAdjustmentLayer is null || _pendingAdjustmentBefore is null) return;

            var after = Scene.CaptureLayers();
            History.PushStructural(new SceneStructuralCommand(Scene, _pendingAdjustmentBefore, _pendingAdjustmentBeforeActive,
                after, IndexOf(Scene.ActiveLayer)), "New Adjustment Layer");
            _pendingAdjustmentLayer = null;
            _pendingAdjustmentBefore = null;
        }

        /// <summary>Removes the pending new adjustment layer (dialog cancelled).</summary>
        public void CancelNewAdjustmentLayer()
        {
            if (_pendingAdjustmentLayer is not { } layer) return;

            _pendingAdjustmentLayer = null;
            _pendingAdjustmentBefore = null;
            Scene.RemoveLayer(layer);
            if (_pendingAdjustmentBeforeActive >= 0 && _pendingAdjustmentBeforeActive < Scene.Layers.Count)
                Scene.ActiveLayer = Scene.Layers[_pendingAdjustmentBeforeActive];

            RebuildFromScene();
            RaiseInvalidate();
        }

        /// <summary>Live-updates an adjustment layer's settings while its dialog is
        /// open (no history entry yet).</summary>
        public void PreviewAdjustment(Layer layer, Core.Adjustments.AdjustmentSettings settings)
        {
            layer.Adjustment = settings;
            if (ReferenceEquals(layer, _pendingAdjustmentLayer)) layer.Name = settings.DisplayName;
            RaiseInvalidate();
        }

        /// <summary>Records re-editing an existing adjustment layer as one undo step.</summary>
        public void CommitAdjustmentEdit(Layer layer, Core.Adjustments.AdjustmentSettings before, Core.Adjustments.AdjustmentSettings after)
        {
            layer.Adjustment = after;
            History.Push(new AdjustmentChangeCommand(Scene, layer, before, after));
        }

        /// <summary>Live-updates a layer's style while the Layer Style dialog is open
        /// (no history entry yet).</summary>
        public void PreviewLayerStyle(Layer layer, Core.Effects.LayerStyle? style)
        {
            layer.Style = style is { HasAnyEffect: true } ? style : null;
            RaiseInvalidate();
        }

        /// <summary>Records a layer style change as one undo step.</summary>
        public void CommitLayerStyle(Layer layer, Core.Effects.LayerStyle? before, Core.Effects.LayerStyle? after)
        {
            after = after is { HasAnyEffect: true } ? after : null;
            layer.Style = after;
            if (before == after) { RaiseInvalidate(); return; }

            History.Push(new LayerStyleChangeCommand(Scene, layer, before, after), after is null ? "Clear Layer Style" : "Layer Style");
            foreach (var item in Items) item.RefreshStyleState();
        }

        private int IndexOf(Layer? layer) => IndexOfLayer(layer);

        /// <summary>Runs a layer-list operation (add/delete/reorder/merge), recording it
        /// as one undo/redo entry via a whole-scene before/after snapshot - see
        /// <see cref="HistoryManager.PushStructural"/> for why every structural change
        /// uses the same snapshot mechanism rather than a bespoke inverse per operation.
        /// The Layers list rebuild and canvas repaint both happen via <see cref="History"/>'s
        /// own Changed subscription (see the constructor), so this doesn't need to call
        /// either itself.</summary>
        private void ExecuteStructuralChange(Action operation, [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
        {
            var before = Scene.CaptureLayers();
            var beforeActiveIndex = IndexOfLayer(ActiveLayer?.Model);
            var guidesBefore = Scene.Guides.ToList();

            operation();

            var after = Scene.CaptureLayers();
            var afterActiveIndex = IndexOfLayer(Scene.ActiveLayer);
            var guidesAfter = Scene.Guides.ToList();
            var guidesChanged = !guidesBefore.SequenceEqual(guidesAfter);

            History.PushStructural(new SceneStructuralCommand(Scene, before, beforeActiveIndex, after, afterActiveIndex,
                guidesChanged ? guidesBefore : null, guidesChanged ? guidesAfter : null), StepName(caller));
        }

        /// <summary>History panel name for a structural change, from the method that made it.</summary>
        /// <summary>Commands the Actions panel can record and play back by name
        /// (see <see cref="CommandRecorded"/>).</summary>
        public static readonly IReadOnlyList<string> RecordableCommands = new[]
        {
            nameof(AddLayer), nameof(DeleteLayer), nameof(DuplicateLayer), nameof(MergeDown), nameof(MergeVisible),
            nameof(FlattenImage), nameof(Rotate90Clockwise), nameof(Rotate90CounterClockwise), nameof(Rotate180),
            nameof(FlipCanvasHorizontal), nameof(FlipCanvasVertical), nameof(FlipLayerHorizontal), nameof(FlipLayerVertical),
        };

        /// <summary>Raised after a recordable command ran, with its name.</summary>
        public event Action<string>? CommandRecorded;

        private void Recorded([System.Runtime.CompilerServices.CallerMemberName] string command = "") =>
            CommandRecorded?.Invoke(command);

        /// <summary>Runs a recordable command by name (Actions playback).</summary>
        public bool RunRecordableCommand(string command)
        {
            if (!RecordableCommands.Contains(command)) return false;
            if (GetType().GetProperty(command + "Command")?.GetValue(this) is not System.Windows.Input.ICommand cmd || !cmd.CanExecute(null))
                return false;
            cmd.Execute(null);
            return true;
        }

        public static string TitleOf(string command) => command switch
        {
            nameof(AddLayer) => "New Layer",
            nameof(Rotate90Clockwise) => "Rotate Canvas 90° CW",
            nameof(Rotate90CounterClockwise) => "Rotate Canvas 90° CCW",
            nameof(Rotate180) => "Rotate Canvas 180°",
            _ => System.Text.RegularExpressions.Regex.Replace(command, "(?<=[a-z])(?=[A-Z])", " "),
        };

        private static string StepName(string caller) =>
            StepNameOf(caller.EndsWith("Core", StringComparison.Ordinal) ? caller[..^4] : caller);

        private static string StepNameOf(string caller) => caller switch
        {
            nameof(AddLayer) => "New Layer",
            nameof(AutoAlignLayers) => "Auto-Align Layers",
            nameof(MergeToHdrPro) => "Merge to HDR Pro",
            nameof(ResizeDocument) => "Canvas Size",
            nameof(ResampleDocument) => "Image Size",
            nameof(RotateDocument) => "Rotate Canvas",
            nameof(FlipDocument) => "Flip Canvas",
            nameof(CropDocument) => "Crop",
            nameof(PlaceSmartObject) => "Place Embedded",
            nameof(PasteAsNewLayer) => "Paste",
            nameof(AddMask) => "Add Layer Mask",
            nameof(ToggleClippingMask) => "Clipping Mask",
            nameof(AddTextLayer) => "Type Layer",
            nameof(RasterizeType) => "Rasterize Type",
            nameof(AddShapeLayer) => "Shape Layer",
            nameof(AddFillLayer) => "Fill Layer",
            nameof(RasterizeFill) => "Rasterize Fill",
            nameof(RasterizeShape) => "Rasterize Shape",
            nameof(RemoveMask) => "Delete Layer Mask",
            nameof(ConvertForSmartFilters) => "Convert for Smart Filters",
            nameof(AddMaskFromSelection) => "Add Layer Mask",
            nameof(RestoreSnapshot) => "Snapshot",
            nameof(ContentAwareScaleDocument) => "Content-Aware Scale",
            nameof(RasterizeSmartFilters) => "Rasterize Smart Filters",
            _ => System.Text.RegularExpressions.Regex.Replace(caller, "(?<=[a-z])(?=[A-Z])", " "),
        };

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
            var rows = PanelRows();
            if (Items.Select(item => (item.Model, item.Depth)).SequenceEqual(rows))
            {
                foreach (var item in Items) item.RefreshStyleState();
                ActiveLayer = Items.FirstOrDefault(item => item.Model == Scene.ActiveLayer) ?? Items.FirstOrDefault();
                return;
            }

            foreach (var item in Items)
            {
                item.VisualStateChanged -= OnLayerVisualStateChanged;
                item.ExpandedChanged -= OnLayerExpandedChanged;
            }

            Items.Clear();

            foreach (var (model, depth) in rows)
            {
                var itemViewModel = new LayerViewModel(model, depth);
                itemViewModel.VisualStateChanged += OnLayerVisualStateChanged;
                itemViewModel.ExpandedChanged += OnLayerExpandedChanged;
                Items.Add(itemViewModel);
            }

            ActiveLayer = Items.FirstOrDefault(item => item.Model == Scene.ActiveLayer) ?? Items.FirstOrDefault();
        }

        /// <summary>The Layers panel's rows, front to back: every layer except those
        /// inside a collapsed group, with how deep in groups each one is.</summary>
        private List<(Layer Model, int Depth)> PanelRows()
        {
            var rows = new List<(Layer, int)>();
            for (var i = Scene.Layers.Count - 1; i >= 0; i--)
            {
                var layer = Scene.Layers[i];
                var hidden = false;
                var depth = 0;
                for (var parent = Scene.ParentOf(layer); parent is not null && depth < 64; parent = Scene.ParentOf(parent))
                {
                    depth++;
                    if (!parent.IsExpanded) hidden = true;
                }
                if (!hidden) rows.Add((layer, depth));
            }
            return rows;
        }

        private void OnLayerVisualStateChanged(object? sender, EventArgs e)
        {
            History.MarkModified();
            RaiseInvalidate();
        }

        /// <summary>For edits that touch layers other than the active one outside of
        /// history (e.g. a live multi-layer move): repaint without the layer cache.</summary>
        public void NotifyContentChanged() => RaiseInvalidate();

        private void RaiseInvalidate()
        {
            ContentVersion++;
            InvalidateRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Bumped whenever something other than a live stroke on the active
        /// layer may have changed what the canvas shows (history, structure, a layer's
        /// visibility/opacity/blend). The renderer keeps the layers above and below the
        /// active one cached until this changes.</summary>
        public long ContentVersion { get; private set; }

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
