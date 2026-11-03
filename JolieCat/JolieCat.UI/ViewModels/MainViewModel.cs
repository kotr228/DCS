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
using JolieCat.Shared.Enums;
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

            // Every document remembers how it looked when opened (History Brush source).
            Documents.CollectionChanged += (_, e) =>
            {
                if (e.NewItems is null) return;
                foreach (DocumentViewModel document in e.NewItems) document.Layers.CaptureOpeningState();
            };

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
            document.Layers.CommandRecorded += command =>
            {
                if (ReferenceEquals(document, ActiveDocument)) OnLayersCommandRecorded(command);
            };

            document.Layers.History.Changed += (_, _) =>
            {
                if (!ReferenceEquals(document, ActiveDocument)) return;
                UndoCommand.NotifyCanExecuteChanged();
                RedoCommand.NotifyCanExecuteChanged();
                RefreshHistorySteps();
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
            RefreshHistorySteps();
        }

        /// <summary>Refreshes <see cref="DimensionDebugText"/> from the live scene -
        /// see that property's own remarks for why this exists and why
        /// <see cref="LayersViewModel.InvalidateRequested"/> is what drives it.</summary>
        private void UpdateDimensionDebugText()
        {
            // Runs whenever the active document, active layer or layer stack changes -
            // the same moments the active-layer-dependent commands need re-checking.
            EditAdjustmentLayerCommand.NotifyCanExecuteChanged();
            NotifyLayerStyleCommands();

            var activeLayer = Layers.ActiveLayer?.Model;
            DimensionDebugText = activeLayer is null
                ? $"{Layers.DocumentWidth} × {Layers.DocumentHeight} px"
                : $"{Layers.DocumentWidth} × {Layers.DocumentHeight} px  ·  {activeLayer.Name}";
        }

        [RelayCommand]
        private void ToggleLeftPanel() => IsLeftPanelVisible = !IsLeftPanelVisible;

        [RelayCommand]
        private void ToggleRightPanel() => IsRightPanelVisible = !IsRightPanelVisible;

        /// <summary>
        /// Thin pass-through so tool selection can also be driven from MainViewModel
        /// (e.g. future keyboard shortcuts), in addition to the Tools panel's own list
        /// selection binding directly to <see cref="Toolbox"/>.
        /// </summary>
        [RelayCommand]
        private void SelectTool(ToolDefinition? tool) => Toolbox.SelectToolCommand.Execute(tool);

        /// <summary>Prompts for one of the three project types (see
        /// <see cref="NewProjectDialog"/>/<see cref="ProjectType"/>), then opens a
        /// brand new, empty document of that type as its own tab and makes it active -
        /// the tab strip's "+"/File-New action. Cancelling the dialog opens nothing.</summary>
        [RelayCommand]
        private void NewDocument()
        {
            var dialog = new NewProjectDialog { Owner = Application.Current.MainWindow };
            if (dialog.ShowDialog() != true) return;

            var document = new DocumentViewModel(Toolbox, $"Untitled {Documents.Count + 1}", dialog.SelectedProjectType,
                dialog.DocumentWidth, dialog.DocumentHeight, dialog.BackgroundFill);
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
        private async Task CloseDocument(DocumentViewModel? document)
        {
            document ??= ActiveDocument;
            if (document is null || IsSaving) return;
            if (!await ConfirmCloseAsync(document)) return;
            Recovery.Delete(document);

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
            {
                hostLayer.RefreshSmartObjectContent(document.Layers.DocumentWidth, document.Layers.DocumentHeight);
                foreach (var parent in Documents)
                    if (parent.Layers.Scene.Layers.Contains(hostLayer))
                        parent.Layers.RequestRepaint();
            }

            Documents.Remove(document);
            document.Dispose();
        }

        [RelayCommand(CanExecute = nameof(CanUndo))]
        private void Undo()
        {
            Canvas.FlushTextRestyle();
            Layers.History.Undo();
        }

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
            _clipboardOrigin = new SKPointI(bounds.Left, bounds.Top);
            PasteCommand.NotifyCanExecuteChanged();
            PasteInPlaceCommand.NotifyCanExecuteChanged();
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
        /// <summary>Ctrl+S: saves the active document to its .jolie file, asking for a
        /// location only the first time (or when it came from a PSD/image).</summary>
        [RelayCommand]
        private async Task SaveProjectAsync()
        {
            if (ActiveDocument is { } document) await SaveDocumentAsync(document, forceDialog: false);
        }

        /// <summary>Ctrl+Shift+S: always asks where to save.</summary>
        [RelayCommand]
        private async Task SaveProjectAsAsync()
        {
            if (ActiveDocument is { } document) await SaveDocumentAsync(document, forceDialog: true);
        }

        /// <summary>
        /// Writes <paramref name="document"/> - every layer's pixels and metadata, plus
        /// the timeline - as a single .jolie file. <see cref="IsSaving"/> is true for
        /// exactly the write's duration: MainWindow's overlay blocks edits meanwhile, and
        /// the write itself runs on a background thread. Returns true if it was saved
        /// (false if cancelled, failed, or another save is still running).
        /// </summary>
        public async Task<bool> SaveDocumentAsync(DocumentViewModel document, bool forceDialog)
        {
            // Ctrl+S is a keyboard binding the mouse-blocking overlay doesn't stop, so a
            // second save must never start while one is in flight.
            if (IsSaving) return false;

            var path = document.FilePath;
            var hasProjectPath = path is not null && string.Equals(Path.GetExtension(path), ".jolie", StringComparison.OrdinalIgnoreCase);
            if (forceDialog || !hasProjectPath)
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "JolieCat Project (*.jolie)|*.jolie",
                    DefaultExt = ".jolie",
                    FileName = $"{document.Title}.jolie",
                };
                if (dialog.ShowDialog() != true) return false;
                path = dialog.FileName;
            }

            IsSaving = true;
            SaveStatusMessage = "Saving project...";

            try
            {
                await ProjectSerializer.SaveAsync(path!, document.Layers.Scene, BuildTimelineData(document), document.Timeline.TotalFrames, document.Timeline.FrameRate,
                    document.ProjectType, document.Layers.SpriteSheetGrid);

                document.Title = Path.GetFileNameWithoutExtension(path!);
                document.FilePath = path;
                document.Layers.History.MarkSaved();
                RememberRecent(path!);
                Recovery.Delete(document);

                SaveStatusMessage = SaveSuccessMessage;
                _ = ClearSaveStatusAfterDelayAsync(SaveSuccessMessage);
                return true;
            }
            catch (Exception ex)
            {
                SaveStatusMessage = string.Empty;
                MessageBox.Show($"Couldn't save the project:\n{ex.Message}", "Save Project", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            finally
            {
                IsSaving = false;
            }
        }

        /// <summary>Asks what to do with unsaved changes before <paramref name="document"/>
        /// closes. Returns true if it may close (saved, or changes discarded).</summary>
        public async Task<bool> ConfirmCloseAsync(DocumentViewModel document)
        {
            if (!document.IsDirty || document.SmartObjectHostLayer is not null) return true;

            ActiveDocument = document;
            var answer = MessageBox.Show(Application.Current.MainWindow!,
                $"Save changes to \"{document.Title}\" before closing?",
                "JolieCat", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

            return answer switch
            {
                MessageBoxResult.Yes => await SaveDocumentAsync(document, forceDialog: false),
                MessageBoxResult.No => true,
                _ => false,
            };
        }

        /// <summary>Asks about every unsaved document in turn. False if the user
        /// cancelled at any point (the app should then stay open).</summary>
        public async Task<bool> ConfirmCloseAllAsync()
        {
            foreach (var document in Documents.ToList())
                if (!await ConfirmCloseAsync(document)) return false;
            return true;
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

            var dialog = new OpenFileDialog
            {
                Filter = "All supported|*.jolie;*.psd;*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif" +
                         "|JolieCat Project (*.jolie)|*.jolie|Photoshop Document (*.psd)|*.psd" +
                         "|Images (*.png;*.jpg;*.webp;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif",
            };
            if (dialog.ShowDialog() != true) return;

            OpenPath(dialog.FileName);
        }

        /// <summary>The File &gt; Open Recent list, most recent first.</summary>
        public ObservableCollection<string> RecentFiles { get; } = new(Media.RecentFilesStore.Load());

        public bool HasRecentFiles => RecentFiles.Count > 0;

        [RelayCommand]
        private void OpenRecent(string? path)
        {
            if (IsSaving || string.IsNullOrEmpty(path)) return;

            if (!File.Exists(path))
            {
                MessageBox.Show($"'{path}' no longer exists.", "Open Recent", MessageBoxButton.OK, MessageBoxImage.Warning);
                RecentFiles.Remove(path);
                Media.RecentFilesStore.Save(RecentFiles);
                OnPropertyChanged(nameof(HasRecentFiles));
                return;
            }

            OpenPath(path);
        }

        [RelayCommand]
        private void ClearRecentFiles()
        {
            RecentFiles.Clear();
            Media.RecentFilesStore.Save(RecentFiles);
            OnPropertyChanged(nameof(HasRecentFiles));
        }

        private void RememberRecent(string path)
        {
            var updated = Media.RecentFilesStore.Promote(RecentFiles, path);
            RecentFiles.Clear();
            foreach (var p in updated) RecentFiles.Add(p);
            Media.RecentFilesStore.Save(RecentFiles);
            OnPropertyChanged(nameof(HasRecentFiles));
        }

        /// <summary>Opens a .jolie project, a .psd, or a plain image as a new tab.</summary>
        public void OpenPath(string path)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".psd")
            {
                OpenPsd(path);
                return;
            }

            if (extension != ".jolie")
            {
                OpenImage(path);
                return;
            }

            try
            {
                var result = ProjectSerializer.Load(path);
                var name = Path.GetFileNameWithoutExtension(path);

                var document = new DocumentViewModel(Toolbox, name, result.ProjectType) { FilePath = path };
                RegisterDocument(document);
                document.Layers.LoadScene(result.Scene, name, result.ProjectType, result.SpriteSheetGrid);
                document.Timeline.LoadTracks(result.TimelineTracks, result.TimelineTotalFrames, result.TimelineFrameRate);

                // A Clipbar Animation project derived via DeriveClipbarFromSpriteSheet
                // has its clips reference their own frame layers by name, not by any
                // serialized identifier (TimelineClipData carries no layer reference at
                // all) - reopening a saved one needs this same by-name rewiring to
                // restore that flipbook behavior. Harmless to call unconditionally
                // (every clip on any other project type simply matches no layer), but
                // gated here anyway since only this project type ever needs it.
                if (result.ProjectType == ProjectType.ClipbarAnimation)
                    document.Timeline.RewireFrameLayers(result.Scene);

                Documents.Add(document);
                ActiveDocument = document;
                Canvas.ResetView();
                RememberRecent(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Couldn't open '{path}':\n{ex.Message}", "Open Project", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>Opens a PNG/JPEG/WebP/BMP/GIF as a new one-layer document of the
        /// image's size. Saving it later asks where to put the .jolie project.</summary>
        private void OpenImage(string path)
        {
            try
            {
                using var decoded = SKBitmap.Decode(path)
                    ?? throw new InvalidDataException("The file isn't an image JolieCat can read.");
                var name = Path.GetFileNameWithoutExtension(path);

                var document = new DocumentViewModel(Toolbox, name, ProjectType.StandardImage, decoded.Width, decoded.Height, null);
                RegisterDocument(document);
                document.Layers.Scene.Layers[0].Canvas.DrawBitmap(decoded, 0, 0);
                document.Layers.RequestRepaint();

                Documents.Add(document);
                ActiveDocument = document;
                Canvas.ResetView();
                RememberRecent(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Couldn't open '{Path.GetFileName(path)}':\n{ex.Message}", "Open", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>Opens a Photoshop file as a new tab, keeping its layers, names,
        /// opacity, blend modes, visibility and masks (see <see cref="Core.Formats.PsdCodec"/>).</summary>
        private void OpenPsd(string path)
        {
            try
            {
                var psd = Core.Formats.PsdCodec.Read(path);
                var name = Path.GetFileNameWithoutExtension(path);

                var document = new DocumentViewModel(Toolbox, name);
                RegisterDocument(document);
                document.Layers.LoadScene(psd.Scene, name);

                Documents.Add(document);
                ActiveDocument = document;
                Canvas.ResetView();
                RememberRecent(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Couldn't open '{Path.GetFileName(path)}':\n{ex.Message}", "Open PSD", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>Writes the active document as a layered Photoshop file. The file
        /// write runs on a background thread under the same <see cref="IsSaving"/> lock
        /// a project save uses, so nothing edits the layers while they're being read.</summary>
        [RelayCommand]
        private async Task ExportPsdAsync()
        {
            if (IsSaving) return;
            if (ActiveDocument is not { } activeDocument) return;

            var dialog = new SaveFileDialog
            {
                Filter = "Photoshop Document (*.psd)|*.psd",
                DefaultExt = ".psd",
                FileName = $"{activeDocument.Title}.psd",
            };
            if (dialog.ShowDialog() != true) return;

            IsSaving = true;
            SaveStatusMessage = "Exporting PSD...";

            try
            {
                var layers = activeDocument.Layers;
                await Task.Run(() => Core.Formats.PsdCodec.Write(dialog.FileName, layers.Scene, layers.DocumentWidth, layers.DocumentHeight));

                SaveStatusMessage = ExportSuccessMessage;
                _ = ClearSaveStatusAfterDelayAsync(ExportSuccessMessage);
            }
            catch (Exception ex)
            {
                SaveStatusMessage = string.Empty;
                MessageBox.Show($"Couldn't export the PSD:\n{ex.Message}", "Export PSD", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsSaving = false;
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

        /// <summary>Switches the active document's own workspace content between the
        /// plain canvas and the dedicated Timeline workspace (see
        /// <see cref="DocumentViewModel.WorkspaceMode"/>) - MainWindow.xaml only shows
        /// the switch itself for a <see cref="ProjectType.ClipbarAnimation"/> project,
        /// but this applies unconditionally like every other simple setter-command in
        /// this class, since there is nothing unsafe about a document of any other type
        /// having a WorkspaceMode that just happens to never be shown.</summary>
        [RelayCommand]
        private void SetWorkspaceMode(WorkspaceMode mode)
        {
            if (ActiveDocument is { } document) document.WorkspaceMode = mode;
        }

        /// <summary>"Slice &amp; Export Frames": prompts for a destination folder and
        /// writes one PNG per cell of the active document's Sprite Sheet grid (see
        /// <see cref="LayersViewModel.SpriteSheetGrid"/>/<see cref="ImageExportService.ExportSpriteSheetCellsAsync"/>),
        /// named after the active document's own tab title. A no-op if the active
        /// document isn't a <see cref="ProjectType.SpriteSheet"/> project at all - the
        /// Sprite Sheet panel that exposes this command already only shows for one, but
        /// this guards it independently in case that ever changes.</summary>
        [RelayCommand]
        private async Task SliceSpriteSheetAsync()
        {
            if (IsSaving) return;
            if (Layers.ProjectType != ProjectType.SpriteSheet) return;

            var folderDialog = new OpenFolderDialog { Title = "Choose a folder for the sliced frames" };
            if (folderDialog.ShowDialog() != true) return;

            IsSaving = true;
            SaveStatusMessage = "Slicing Sprite Sheet frames...";

            try
            {
                var baseName = ActiveDocument?.Title ?? "frame";
                var written = await ImageExportService.ExportSpriteSheetCellsAsync(
                    Layers.Scene, Layers.DocumentWidth, Layers.DocumentHeight, Layers.SpriteSheetGrid,
                    folderDialog.FolderName, baseName, ImageExportFormat.Png, 100);

                SaveStatusMessage = $"✓ Exported {written.Count} frame(s)";
                _ = ClearSaveStatusAfterDelayAsync(SaveStatusMessage);
            }
            catch (Exception ex)
            {
                SaveStatusMessage = string.Empty;
                MessageBox.Show($"Couldn't slice the Sprite Sheet:\n{ex.Message}", "Slice Sprite Sheet", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsSaving = false;
            }
        }

        /// <summary>"Resize Canvas...": prompts for a new width/height (with optional
        /// aspect-ratio locking - see <see cref="ResizeCanvasViewModel"/>) and applies
        /// it via <see cref="LayersViewModel.ResizeDocument"/>, which already resizes
        /// every layer's own bitmap and mask anchored at their shared top-left corner -
        /// cropped if shrinking, padded with transparency if growing, with no
        /// coordinate shift on either. Recorded as one undo/redo entry like any other
        /// structural change.</summary>
        [RelayCommand]
        private void ResizeCanvas()
        {
            if (IsSaving) return;

            var options = new ResizeCanvasViewModel(Layers.DocumentWidth, Layers.DocumentHeight);
            var dialog = new ResizeCanvasDialog { DataContext = options, Owner = Application.Current.MainWindow };
            if (dialog.ShowDialog() != true) return;

            Layers.ResizeDocument((int)Math.Round(options.Width), (int)Math.Round(options.Height), options.AnchorCenter);
        }

        /// <summary>Select &gt; Modify &gt; Expand/Contract/Border/Smooth: asks for the
        /// amount in pixels, then reshapes the current selection.</summary>
        [RelayCommand]
        private void ModifySelection(SelectionModifyKind kind)
        {
            if (!Canvas.HasSelection) return;

            var title = kind switch
            {
                SelectionModifyKind.Expand => "Expand Selection",
                SelectionModifyKind.Contract => "Contract Selection",
                SelectionModifyKind.Border => "Border Selection",
                SelectionModifyKind.Feather => "Feather Selection",
                _ => "Smooth Selection",
            };
            var label = kind switch
            {
                SelectionModifyKind.Expand => "Expand by",
                SelectionModifyKind.Contract => "Contract by",
                SelectionModifyKind.Border => "Width",
                SelectionModifyKind.Feather => "Feather radius",
                _ => "Sample radius",
            };

            var dialog = new NumberPromptDialog(title, label, _lastModifyAmount, 1, 500, "px") { Owner = Application.Current.MainWindow };
            if (dialog.ShowDialog() != true) return;

            _lastModifyAmount = dialog.Value;
            Canvas.ModifySelection(kind, (float)dialog.Value);
        }

        private double _lastModifyAmount = 5;

        /// <summary>Edit > Define Pattern: the visible image inside the selection (or the
        /// whole image) becomes a pattern for Layer Style > Pattern Overlay.</summary>
        [RelayCommand]
        private void DefinePattern()
        {
            var name = DefinePatternFromDocument();
            if (name is null) return;
            MessageBox.Show($"Saved as \"{name}\".\n\nUse it in Layer Style > Pattern Overlay.", "Define Pattern",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>Edit > Manage Patterns: rename or delete the user's patterns.</summary>
        [RelayCommand]
        private void ManagePatterns()
        {
            var manager = new PatternManagerViewModel();
            manager.Renamed += RenamePatternInStyles;
            new PatternManagerDialog { DataContext = manager, Owner = Application.Current.MainWindow }.ShowDialog();
            manager.Renamed -= RenamePatternInStyles;
        }

        /// <summary>Layer styles in every open document follow a renamed pattern.</summary>
        internal void RenamePatternInStyles(string oldName, string newName)
        {
            foreach (var document in Documents)
            {
                var changed = false;
                foreach (var layer in document.Layers.Scene.Layers)
                    if (layer.Style is { PatternOverlay: true } style && style.PatternName == oldName)
                    {
                        layer.Style = style with { PatternName = newName };
                        changed = true;
                    }
                if (changed) document.Layers.NotifyContentChanged();
            }
        }

        /// <summary>Does the work of <see cref="DefinePattern"/>; returns the new pattern's
        /// name, or null if the selection is empty.</summary>
        internal string? DefinePatternFromDocument()
        {
            int width = Layers.DocumentWidth, height = Layers.DocumentHeight;
            var area = new SKRectI(0, 0, width, height);
            var selection = Layers.Scene.Selection;
            if (selection.HasSelection && selection.Path is { } path)
            {
                var b = path.Bounds;
                area = SKRectI.Intersect(area, new SKRectI((int)Math.Floor(b.Left), (int)Math.Floor(b.Top), (int)Math.Ceiling(b.Right), (int)Math.Ceiling(b.Bottom)));
            }
            if (area.Width <= 0 || area.Height <= 0) return null;

            using var flat = Core.Rendering.SceneCompositor.Flatten(Layers.Scene, width, height);
            using var piece = new SKBitmap(area.Width, area.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(piece))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(flat, -area.Left, -area.Top);
            }

            var name = Media.PatternLibrary.NextName();
            Media.PatternLibrary.Define(name, piece);
            return name;
        }

        /// <summary>View > New Guide: a guide at a typed position.</summary>
        [RelayCommand]
        private void NewGuide(string? orientation)
        {
            var vertical = orientation != "Horizontal";
            var max = vertical ? Layers.DocumentWidth : Layers.DocumentHeight;
            var dialog = new NumberPromptDialog(vertical ? "New Vertical Guide" : "New Horizontal Guide", "Position", max / 2.0, 0, max, "px")
            {
                Owner = Application.Current.MainWindow,
            };
            if (dialog.ShowDialog() != true) return;
            Canvas.AddGuide(vertical, (float)dialog.Value);
        }

        /// <summary>View > Grid Size: spacing of the grid lines.</summary>
        [RelayCommand]
        private void GridSize()
        {
            var dialog = new NumberPromptDialog("Grid", "Grid line every", Canvas.GridSize, 2, 2000, "px") { Owner = Application.Current.MainWindow };
            if (dialog.ShowDialog() != true) return;
            Canvas.GridSize = (int)Math.Round(dialog.Value);
            Canvas.ShowGrid = true;
        }

        private double _lastColorRangeFuzziness = 40;

        /// <summary>Select &gt; Color Range: selects everything close to the foreground
        /// color (pick it with the Eyedropper first), across the whole visible image.</summary>
        [RelayCommand]
        private void SelectColorRange()
        {
            var dialog = new NumberPromptDialog("Color Range", "Fuzziness", _lastColorRangeFuzziness, 0, 255, "")
            {
                Owner = Application.Current.MainWindow,
            };
            if (dialog.ShowDialog() != true) return;

            _lastColorRangeFuzziness = dialog.Value;
            Canvas.SelectColorRange(Canvas.BrushColor, (float)dialog.Value);
        }

        /// <summary>Image &gt; Image Size: rescales every layer to a new pixel size.</summary>
        [RelayCommand]
        private void ImageSize()
        {
            if (IsSaving) return;

            var options = new ResizeCanvasViewModel(Layers.DocumentWidth, Layers.DocumentHeight, resample: true);
            var dialog = new ResizeCanvasDialog { DataContext = options, Owner = Application.Current.MainWindow };
            if (dialog.ShowDialog() != true) return;

            int oldWidth = Layers.DocumentWidth, oldHeight = Layers.DocumentHeight;
            int width = (int)Math.Round(options.Width), height = (int)Math.Round(options.Height);
            Layers.ResampleDocument(width, height);
            RecordImageSize(width, height, oldWidth, oldHeight);
        }

        /// <summary>Type 2 -&gt; Type 3 derivation: builds a brand new Clipbar Animation
        /// document directly from the active Sprite Sheet project's current grid and
        /// content (see <see cref="SpriteSheetToClipbarConverter"/>) - each grid cell
        /// becomes its own frame layer plus a matching Timeline clip, the "foundational
        /// track assets" the new document's dedicated Timeline workspace opens
        /// straight into (see <see cref="DocumentViewModel"/>'s own ClipbarAnimation
        /// default). Opens the result as its own new tab, active. A no-op if the
        /// active document isn't a <see cref="ProjectType.SpriteSheet"/> project at all -
        /// the Sprite Sheet panel that exposes this command already only shows for
        /// one, but this guards it independently in case that ever changes.</summary>
        [RelayCommand]
        private void DeriveClipbarFromSpriteSheet()
        {
            if (IsSaving) return;
            if (Layers.ProjectType != ProjectType.SpriteSheet) return;

            var sourceTitle = ActiveDocument?.Title ?? "Sprite Sheet";

            // A defensive copy, not the source document's own live grid instance - the
            // new document's Document.SpriteSheetGrid is kept purely for provenance
            // (it's never shown or edited for a ClipbarAnimation project), and sharing
            // the actual instance would let a later edit to the *source* Sprite Sheet's
            // own grid silently reach into this already-created document too.
            var grid = Layers.SpriteSheetGrid;
            var gridSnapshot = new SpriteSheetGrid
            {
                Columns = grid.Columns,
                Rows = grid.Rows,
                PaddingX = grid.PaddingX,
                PaddingY = grid.PaddingY,
                MarginX = grid.MarginX,
                MarginY = grid.MarginY,
            };

            var result = SpriteSheetToClipbarConverter.Convert(Layers.Scene, Layers.DocumentWidth, Layers.DocumentHeight, gridSnapshot, Timeline.FrameRate);

            var document = new DocumentViewModel(Toolbox, $"{sourceTitle} (Clipbar)", ProjectType.ClipbarAnimation);
            RegisterDocument(document);
            document.Layers.LoadScene(result.Scene, document.Title, ProjectType.ClipbarAnimation, gridSnapshot);
            document.Timeline.LoadTracks(result.TimelineTracks, result.TotalFrames, result.FrameRate);
            document.Timeline.RewireFrameLayers(result.Scene);

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
            if (IsSaving || Layers.ActiveLayer is null || Layers.ActiveLayer.Model.IsGroup) return;

            Canvas.BeginAdjustment(FilterOptionsViewModel.TitleOf(kind));

            var options = new FilterOptionsViewModel(kind);
            if (kind == FilterKind.Clouds)
            {
                // Clouds are drawn from the foreground to the background color.
                static double Pack(System.Windows.Media.Color c) => (c.R << 16) | (c.G << 8) | c.B;
                options.Amount = Pack(Canvas.PrimaryColor);
                options.SecondAmount = Pack(Canvas.SecondaryColor);
            }

            // Filters with no settings (Invert, Desaturate, Find Edges) apply right away.
            if (!options.HasSettings)
            {
                Canvas.UpdateAdjustmentPreview(options.Apply);
                CommitFilter(StepForFilter(options));
                return;
            }

            void UpdatePreview(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
                Canvas.UpdateAdjustmentPreview(options.Apply);
            options.PropertyChanged += UpdatePreview;
            UpdatePreview(null, null!);

            var dialog = new FilterDialog { DataContext = options, Owner = Application.Current.MainWindow };
            var applied = dialog.ShowDialog() == true;
            options.PropertyChanged -= UpdatePreview;

            if (applied)
            {
                CommitFilter(StepForFilter(options));
            }
            else
            {
                Canvas.CancelAdjustment();
            }
        }

        /// <summary>Filter > Blur Gallery > Tilt-Shift / Iris Blur, with a live canvas preview.</summary>
        [RelayCommand]
        private void OpenBlurGallery(string? kind)
        {
            if (IsSaving || Layers.ActiveLayer is null || Layers.ActiveLayer.Model.IsGroup) return;

            var options = new BlurGalleryViewModel(iris: kind == "Iris");
            Canvas.BeginAdjustment(options.Title);
            void UpdatePreview(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
                Canvas.UpdateAdjustmentPreview(options.Apply);
            options.PropertyChanged += UpdatePreview;
            UpdatePreview(null, null!);

            var dialog = new BlurGalleryDialog { DataContext = options, Owner = Application.Current.MainWindow };
            var applied = dialog.ShowDialog() == true;
            options.PropertyChanged -= UpdatePreview;

            if (applied)
            {
                CommitFilter(StepForBlurGallery(options.Settings, options.Title));
            }
            else
            {
                Canvas.CancelAdjustment();
            }
        }

        /// <summary>Filter > Camera Raw Filter: white balance, exposure, tone and presence
        /// sliders with a live canvas preview (see <see cref="CameraRawViewModel"/>).</summary>
        [RelayCommand]
        private void OpenCameraRaw()
        {
            if (IsSaving || Layers.ActiveLayer is null || Layers.ActiveLayer.Model.IsGroup) return;

            Canvas.BeginAdjustment("Camera Raw Filter");
            var options = new CameraRawViewModel();
            void UpdatePreview(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
                Canvas.UpdateAdjustmentPreview(options.Apply);
            options.PropertyChanged += UpdatePreview;

            var dialog = new CameraRawDialog { DataContext = options, Owner = Application.Current.MainWindow };
            var applied = dialog.ShowDialog() == true;
            options.PropertyChanged -= UpdatePreview;

            if (applied && !options.Settings.IsNeutral)
            {
                CommitFilter(StepForCameraRaw(options.Settings));
            }
            else
            {
                Canvas.CancelAdjustment();
            }
        }

        /// <summary>Filter > Liquify: brushes that push, pinch, bloat and twirl the
        /// layer's pixels (see <see cref="LiquifyViewModel"/>). The result is one undo step.</summary>
        [RelayCommand]
        private void OpenLiquify()
        {
            if (IsSaving || Layers.ActiveLayer is null || Layers.ActiveLayer.Model.IsGroup) return;

            if (Layers.ActiveLayer.Model.HasSmartFilters)
            {
                MessageBox.Show("Liquify can't be a Smart Filter yet. Rasterize the layer first (Layer > Rasterize Smart Filters).",
                    "Liquify", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Canvas.BeginAdjustment("Liquify");
            if (Canvas.AdjustmentSourceBitmap is not { } source) { Canvas.CancelAdjustment(); return; }

            // The other layers, shown under the one being reshaped.
            var active = Layers.ActiveLayer.Model;
            var wasVisible = active.IsVisible;
            active.IsVisible = false;
            using var backdrop = Core.Rendering.SceneCompositor.Flatten(Layers.Scene, source.Width, source.Height);
            active.IsVisible = wasVisible;

            using var options = new LiquifyViewModel(source, backdrop: backdrop);
            var dialog = new LiquifyDialog(options) { Owner = Application.Current.MainWindow };
            if (dialog.ShowDialog() == true && options.HasChanges)
            {
                Canvas.UpdateAdjustmentPreview(options.ApplyTo);
                Canvas.CommitAdjustment();
            }
            else
            {
                Canvas.CancelAdjustment();
            }
        }

        /// <summary>Opens the shared Brightness/Contrast or Hue/Saturation/Lightness
        /// dialog (see <see cref="SimpleAdjustmentViewModel"/>) - same live-preview/
        /// commit-or-cancel pattern as <see cref="OpenFilterDialog"/>.</summary>
        [RelayCommand]
        private void OpenSimpleAdjustmentDialog(SimpleAdjustmentKind kind)
        {
            if (IsSaving || Layers.ActiveLayer is null || Layers.ActiveLayer.Model.IsGroup) return;

            var adjustmentTitle = kind switch { SimpleAdjustmentKind.BrightnessContrast => "Brightness/Contrast", SimpleAdjustmentKind.HueSaturation => "Hue/Saturation", _ => kind.ToString() };
            Canvas.BeginAdjustment(adjustmentTitle);

            var options = new SimpleAdjustmentViewModel(kind);
            void UpdatePreview(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
                Canvas.UpdateAdjustmentPreview(options.Apply);
            options.PropertyChanged += UpdatePreview;
            UpdatePreview(null, null!);

            var dialog = new SimpleAdjustmentDialog { DataContext = options, Owner = Application.Current.MainWindow };
            var applied = dialog.ShowDialog() == true;
            options.PropertyChanged -= UpdatePreview;

            if (applied)
            {
                CommitFilter(StepForSimpleAdjustment(options, adjustmentTitle));
            }
            else
            {
                Canvas.CancelAdjustment();
            }
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
            if (IsSaving || Layers.ActiveLayer is null || Layers.ActiveLayer.Model.IsGroup) return;

            Canvas.BeginAdjustment("Levels");
            if (Canvas.AdjustmentSourceBitmap is not { } source) { Canvas.CancelAdjustment(); return; }

            var options = new LevelsViewModel(source);
            void UpdatePreview(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
                Canvas.UpdateAdjustmentPreview(options.Apply);
            options.PropertyChanged += UpdatePreview;
            UpdatePreview(null, null!);

            var dialog = new LevelsDialog { DataContext = options, Owner = Application.Current.MainWindow };
            var applied = dialog.ShowDialog() == true;
            options.PropertyChanged -= UpdatePreview;

            if (applied)
            {
                CommitFilter(StepForLevels(options));
            }
            else
            {
                Canvas.CancelAdjustment();
            }
        }

        /// <summary>Opens Curves (see <see cref="CurvesViewModel"/>) - same live-
        /// preview/commit-or-cancel pattern; the control-point collection's own
        /// changes (not a single settable property) drive the preview here.</summary>
        [RelayCommand]
        private void OpenCurvesDialog()
        {
            if (IsSaving || Layers.ActiveLayer is null || Layers.ActiveLayer.Model.IsGroup) return;

            Canvas.BeginAdjustment("Curves");

            var options = new CurvesViewModel();
            void UpdatePreview(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
                Canvas.UpdateAdjustmentPreview(options.Apply);
            options.PropertyChanged += UpdatePreview;
            UpdatePreview(null, null!);

            var dialog = new CurvesDialog { DataContext = options, Owner = Application.Current.MainWindow };
            var applied = dialog.ShowDialog() == true;
            options.PropertyChanged -= UpdatePreview;

            if (applied)
            {
                CommitFilter(StepForCurves(options));
            }
            else
            {
                Canvas.CancelAdjustment();
            }
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
