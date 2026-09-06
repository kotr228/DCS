using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Serialization;
using JolieCat.Core.Tools;
using JolieCat.UI.ViewModels.Layers;
using JolieCat.UI.ViewModels.Timeline;
using Microsoft.Win32;

namespace JolieCat.UI.ViewModels
{
    /// <summary>
    /// View model for <see cref="MainWindow"/>: owns the visibility of the four docking
    /// zones (Left/Right/Bottom) around the central canvas, and composes the panels'
    /// own view models (toolbox, layers, canvas, timeline) rather than flattening their
    /// state in here as the editor grows more panels. Also owns the two things that cut
    /// across every panel: undo/redo (delegating to <see cref="LayersViewModel.History"/>,
    /// the document's single shared history) and whole-project Save/Open.
    /// </summary>
    public partial class MainViewModel : ObservableObject
    {
        /// <summary>The exact text the status bar shows once a save completes - a
        /// constant (rather than an inline literal) so MainWindow.xaml's fade-out
        /// trigger, which has to match it exactly to fire, references this instead of
        /// its own separately-typed copy (via <c>x:Static</c>).</summary>
        public const string SaveSuccessMessage = "✓ Project saved successfully";

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
        /// a little while after this already flips back to false. Also gates Undo/Redo's
        /// own CanExecute (see below): the overlay only blocks mouse input, but Undo and
        /// Redo can each mutate or entirely replace a layer's SKBitmap synchronously on
        /// the UI thread, while SaveAsync is concurrently reading that same layer's
        /// pixels on a background thread - a real race, not just an unwanted concurrent
        /// edit, so these two specifically need to be genuinely disabled (not merely
        /// blocked by the overlay) rather than just re-guarded like SaveProjectAsync/
        /// OpenProject/ImportImage's own early-return checks.</summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
        [NotifyCanExecuteChangedFor(nameof(RedoCommand))]
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
        /// (0,0)" for that reason, not as a placeholder. Recomputed on every
        /// <see cref="LayersViewModel.InvalidateRequested"/> (structural changes,
        /// undo/redo, opening a project, importing an image - anything that could
        /// change either size), which is the same event the canvas repaint itself
        /// already relies on for "did anything worth redrawing just happen".</summary>
        [ObservableProperty]
        private string dimensionDebugText = string.Empty;

        public ToolboxViewModel Toolbox { get; }

        public LayersViewModel Layers { get; }

        public CanvasViewModel Canvas { get; }

        public TimelineViewModel Timeline { get; }

        public MainViewModel()
        {
            Toolbox = new ToolboxViewModel();
            Layers = new LayersViewModel();
            Canvas = new CanvasViewModel(Toolbox, Layers);
            Timeline = new TimelineViewModel();

            // Undo/Redo's enabled state (bound from MainWindow's Edit menu/toolbar, if
            // any, and implicitly from the Ctrl+Z/Ctrl+Y key bindings' own CanExecute
            // gate) needs to track the shared history regardless of which panel actually
            // pushed the command that changed it.
            Layers.History.Changed += (_, _) =>
            {
                UndoCommand.NotifyCanExecuteChanged();
                RedoCommand.NotifyCanExecuteChanged();
            };

            // InvalidateRequested covers everything that can change either size
            // (structural changes, undo/redo, opening a project, importing an image);
            // PropertyChanged additionally covers just clicking a different layer in
            // the Layers panel active, which changes ActiveLayer without itself
            // requesting a repaint - otherwise the readout would keep showing the
            // previously active layer's name until something else invalidated.
            Layers.InvalidateRequested += (_, _) => UpdateDimensionDebugText();
            Layers.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(LayersViewModel.ActiveLayer)) UpdateDimensionDebugText();
            };
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

        [RelayCommand(CanExecute = nameof(CanUndo))]
        private void Undo() => Layers.History.Undo();

        private bool CanUndo() => Layers.History.CanUndo && !IsSaving;

        [RelayCommand(CanExecute = nameof(CanRedo))]
        private void Redo() => Layers.History.Redo();

        private bool CanRedo() => Layers.History.CanRedo && !IsSaving;

        /// <summary>Prompts for a destination and writes the whole open project - every
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

            var dialog = new SaveFileDialog
            {
                Filter = "JolieCat Project (*.jolie)|*.jolie",
                DefaultExt = ".jolie",
                FileName = "Untitled.jolie",
            };
            if (dialog.ShowDialog() != true) return;

            IsSaving = true;
            SaveStatusMessage = "Saving project...";

            try
            {
                await ProjectSerializer.SaveAsync(dialog.FileName, Layers.Scene, BuildTimelineData(), Timeline.TotalFrames, Timeline.FrameRate);

                SaveStatusMessage = SaveSuccessMessage;
                _ = ClearSaveStatusAfterDelayAsync();
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

        /// <summary>Clears <see cref="SaveStatusMessage"/> back to empty once <see cref="SaveSuccessMessage"/>
        /// has been up long enough to fade out on its own (see MainWindow.xaml's
        /// DataTrigger) - not awaited by the caller, since the save itself is already
        /// long finished by this point and nothing should block on a purely cosmetic
        /// timer. Clearing back to empty (rather than leaving the success text in place)
        /// also matters functionally: a DataTrigger only fires on a value *change*, so
        /// without this the very next save's success message wouldn't re-trigger the
        /// fade at all.</summary>
        private async Task ClearSaveStatusAfterDelayAsync()
        {
            await Task.Delay(SaveSuccessDisplayDuration);

            if (SaveStatusMessage == SaveSuccessMessage)
                SaveStatusMessage = string.Empty;
        }

        /// <summary>Prompts for a <c>.jolie</c> file and replaces the open document (layers
        /// and timeline alike) with what it contains (see <see cref="ProjectSerializer.Load"/>).</summary>
        [RelayCommand]
        private void OpenProject()
        {
            // Same reasoning as SaveProjectAsync's own guard: a keyboard shortcut isn't
            // stopped by the mouse-blocking saving overlay, so this needs to check for
            // itself that a save isn't still in flight before replacing the document
            // out from under it.
            if (IsSaving) return;

            var dialog = new OpenFileDialog { Filter = "JolieCat Project (*.jolie)|*.jolie" };
            if (dialog.ShowDialog() != true) return;

            try
            {
                var result = ProjectSerializer.Load(dialog.FileName);
                Layers.LoadScene(result.Scene, Path.GetFileNameWithoutExtension(dialog.FileName));
                Timeline.LoadTracks(result.TimelineTracks, result.TimelineTotalFrames, result.TimelineFrameRate);
                Canvas.ResetView();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Couldn't open '{dialog.FileName}':\n{ex.Message}", "Open Project", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>Prompts for one or more image files (PNG/JPG/JPEG/BMP/WEBP) and
        /// imports each as its own new layer - the file-dialog counterpart to dropping
        /// images directly onto the canvas (see <see cref="CanvasViewModel.ImportImageFiles"/>,
        /// which both paths funnel through).</summary>
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

        /// <summary>Maps the Timeline panel's view models to the plain, UI-agnostic data
        /// <see cref="ProjectSerializer"/> actually writes - this mapping (not the Core
        /// serializer) is what keeps <c>JolieCat.Core</c> from needing to reference
        /// <c>JolieCat.UI</c>'s timeline view models at all.</summary>
        private TimelineTrackData[] BuildTimelineData() => Timeline.Tracks.Select(track => new TimelineTrackData
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
