using System;
using System.IO;
using System.Linq;
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
        [ObservableProperty]
        private bool isLeftPanelVisible = true;

        [ObservableProperty]
        private bool isRightPanelVisible = true;

        [ObservableProperty]
        private bool isBottomPanelVisible = true;

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

        private bool CanUndo() => Layers.History.CanUndo;

        [RelayCommand(CanExecute = nameof(CanRedo))]
        private void Redo() => Layers.History.Redo();

        private bool CanRedo() => Layers.History.CanRedo;

        /// <summary>Prompts for a destination and writes the whole open project - every
        /// layer's pixels and metadata, plus the timeline's tracks/clips/keyframes - as a
        /// single <c>.jolie</c> file (see <see cref="ProjectSerializer.Save"/>).</summary>
        [RelayCommand]
        private void SaveProject()
        {
            var dialog = new SaveFileDialog
            {
                Filter = "JolieCat Project (*.jolie)|*.jolie",
                DefaultExt = ".jolie",
                FileName = "Untitled.jolie",
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                ProjectSerializer.Save(dialog.FileName, Layers.Scene, BuildTimelineData(), Timeline.TotalFrames, Timeline.FrameRate);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Couldn't save the project:\n{ex.Message}", "Save Project", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>Prompts for a <c>.jolie</c> file and replaces the open document (layers
        /// and timeline alike) with what it contains (see <see cref="ProjectSerializer.Load"/>).</summary>
        [RelayCommand]
        private void OpenProject()
        {
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
