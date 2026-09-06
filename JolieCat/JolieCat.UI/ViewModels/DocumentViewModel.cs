using System;
using CommunityToolkit.Mvvm.ComponentModel;
using JolieCat.Core.Documents;
using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels.Layers;
using JolieCat.UI.ViewModels.Timeline;

namespace JolieCat.UI.ViewModels
{
    /// <summary>
    /// One open document/tab: its own independent <see cref="Layers"/> (Scene + layer
    /// stack + undo/redo history), <see cref="Canvas"/> (pan/zoom + paint-interaction
    /// state), and <see cref="Timeline"/> - everything <see cref="MainViewModel"/> used
    /// to own as a single instance of each, now one set per open document so switching
    /// tabs is a matter of which <see cref="DocumentViewModel"/> is active, not
    /// recreating or resetting any of them.
    /// </summary>
    /// <remarks>
    /// <see cref="ToolboxViewModel"/> is deliberately NOT duplicated per document - the
    /// active tool and its options are shared app-wide state, exactly like every other
    /// editor's own single tool palette that stays selected as you switch between open
    /// files, so it's passed in from <see cref="MainViewModel"/> rather than owned here.
    /// </remarks>
    public sealed partial class DocumentViewModel : ObservableObject, IDisposable
    {
        private bool _disposed;

        /// <summary>The tab header's text - the document's filename once saved/opened,
        /// or "Untitled N" for a new, never-saved document.</summary>
        [ObservableProperty]
        private string title;

        /// <summary>Absolute path of the <c>.jolie</c> file this document was opened
        /// from or last saved to, or null if it has never been saved.</summary>
        [ObservableProperty]
        private string? filePath;

        public LayersViewModel Layers { get; }

        public CanvasViewModel Canvas { get; }

        public TimelineViewModel Timeline { get; }

        /// <summary>Set when this tab was opened via "Edit Contents" on a Smart Object
        /// layer (see <c>MainViewModel.EditSmartObjectContents</c>) - the layer whose
        /// own <see cref="Layer.SmartObject"/> owns this tab's <see cref="Layers"/>'
        /// <see cref="Layers.Scene"/> for that layer's whole lifetime (across repeated
        /// Edit Contents sessions, not just this one tab). Null for every ordinary
        /// document tab. <see cref="Dispose"/> leaves that Scene undisposed precisely
        /// because of this - see its own remarks.</summary>
        public Layer? SmartObjectHostLayer { get; init; }

        /// <summary>Mirrors <see cref="Layers"/>' own <see cref="Layers.ProjectType"/> -
        /// see <see cref="Shared.Enums.ProjectType"/>'s own remarks for what each mode
        /// changes about the workspace.</summary>
        public ProjectType ProjectType => Layers.ProjectType;

        /// <summary>Which content the center workspace area shows for this tab - see
        /// <see cref="ViewModels.WorkspaceMode"/>'s own remarks. Defaults to
        /// <see cref="WorkspaceMode.Timeline"/> for a <see cref="Shared.Enums.ProjectType.ClipbarAnimation"/>
        /// project (see the constructor) so it always opens straight into the
        /// dedicated Timeline workspace rather than requiring an extra click every
        /// time - every other project type defaults to (and, since MainWindow.xaml
        /// only ever shows the Design/Timeline switch for a Clipbar Animation project
        /// at all, effectively stays at) <see cref="WorkspaceMode.Design"/>.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsDesignMode))]
        [NotifyPropertyChangedFor(nameof(IsTimelineMode))]
        private WorkspaceMode workspaceMode = WorkspaceMode.Design;

        public bool IsDesignMode => WorkspaceMode == WorkspaceMode.Design;

        public bool IsTimelineMode => WorkspaceMode == WorkspaceMode.Timeline;

        public DocumentViewModel(ToolboxViewModel toolbox, string title, ProjectType projectType = ProjectType.StandardImage)
        {
            ArgumentNullException.ThrowIfNull(toolbox);
            if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Document title cannot be empty.", nameof(title));

            this.title = title;
            Layers = new LayersViewModel(projectType);
            Canvas = new CanvasViewModel(toolbox, Layers);
            Timeline = new TimelineViewModel();

            // The Timeline's own flipbook logic (see TimelineViewModel.UpdateFrameLayerVisibility)
            // mutates a frame Layer's IsVisible directly, not through Layers.LayerViewModel,
            // so nothing else here would otherwise notice the canvas needs repainting.
            Timeline.FrameVisibilityChanged += OnTimelineFrameVisibilityChanged;

            if (projectType == ProjectType.ClipbarAnimation)
                workspaceMode = WorkspaceMode.Timeline;
        }

        private void OnTimelineFrameVisibilityChanged(object? sender, EventArgs e) => Layers.RequestRepaint();

        /// <summary>Disposes <see cref="Canvas"/> (which unsubscribes from the shared,
        /// long-lived <see cref="ToolboxViewModel"/>'s events - without this, a closed
        /// document's CanvasViewModel, and everything it references including every
        /// layer's native bitmap, would be kept alive forever by that subscription) and
        /// <see cref="Layers"/> (which frees every layer's/mask's unmanaged pixel
        /// buffer) - except when <see cref="SmartObjectHostLayer"/> is set: that tab's
        /// <see cref="Layers"/>' <see cref="Layers.Scene"/> is the very same
        /// <see cref="Documents.SmartObjectContent.EmbeddedScene"/> instance the host
        /// layer keeps for its own whole lifetime (so a later "Edit Contents" session
        /// can reopen and keep editing it), so disposing it here - the one place a
        /// Scene's bitmaps are ever freed - would leave that Smart Object permanently
        /// broken the moment this one editing tab happened to close. Also disposes
        /// <see cref="Timeline"/>, which stops its own playback timer - left running,
        /// it would keep ticking (and advancing a now-orphaned <see cref="TimelineViewModel.CurrentFrame"/>)
        /// forever even after this document is gone.</summary>
        public void Dispose()
        {
            if (_disposed) return;

            Timeline.FrameVisibilityChanged -= OnTimelineFrameVisibilityChanged;
            Canvas.Dispose();
            Timeline.Dispose();
            if (SmartObjectHostLayer is null)
                Layers.Dispose();
            _disposed = true;
        }
    }
}
