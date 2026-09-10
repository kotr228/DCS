namespace JolieCat3D.Service.Interop
{
    /// <summary>Which kind of file <see cref="JolieWorkspaceWatcher.AssetChanged"/> is
    /// reporting a change to.</summary>
    public enum JolieWorkspaceAssetKind
    {
        /// <summary>A whole <c>.jolie</c> project file (see <see cref="JolieProjectReader"/>).</summary>
        JolieProject,

        /// <summary>A plain exported image - a texture, a sprite-sheet PNG, or one frame
        /// of a clipbar sequence (see <see cref="TwoDAssetBridge"/>).</summary>
        ExportedImage,
    }

    /// <summary>One file-system change <see cref="JolieWorkspaceWatcher"/> noticed.</summary>
    public sealed record WorkspaceAssetChangedEventArgs(string FilePath, JolieWorkspaceAssetKind Kind);

    /// <summary>
    /// Watches a JolieCat 2D editor workspace folder (recursively) for new/changed/
    /// renamed <c>.jolie</c> project files and exported image files, raising
    /// <see cref="AssetChanged"/> so a caller (<c>JolieCat3D.UI</c>) can automatically
    /// reload a material's texture the moment JolieCat 2D re-saves or re-exports it -
    /// the actual "automatically detect, load, or refresh" bridge the task asks for. A
    /// thin, disposable wrapper over the BCL's own <see cref="FileSystemWatcher"/> - no
    /// polling loop of this project's own, and nothing JolieCat-2D-specific beyond
    /// recognizing its own file extensions (see <see cref="ImageExtensions"/>).
    ///
    /// <see cref="AssetChanged"/> is raised on whichever background thread
    /// <see cref="FileSystemWatcher"/> itself uses (a ThreadPool thread, NOT the caller's
    /// own thread) - a WPF caller MUST marshal back to its own dispatcher thread (e.g.
    /// <c>Dispatcher.Invoke</c>) before touching any UI-affinity object from a handler,
    /// the same requirement every other direct <see cref="FileSystemWatcher"/> consumer
    /// has; this class has no WPF dependency of its own to do that marshaling itself.
    /// </summary>
    public sealed class JolieWorkspaceWatcher : IDisposable
    {
        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp" };

        private readonly FileSystemWatcher _watcher;
        private bool _disposed;

        public string WorkspacePath { get; }

        /// <summary>Raised for a create/change/rename of any <c>.jolie</c> or recognized
        /// image file anywhere under <see cref="WorkspacePath"/> (recursively) - see this
        /// class's own remarks on which thread this fires on.</summary>
        public event EventHandler<WorkspaceAssetChangedEventArgs>? AssetChanged;

        public JolieWorkspaceWatcher(string workspacePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
            WorkspacePath = workspacePath;

            _watcher = new FileSystemWatcher(workspacePath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.Size,
            };
            // "*.*" (FileSystemWatcher's own default Filter) plus a manual extension
            // check in the handler, rather than one watcher per extension - a single
            // FileSystemWatcher only ever has one Filter pattern active at a time.
            _watcher.Changed += OnFileEvent;
            _watcher.Created += OnFileEvent;
            _watcher.Renamed += OnFileEvent;
        }

        /// <summary>Starts actually raising events - a fresh watcher does nothing at all
        /// until this is called (matching <see cref="FileSystemWatcher.EnableRaisingEvents"/>'s
        /// own default of false), so a caller can subscribe to <see cref="AssetChanged"/>
        /// first without racing an event it hasn't wired up yet.</summary>
        public void Start() => _watcher.EnableRaisingEvents = true;

        public void Stop() => _watcher.EnableRaisingEvents = false;

        private void OnFileEvent(object sender, FileSystemEventArgs e)
        {
            var extension = Path.GetExtension(e.FullPath).ToLowerInvariant();

            JolieWorkspaceAssetKind kind;
            if (extension == ".jolie") kind = JolieWorkspaceAssetKind.JolieProject;
            else if (Array.IndexOf(ImageExtensions, extension) >= 0) kind = JolieWorkspaceAssetKind.ExportedImage;
            else return; // Not a file type this bridge recognizes - ignored, not an error.

            AssetChanged?.Invoke(this, new WorkspaceAssetChangedEventArgs(e.FullPath, kind));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _watcher.Changed -= OnFileEvent;
            _watcher.Created -= OnFileEvent;
            _watcher.Renamed -= OnFileEvent;
            _watcher.Dispose();
        }
    }
}
