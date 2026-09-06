using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Clipboard;
using JolieCat.Core.Documents;
using JolieCat.Core.Export;
using JolieCat.Core.Serialization;
using JolieCat.Core.Tools;
using JolieCat.UI.ViewModels.Layers;
using JolieCat.UI.ViewModels.Timeline;
using JolieCat.UI.Views;
using Microsoft.Win32;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    /// <summary>
    /// View model for <see cref="MainWindow"/>: owns the visibility of the four docking
    /// zones (Left/Right/Bottom) around the central canvas, the open document tabs (see
    /// <see cref="DocumentViewModel"/>) and which one is active, and composes the
    /// panels' own view models. Also owns the things that cut across every panel and
    /// every document alike: the shared tool palette (<see cref="Toolbox"/>), undo/redo/
    /// copy/paste (each acting on whichever document is currently active), and
    /// whole-project Save/Open/Import/Export.
    /// </summary>
    /// <remarks>
    /// <see cref="Layers"/>/<see cref="Canvas"/>/<see cref="Timeline"/> are kept as
    /// forwarding properties resolving to <see cref="ActiveDocument"/>'s own instances,
    /// rather than every other view in this app (MainWindow.xaml, CanvasView.xaml, the
    /// Timeline/Properties views) being rewritten to bind through an extra
    /// "ActiveDocument." prefix - a well-established WPF "current item" facade pattern
    /// that keeps every existing binding path working unchanged as tabs are added,
    /// closed, and switched between.
    /// </remarks>
    public partial class MainViewModel : ObservableObject
    {
        /// <summary>The exact text the status bar shows once a save completes - a
        /// constant (rather than an inline literal) so MainWindow.xaml's fade-out
        /// trigger, which has to match it exactly to fire, references this instead of
        /// its own separately-typed copy (via <c>x:Static</c>).</summary>
        public const string SaveSuccessMessage = "✓ Project saved successfully";

        /// <summary>The status bar's success text for an image export - a separate
        /// constant from <see cref="SaveSuccessMessage"/> (rather than reusing it) since
        /// MainWindow.xaml's fade-out DataTrigger has to match the exact text it's
        /// looking for, and "project saved" would be a misleading thing to show after
        /// exporting a flattened image rather than the project file itself.</summary>
        public const string ExportSuccessMessage = "✓ Image exported successfully";

        /// <summary>How long <see cref="SaveSuccessMessage"/> stays up before this class
        /// clears it back to empty - matched by the fade-out Storyboard's own total
        /// duration in MainWindow.xaml, so the message is gone right as (not well before
        /// or after) the animation finishes.</summary>
        private static readonly TimeSpan SaveSuccessDisplayDuration = TimeSpan.FromSeconds(2.5);

        [ObservableProperty]
        private bool isLeftPanelVisible = true;

        [ObservableProperty]
        private bool isRightPanelVisible = true;

        [ObservableProperty]
        private bool isBottomPanelVisible = true;

        /// <summary>True for exactly the duration of an in-flight save - drives the
        /// full-workspace overlay (see MainWindow.xaml) that blocks further edits while
        /// <see cref="ProjectSerializer.SaveAsync"/> is writing the file, distinct from
        /// <see cref="SaveStatusMessage"/>, which keeps showing "saved successfully" for
        /// a little while after this already flips back to false. Also gates Undo/Redo/
        /// Paste's own CanExecute (see below): the overlay only blocks mouse input, but
        /// each of these can mutate or entirely replace a layer's SKBitmap synchronously
        /// on the UI thread, while SaveAsync is concurrently reading that same layer's
        /// pixels on a background thread - a real race, not just an unwanted concurrent
        /// edit, so these specifically need to be genuinely disabled (not merely blocked
        /// by the overlay) rather than just re-guarded like SaveProjectAsync/OpenProject/
        /// ImportImage's own early-return checks.</summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
        [NotifyCanExecuteChangedFor(nameof(RedoCommand))]
        [NotifyCanExecuteChangedFor(nameof(CopySelectionCommand))]
        [NotifyCanExecuteChangedFor(nameof(PasteCommand))]
        private bool isSaving;

        /// <summary>The bottom status bar's text: "Saving project..." while a save is
        /// in flight, <see cref="SaveSuccessMessage"/> for a few seconds after it
        /// completes, then empty again. Empty means the status bar shows nothing.</summary>
        [ObservableProperty]
        private string saveStatusMessage = string.Empty;

        /// <summary>Temporary diagnostic readout for the status bar's right side:
        /// the live document canvas size and the active layer's own bitmap size, so
        /// either can be read directly off the running app the instant a visual
        /// mismatch appears, instead of inferred from a screenshot. Every layer is
        /// always exactly the document's size and drawn from (0,0) - there is no
        /// per-layer offset anywhere in this codebase - so this always shows "@
        /// (0,0)" for that reason, not as a placeholder. Recomputed on every relevant
        /// change of whichever document is currently active (see <see cref="RegisterDocument"/>
        /// and <see cref="OnActiveDocumentChanged"/>).</summary>
        [ObservableProperty]
        private string dimensionDebugText = string.Empty;

        /// <summary>Every open document/tab.</summary>
        public ObservableCollection<DocumentViewModel> Documents { get; } = new();

        /// <summary>Nullable, even though the app never intentionally leaves no
        /// document active: a WPF Selector (the tab strip's ListBox, TwoWay-bound
        /// SelectedItem="{Binding ActiveDocument}") sets its bound property to null of
        /// its own accord the moment the item it currently has selected is removed
        /// from the bound collection - synchronously, as part of processing that
        /// collection change, before any of this class's own code gets a chance to
        /// react. <see cref="CloseDocument"/> reassigns <see cref="ActiveDocument"/>
        /// to the next tab *before* removing the closed one specifically to avoid ever
        /// triggering that transient-null window - but declaring this nullable (rather
        /// than asserting it can't happen) and giving every reader of it a safe
        /// fallback (see <see cref="Layers"/>/<see cref="Canvas"/>/<see cref="Timeline"/>)
        /// means a future change that reintroduces the same WPF timing quirk (a second
        /// TwoWay-bound selector, a different close/removal order) fails safe instead
        /// of throwing a NullReferenceException out of a property getter.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Layers))]
        [NotifyPropertyChangedFor(nameof(Canvas))]
        [NotifyPropertyChangedFor(nameof(Timeline))]
        [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
        [NotifyCanExecuteChangedFor(nameof(RedoCommand))]
        [NotifyCanExecuteChangedFor(nameof(CopySelectionCommand))]
        [NotifyCanExecuteChangedFor(nameof(PasteCommand))]
        private DocumentViewModel? activeDocument;

        /// <summary>A never-shown, lazily-created document (never added to
        /// <see cref="Documents"/>, never made active) that <see cref="Layers"/>/
        /// <see cref="Canvas"/>/<see cref="Timeline"/> fall back to for the rare/
        /// transient moment <see cref="ActiveDocument"/> is null - see its own remarks.
        /// Lets every existing binding and call site that reads Layers/Canvas/Timeline
        /// keep assuming a non-null instance rather than needing a null-conditional
        /// (or a crash) scattered across every one of them.</summary>
        private DocumentViewModel? _fallbackDocument;

        private DocumentViewModel FallbackDocument => _fallbackDocument ??= new DocumentViewModel(Toolbox, "(no document)");

        /// <summary>The active tool and its options - shared by every document tab
        /// rather than duplicated per document (see <see cref="DocumentViewModel"/>'s
        /// own remarks for why).</summary>
        public ToolboxViewModel Toolbox { get; }

        /// <summary>Forwards to <see cref="ActiveDocument"/>'s own <see cref="DocumentViewModel.Layers"/>
        /// (or <see cref="FallbackDocument"/>'s, on the rare/transient tick
        /// <see cref="ActiveDocument"/> is null - see its own remarks) - see this
        /// class's remarks for why every existing binding/call site can keep reading
        /// this property unchanged as the active document changes.</summary>
        public LayersViewModel Layers => (ActiveDocument ?? FallbackDocument).Layers;

        public CanvasViewModel Canvas => (ActiveDocument ?? FallbackDocument).Canvas;

        public TimelineViewModel Timeline => (ActiveDocument ?? FallbackDocument).Timeline;

        public MainViewModel()
        {
            Toolbox = new ToolboxViewModel();

            var firstDocument = new DocumentViewModel(Toolbox, "Untitled 1");
            RegisterDocument(firstDocument);
            Documents.Add(firstDocument);
            activeDocument = firstDocument;

            UpdateDimensionDebugText();
        }

        /// <summary>Wires up the per-document event subscriptions that keep the shared
        /// Undo/Redo state and the status bar's dimension readout live - once per
        /// document, for its whole lifetime, rather than unsubscribing/resubscribing on
        /// every tab switch. Each handler gates on whether <paramref name="document"/>
        /// is still the active one before touching any shared UI state, so a background
        /// tab's own edits (there aren't any right now - nothing runs on an inactive
        /// tab - but this keeps the guard correct if that ever changes) can never
        /// clobber what the active tab is showing.</summary>
        private void RegisterDocument(DocumentViewModel document)
        {
            document.Layers.History.Changed += (_, _) =>
            {
                if (!ReferenceEquals(document, ActiveDocument)) return;
                UndoCommand.NotifyCanExecuteChanged();
                RedoCommand.NotifyCanExecuteChanged();
            };

            // InvalidateRequested covers everything that can change either size
            // (structural changes, undo/redo, opening a project, importing an image);
            // PropertyChanged additionally covers just clicking a different layer in
            // the Layers panel active, which changes ActiveLayer without itself
            // requesting a repaint - otherwise the readout would keep showing the
            // previously active layer's name until something else invalidated.
            document.Layers.InvalidateRequested += (_, _) =>
            {
                if (ReferenceEquals(document, ActiveDocument)) UpdateDimensionDebugText();
            };
            document.Layers.PropertyChanged += (_, e) =>
            {
                if (ReferenceEquals(document, ActiveDocument) && e.PropertyName == nameof(LayersViewModel.ActiveLayer))
                    UpdateDimensionDebugText();
            };
        }

        /// <summary>Refreshes every piece of shared UI state that depends on which
        /// document is active - the status bar's dimension readout, and Undo/Redo/Copy/
        /// Paste's enabled state (each of which reads <see cref="Layers"/>, which just
        /// changed identity). Runs the same way even when <paramref name="value"/> is
        /// the transient null described on <see cref="ActiveDocument"/>'s own remarks -
        /// every one of these reads <see cref="Layers"/>, which already falls back
        /// safely rather than needing a null check here too.</summary>
        partial void OnActiveDocumentChanged(DocumentViewModel? value)
        {
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
            CopySelectionCommand.NotifyCanExecuteChanged();
            PasteCommand.NotifyCanExecuteChanged();
            UpdateDimensionDebugText();
        }

        /// <summary>Refreshes <see cref="DimensionDebugText"/> from the live scene -
        /// see that property's own remarks for why this exists and why
        /// <see cref="LayersViewModel.InvalidateRequested"/> is what drives it.</summary>
        private void UpdateDimensionDebugText()
        {
            var activeLayer = Layers.ActiveLayer?.Model;
            DimensionDebugText = activeLayer is null
                ? $"Canvas {Layers.DocumentWidth}x{Layers.DocumentHeight}"
                : $"Canvas {Layers.DocumentWidth}x{Layers.DocumentHeight}  |  Layer \"{activeLayer.Name}\" " +
                  $"{activeLayer.Bitmap.Width}x{activeLayer.Bitmap.Height} @ (0,0)";
        }

        [RelayCommand]
        private void ToggleLeftPanel() => IsLeftPanelVisible = !IsLeftPanelVisible;

        [RelayCommand]
        private void ToggleRightPanel() => IsRightPanelVisible = !IsRightPanelVisible;

        [RelayCommand]
        private void ToggleBottomPanel() => IsBottomPanelVisible = !IsBottomPanelVisible;

        /// <summary>
        /// Thin pass-through so tool selection can also be driven from MainViewModel
        /// (e.g. future keyboard shortcuts), in addition to the Tools panel's own list
        /// selection binding directly to <see cref="Toolbox"/>.
        /// </summary>
        [RelayCommand]
        private void SelectTool(ToolDefinition? tool) => Toolbox.SelectToolCommand.Execute(tool);

        /// <summary>Opens a brand new, empty document as its own tab and makes it
        /// active - the tab strip's "+"/File-New action.</summary>
        [RelayCommand]
        private void NewDocument()
        {
            var document = new DocumentViewModel(Toolbox, $"Untitled {Documents.Count + 1}");
            RegisterDocument(document);
            Documents.Add(document);
            ActiveDocument = document;
        }

        /// <summary>Closes a document tab - <paramref name="document"/> defaults to
        /// whichever is active when invoked from a keyboard shortcut rather than a
        /// specific tab's own close button. Always leaves at least one tab open: closing
        /// the last one opens a fresh blank document in its place instead, exactly like
        /// <see cref="NewDocument"/>, rather than leaving the workspace with nothing
        /// open (which nothing else in this app is set up to handle - every panel here
        /// assumes an active document always exists).</summary>
        /// <remarks>
        /// If <paramref name="document"/> (or the <see cref="ActiveDocument"/> it
        /// defaults to) is the active tab, <see cref="ActiveDocument"/> is reassigned to
        /// whatever tab should become active *before* <paramref name="document"/> is
        /// removed from <see cref="Documents"/> - deliberately in that order. The tab
        /// strip's ListBox has its SelectedItem TwoWay-bound to ActiveDocument; removing
        /// an item from a Selector's ItemsSource while that same item is still its
        /// SelectedItem makes WPF null out SelectedItem (and so, via the binding,
        /// ActiveDocument) itself, synchronously, as part of processing the removal -
        /// which happened before this method existed in this order and was exactly the
        /// source of a NullReferenceException out of Layers/Canvas/Timeline the instant
        /// that transient null landed. Reassigning ActiveDocument first means the
        /// SelectedItem has already moved off <paramref name="document"/> by the time
        /// it's removed, so WPF has nothing left to "helpfully" null out.
        /// </remarks>
        [RelayCommand]
        private void CloseDocument(DocumentViewModel? document)
        {
            document ??= ActiveDocument;
            if (document is null) return;

            var index = Documents.IndexOf(document);
            if (index < 0) return;

            if (ReferenceEquals(ActiveDocument, document))
            {
                if (Documents.Count == 1)
                {
                    var replacement = new DocumentViewModel(Toolbox, "Untitled 1");
                    RegisterDocument(replacement);
                    Documents.Add(replacement);
                    ActiveDocument = replacement;
                }
                else
                {
                    // Prefers the tab to the right (same index the closed one is
                    // about to vacate) if there is one, else falls back to the left -
                    // the same convention a browser's own tab strip uses.
                    ActiveDocument = Documents[index < Documents.Count - 1 ? index + 1 : index - 1];
                }
            }

            // A Smart Object "Edit Contents" tab's edits only take effect on its parent
            // instance once this tab closes - re-flatten its (possibly just-edited)
            // embedded scene back into the host layer's cached render now, before this
            // tab (and its CanvasViewModel) goes away. The host layer's Bitmap is
            // mutated in place, so the parent document's own tab shows the change the
            // moment it's next shown - switching back to it (which this method's own
            // ActiveDocument reassignment above may already have done) forces a fresh
            // repaint via CanvasView's own tab-switch handling, exactly like any other
            // cross-tab document change.
            if (document.SmartObjectHostLayer is { } hostLayer)
                hostLayer.RefreshSmartObjectContent(document.Layers.DocumentWidth, document.Layers.DocumentHeight);

            Documents.Remove(document);
            document.Dispose();
        }

        [RelayCommand(CanExecute = nameof(CanUndo))]
        private void Undo() => Layers.History.Undo();

        private bool CanUndo() => Layers.History.CanUndo && !IsSaving;

        [RelayCommand(CanExecute = nameof(CanRedo))]
        private void Redo() => Layers.History.Redo();

        private bool CanRedo() => Layers.History.CanRedo && !IsSaving;

        /// <summary>Copies the active layer's pixels within the current selection (the
        /// whole layer, if nothing is selected - matching every other paint operation's
        /// own "no selection means everywhere is fair game" convention, see
        /// <see cref="Selection.Contains"/>) into <see cref="PixelClipboard"/>, cropped
        /// to the selection's bounding box and clipped to its actual shape (not just
        /// that bounding rectangle) so a non-rectangular selection - an ellipse, a
        /// lasso - doesn't bring its corners along transparently.</summary>
        [RelayCommand(CanExecute = nameof(CanCopySelection))]
        private void CopySelection()
        {
            var activeLayer = Layers.ActiveLayer?.Model;
            if (activeLayer is null) return;

            var selection = Layers.Scene.Selection;
            var layerBounds = new SKRectI(0, 0, activeLayer.Bitmap.Width, activeLayer.Bitmap.Height);
            var bounds = selection.HasSelection && selection.Region is { } region
                ? SKRectI.Intersect(region.Bounds, layerBounds)
                : layerBounds;
            if (bounds.IsEmpty) return;

            var copy = new SKBitmap(bounds.Width, bounds.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(copy))
            {
                canvas.Clear(SKColors.Transparent);

                if (selection.HasSelection && selection.Path is { } path)
                {
                    using var translated = new SKPath(path);
                    translated.Transform(SKMatrix.CreateTranslation(-bounds.Left, -bounds.Top));
                    canvas.ClipPath(translated, antialias: true);
                }

                canvas.DrawBitmap(activeLayer.Bitmap, -bounds.Left, -bounds.Top);
            }

            PixelClipboard.SetContent(copy);
            PasteCommand.NotifyCanExecuteChanged();
        }

        private bool CanCopySelection() => !IsSaving && Layers.ActiveLayer is not null;

        /// <summary>Pastes <see cref="PixelClipboard"/>'s content as a new topmost layer
        /// (see <see cref="LayersViewModel.PasteAsNewLayer"/>) into whichever document
        /// is currently active - including a different one than it was copied from -
        /// centered on the document rather than requiring a remembered cursor position,
        /// so a paste always lands somewhere visible regardless of the current pan/zoom
        /// or which document it's landing in.</summary>
        [RelayCommand(CanExecute = nameof(CanPaste))]
        private void Paste()
        {
            if (PixelClipboard.Content is not { } clip) return;

            var x = (Layers.DocumentWidth - clip.Width) / 2;
            var y = (Layers.DocumentHeight - clip.Height) / 2;
            Layers.PasteAsNewLayer(clip, x, y);
        }

        private bool CanPaste() => !IsSaving && PixelClipboard.HasContent;

        /// <summary>Prompts for a destination and writes the active document - every
        /// layer's pixels and metadata, plus the timeline's tracks/clips/keyframes - as a
        /// single <c>.jolie</c> file (see <see cref="ProjectSerializer.SaveAsync"/>).
        /// <see cref="IsSaving"/> is true for exactly the write's duration - MainWindow's
        /// full-workspace overlay blocks further edits for that same span, so nothing
        /// touches the scene while it's being encoded/written - and the actual write runs
        /// on a background thread (SaveAsync), so awaiting it here never blocks the UI
        /// thread despite IsSaving keeping the UI locked meanwhile.</summary>
        [RelayCommand]
        private async Task SaveProjectAsync()
        {
            // The overlay only blocks *mouse* input (it's a hit-test-visible visual on
            // top of everything) - Ctrl+S is a Window-level KeyBinding, routed by
            // keyboard focus rather than hit-testing, so it would otherwise reach this
            // command again even while the overlay is up. Guard here so a second save
            // can never start concurrently with one already in flight, regardless of
            // which input triggered it.
            if (IsSaving) return;
            if (ActiveDocument is not { } activeDocument) return;

            var dialog = new SaveFileDialog
            {
                Filter = "JolieCat Project (*.jolie)|*.jolie",
                DefaultExt = ".jolie",
                FileName = $"{activeDocument.Title}.jolie",
            };
            if (dialog.ShowDialog() != true) return;

            IsSaving = true;
            SaveStatusMessage = "Saving project...";

            try
            {
                await ProjectSerializer.SaveAsync(dialog.FileName, activeDocument.Layers.Scene, BuildTimelineData(activeDocument), activeDocument.Timeline.TotalFrames, activeDocument.Timeline.FrameRate);

                activeDocument.Title = Path.GetFileNameWithoutExtension(dialog.FileName);
                activeDocument.FilePath = dialog.FileName;

                SaveStatusMessage = SaveSuccessMessage;
                _ = ClearSaveStatusAfterDelayAsync(SaveSuccessMessage);
            }
            catch (Exception ex)
            {
                SaveStatusMessage = string.Empty;
                MessageBox.Show($"Couldn't save the project:\n{ex.Message}", "Save Project", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsSaving = false;
            }
        }

        /// <summary>Clears <see cref="SaveStatusMessage"/> back to empty once
        /// <paramref name="expectedMessage"/> (<see cref="SaveSuccessMessage"/> or
        /// <see cref="ExportSuccessMessage"/>) has been up long enough to fade out on
        /// its own (see MainWindow.xaml's DataTriggers) - not awaited by the caller,
        /// since the save/export itself is already long finished by this point and
        /// nothing should block on a purely cosmetic timer. Clearing back to empty
        /// (rather than leaving the success text in place) also matters functionally: a
        /// DataTrigger only fires on a value *change*, so without this the very next
        /// save/export's success message wouldn't re-trigger the fade at all. Checked
        /// against <paramref name="expectedMessage"/> (not just non-empty) so a save's
        /// delayed clear can never stomp an export's success message that started
        /// showing afterward, or vice versa.</summary>
        private async Task ClearSaveStatusAfterDelayAsync(string expectedMessage)
        {
            await Task.Delay(SaveSuccessDisplayDuration);

            if (SaveStatusMessage == expectedMessage)
                SaveStatusMessage = string.Empty;
        }

        /// <summary>Prompts for a <c>.jolie</c> file and opens it as a brand new tab -
        /// rather than replacing the active document's content - so multiple projects
        /// can stay open simultaneously (see <see cref="DocumentViewModel"/>).</summary>
        [RelayCommand]
        private void OpenProject()
        {
            // Same reasoning as SaveProjectAsync's own guard: a keyboard shortcut isn't
            // stopped by the mouse-blocking saving overlay, so this needs to check for
            // itself that a save isn't still in flight before adding a document while
            // one might still be mid-write.
            if (IsSaving) return;

            var dialog = new OpenFileDialog { Filter = "JolieCat Project (*.jolie)|*.jolie" };
            if (dialog.ShowDialog() != true) return;

            try
            {
                var result = ProjectSerializer.Load(dialog.FileName);
                var name = Path.GetFileNameWithoutExtension(dialog.FileName);

                var document = new DocumentViewModel(Toolbox, name) { FilePath = dialog.FileName };
                RegisterDocument(document);
                document.Layers.LoadScene(result.Scene, name);
                document.Timeline.LoadTracks(result.TimelineTracks, result.TimelineTotalFrames, result.TimelineFrameRate);

                Documents.Add(document);
                ActiveDocument = document;
                Canvas.ResetView();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Couldn't open '{dialog.FileName}':\n{ex.Message}", "Open Project", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>Prompts for one or more image files (PNG/JPG/JPEG/BMP/WEBP) and
        /// imports each as its own new layer into the active document - the file-dialog
        /// counterpart to dropping images directly onto the canvas (see
        /// <see cref="CanvasViewModel.ImportImageFiles"/>, which both paths funnel
        /// through).</summary>
        [RelayCommand]
        private void ImportImage()
        {
            if (IsSaving) return;

            var dialog = new OpenFileDialog
            {
                Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.webp|All files (*.*)|*.*",
                Multiselect = true,
            };
            if (dialog.ShowDialog() != true) return;

            Canvas.ImportImageFiles(dialog.FileNames);
        }

        /// <summary>Prompts for a single image file and places it as a new Smart
        /// Object layer in the active document (see <see cref="LayersViewModel.PlaceSmartObject"/>) -
        /// non-destructive, unlike <see cref="ImportImage"/>'s plain raster layer:
        /// scaling or rotating it later via Free Transform always re-samples fresh
        /// from this original file instead of compounding quality loss.</summary>
        [RelayCommand]
        private void PlaceSmartObject()
        {
            if (IsSaving) return;

            var dialog = new OpenFileDialog
            {
                Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.webp|All files (*.*)|*.*",
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                using var bitmap = SKBitmap.Decode(dialog.FileName);
                if (bitmap is null)
                {
                    MessageBox.Show($"Couldn't read '{Path.GetFileName(dialog.FileName)}' as an image.", "Place as Smart Object",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Layers.PlaceSmartObject(bitmap, Path.GetFileNameWithoutExtension(dialog.FileName));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show($"Couldn't read '{Path.GetFileName(dialog.FileName)}':\n{ex.Message}", "Place as Smart Object",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>"Edit Contents": opens a Smart Object layer's embedded sub-
        /// composition as its own document tab - reusing the exact same multi-
        /// document-tab machinery every other open project already uses (see
        /// <see cref="DocumentViewModel"/>) - so its content can be painted,
        /// transformed, or have further layers added exactly like any other document.
        /// Reuses the already-open tab instead of opening a second one if this layer's
        /// contents are already being edited. Closing that tab re-flattens whatever
        /// changed back into <paramref name="layer"/>'s own cached render - see
        /// <see cref="CloseDocument"/>'s own remarks. A no-op for a layer with no
        /// embedded scene (not a Smart Object layer at all, or a placed-image Smart
        /// Object with nothing of its own to edit).</summary>
        [RelayCommand]
        private void EditSmartObjectContents(Layer? layer)
        {
            if (layer?.SmartObject?.EmbeddedScene is not { } embeddedScene) return;

            var existing = Documents.FirstOrDefault(d => ReferenceEquals(d.SmartObjectHostLayer, layer));
            if (existing is not null)
            {
                ActiveDocument = existing;
                return;
            }

            var document = new DocumentViewModel(Toolbox, layer.Name) { SmartObjectHostLayer = layer };
            RegisterDocument(document);
            document.Layers.LoadScene(embeddedScene, layer.Name);

            Documents.Add(document);
            ActiveDocument = document;
            Canvas.ResetView();
        }

        /// <summary>Exports the active document's flattened composite (or a single
        /// selected layer - see <see cref="ExportOptionsViewModel"/>) to a PNG/JPEG/WebP
        /// file at the exact document (or layer) pixel dimensions - no checkerboard, no
        /// selection overlay, no pan/zoom transform, so there is no top-edge clipping or
        /// coordinate offset for those to introduce (see <see cref="ImageExportService"/>).
        /// Flattening happens synchronously (it reads the live scene, so it can't safely
        /// run on a background thread while something else might mutate it), then the
        /// encode and file write - the actually slow part for a large image - run on a
        /// background thread via <see cref="ImageExportService.ExportAsync"/>, exactly
        /// like <see cref="SaveProjectAsync"/>'s own write.</summary>
        [RelayCommand]
        private async Task ExportImageAsync()
        {
            if (IsSaving) return;

            var options = new ExportOptionsViewModel(Layers.Scene);
            var dialog = new ExportDialog { DataContext = options, Owner = Application.Current.MainWindow };
            if (dialog.ShowDialog() != true) return;

            var saveDialog = new SaveFileDialog
            {
                Filter = options.Format switch
                {
                    ImageExportFormat.Png => "PNG Image (*.png)|*.png",
                    ImageExportFormat.Jpeg => "JPEG Image (*.jpg)|*.jpg",
                    ImageExportFormat.WebP => "WebP Image (*.webp)|*.webp",
                    _ => "All files (*.*)|*.*",
                },
                DefaultExt = options.FileExtension,
                FileName = $"Untitled{options.FileExtension}",
            };
            if (saveDialog.ShowDialog() != true) return;

            IsSaving = true;
            SaveStatusMessage = "Exporting image...";

            SKBitmap? flattened = null;
            try
            {
                flattened = options.SelectedTarget.Layer is { } layer
                    ? ImageExportService.FlattenLayer(layer)
                    : ImageExportService.FlattenScene(Layers.Scene, Layers.DocumentWidth, Layers.DocumentHeight);

                await ImageExportService.ExportAsync(saveDialog.FileName, flattened, options.Format, options.Quality);

                SaveStatusMessage = ExportSuccessMessage;
                _ = ClearSaveStatusAfterDelayAsync(ExportSuccessMessage);
            }
            catch (Exception ex)
            {
                SaveStatusMessage = string.Empty;
                MessageBox.Show($"Couldn't export the image:\n{ex.Message}", "Export Image", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                flattened?.Dispose();
                IsSaving = false;
            }
        }

        /// <summary>Opens the shared Filter dialog (Gaussian Blur/Box Blur/Sharpen/
        /// Noise - see <see cref="FilterOptionsViewModel"/>) for <paramref name="kind"/>,
        /// live-previewing every slider change on the canvas via
        /// <see cref="CanvasViewModel.BeginAdjustment"/>/<see cref="CanvasViewModel.UpdateAdjustmentPreview"/>,
        /// then bakes it in (Apply) or discards it (Cancel/closing the dialog any
        /// other way) - never both, and never neither: <see cref="CanvasViewModel.CommitAdjustment"/>/
        /// <see cref="CanvasViewModel.CancelAdjustment"/> run unconditionally based on
        /// <c>ShowDialog</c>'s own result, exactly once.</summary>
        [RelayCommand]
        private void OpenFilterDialog(FilterKind kind)
        {
            if (IsSaving || Layers.ActiveLayer is null) return;

            Canvas.BeginAdjustment();

            var options = new FilterOptionsViewModel(kind);
            void UpdatePreview(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
                Canvas.UpdateAdjustmentPreview(options.Apply);
            options.PropertyChanged += UpdatePreview;
            UpdatePreview(null, null!);

            var dialog = new FilterDialog { DataContext = options, Owner = Application.Current.MainWindow };
            var applied = dialog.ShowDialog() == true;
            options.PropertyChanged -= UpdatePreview;

            if (applied) Canvas.CommitAdjustment();
            else Canvas.CancelAdjustment();
        }

        /// <summary>Opens the shared Brightness/Contrast or Hue/Saturation/Lightness
        /// dialog (see <see cref="SimpleAdjustmentViewModel"/>) - same live-preview/
        /// commit-or-cancel pattern as <see cref="OpenFilterDialog"/>.</summary>
        [RelayCommand]
        private void OpenSimpleAdjustmentDialog(SimpleAdjustmentKind kind)
        {
            if (IsSaving || Layers.ActiveLayer is null) return;

            Canvas.BeginAdjustment();

            var options = new SimpleAdjustmentViewModel(kind);
            void UpdatePreview(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
                Canvas.UpdateAdjustmentPreview(options.Apply);
            options.PropertyChanged += UpdatePreview;
            UpdatePreview(null, null!);

            var dialog = new SimpleAdjustmentDialog { DataContext = options, Owner = Application.Current.MainWindow };
            var applied = dialog.ShowDialog() == true;
            options.PropertyChanged -= UpdatePreview;

            if (applied) Canvas.CommitAdjustment();
            else Canvas.CancelAdjustment();
        }

        /// <summary>Opens Levels (see <see cref="LevelsViewModel"/>) - same live-
        /// preview/commit-or-cancel pattern, with the histogram built once (from
        /// <see cref="CanvasViewModel.AdjustmentSourceBitmap"/>, the layer's own
        /// snapshotted pixels) right after <see cref="CanvasViewModel.BeginAdjustment"/>
        /// runs, so it always reflects the layer's real content, not an empty/
        /// default bitmap.</summary>
        [RelayCommand]
        private void OpenLevelsDialog()
        {
            if (IsSaving || Layers.ActiveLayer is null) return;

            Canvas.BeginAdjustment();
            if (Canvas.AdjustmentSourceBitmap is not { } source) { Canvas.CancelAdjustment(); return; }

            var options = new LevelsViewModel(source);
            void UpdatePreview(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
                Canvas.UpdateAdjustmentPreview(options.Apply);
            options.PropertyChanged += UpdatePreview;
            UpdatePreview(null, null!);

            var dialog = new LevelsDialog { DataContext = options, Owner = Application.Current.MainWindow };
            var applied = dialog.ShowDialog() == true;
            options.PropertyChanged -= UpdatePreview;

            if (applied) Canvas.CommitAdjustment();
            else Canvas.CancelAdjustment();
        }

        /// <summary>Opens Curves (see <see cref="CurvesViewModel"/>) - same live-
        /// preview/commit-or-cancel pattern; the control-point collection's own
        /// changes (not a single settable property) drive the preview here.</summary>
        [RelayCommand]
        private void OpenCurvesDialog()
        {
            if (IsSaving || Layers.ActiveLayer is null) return;

            Canvas.BeginAdjustment();

            var options = new CurvesViewModel();
            void UpdatePreview(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
                Canvas.UpdateAdjustmentPreview(options.Apply);
            options.PropertyChanged += UpdatePreview;
            UpdatePreview(null, null!);

            var dialog = new CurvesDialog { DataContext = options, Owner = Application.Current.MainWindow };
            var applied = dialog.ShowDialog() == true;
            options.PropertyChanged -= UpdatePreview;

            if (applied) Canvas.CommitAdjustment();
            else Canvas.CancelAdjustment();
        }

        /// <summary>Maps a document's Timeline panel view models to the plain,
        /// UI-agnostic data <see cref="ProjectSerializer"/> actually writes - this
        /// mapping (not the Core serializer) is what keeps <c>JolieCat.Core</c> from
        /// needing to reference <c>JolieCat.UI</c>'s timeline view models at all.</summary>
        private static TimelineTrackData[] BuildTimelineData(DocumentViewModel document) => document.Timeline.Tracks.Select(track => new TimelineTrackData
        {
            Name = track.Name,
            Clips = track.Clips.Select(clip => new TimelineClipData
            {
                Name = clip.Name,
                StartFrame = clip.StartFrame,
                LengthFrames = clip.LengthFrames,
            }).ToList(),
            KeyframeFrames = track.Keyframes.Select(keyframe => keyframe.Frame).ToList(),
        }).ToArray();
    }
}
