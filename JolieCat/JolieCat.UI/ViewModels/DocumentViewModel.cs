using System;
using CommunityToolkit.Mvvm.ComponentModel;
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

        public DocumentViewModel(ToolboxViewModel toolbox, string title)
        {
            ArgumentNullException.ThrowIfNull(toolbox);
            if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Document title cannot be empty.", nameof(title));

            this.title = title;
            Layers = new LayersViewModel();
            Canvas = new CanvasViewModel(toolbox, Layers);
            Timeline = new TimelineViewModel();
        }

        /// <summary>Disposes <see cref="Canvas"/> (which unsubscribes from the shared,
        /// long-lived <see cref="ToolboxViewModel"/>'s events - without this, a closed
        /// document's CanvasViewModel, and everything it references including every
        /// layer's native bitmap, would be kept alive forever by that subscription) and
        /// <see cref="Layers"/> (which frees every layer's/mask's unmanaged pixel
        /// buffer). <see cref="Timeline"/> holds no unmanaged resources or external
        /// subscriptions of its own.</summary>
        public void Dispose()
        {
            if (_disposed) return;

            Canvas.Dispose();
            Layers.Dispose();
            _disposed = true;
        }
    }
}
