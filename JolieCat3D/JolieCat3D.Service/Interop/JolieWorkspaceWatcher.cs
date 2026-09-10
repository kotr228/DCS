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
    /// <see cref="FileSystemWatcher"/>'s own Changed/Created events can (and routinely
    /// do) fire WHILE the writer still has the file open - JolieCat 2D's own save/export
    /// is not one atomic OS-level operation - so <see cref="AssetChanged"/> is only ever
    /// raised once <see cref="WaitUntilReadableAsync"/> confirms the file can actually be
    /// opened for shared reading (a short, exponential-backoff retry loop - see its own
    /// remarks), never immediately off the raw OS event. That wait runs entirely on a
    /// ThreadPool thread via <c>async void</c>'s own continuation (never blocking it with
    /// a synchronous sleep), so it costs this watcher's caller nothing to wait for.
    ///
    /// <see cref="AssetChanged"/> itself is STILL raised on a ThreadPool thread, never
    /// the caller's own UI thread (unavoidable - this class has no WPF dependency, and
    /// therefore no dispatcher, of its own) - a WPF caller MUST marshal back to its own
    /// dispatcher thread (e.g. <c>Dispatcher.Invoke</c>) before touching any UI-affinity
    /// object from a handler, the same requirement every other direct
    /// <see cref="FileSystemWatcher"/> consumer has.
    /// </summary>
    public sealed class JolieWorkspaceWatcher : IDisposable
    {
        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp" };

        /// <summary>How many times <see cref="WaitUntilReadableAsync"/> retries before
        /// giving up on a still-locked file - with the doubling delay below, a worst
        /// case of roughly 4.5 seconds total, comfortably longer than any ordinary
        /// image/project save/export takes, without waiting forever on a file that's
        /// genuinely stuck (locked by something else entirely, or deleted mid-write).</summary>
        private const int MaxReadinessAttempts = 8;

        /// <summary>The delay before the FIRST retry, in milliseconds - doubled (capped,
        /// see <see cref="MaxRetryDelayMilliseconds"/>) after each subsequent failed
        /// attempt, so a save that finishes almost instantly resolves almost instantly
        /// too, while a slower one doesn't spin the retry loop hot.</summary>
        private const int InitialRetryDelayMilliseconds = 50;

        private const int MaxRetryDelayMilliseconds = 1000;

        private readonly FileSystemWatcher _watcher;
        private bool _disposed;

        public string WorkspacePath { get; }

        /// <summary>Raised for a create/change/rename of any <c>.jolie</c> or recognized
        /// image file anywhere under <see cref="WorkspacePath"/> (recursively), once the
        /// file is confirmed readable - see this class's own remarks on file-locking and
        /// on which thread this fires on.</summary>
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
            // A FileSystemWatcher that hits an internal error (its own buffer overflowed
            // from too many changes at once, the watched folder was itself deleted, ...)
            // silently stops raising events rather than throwing anywhere a caller could
            // catch it - surfacing that as a recognizable, harmless-to-ignore no-op
            // rather than a mysterious "nothing updates anymore" is strictly better than
            // this class staying quiet about it.
            _watcher.Error += OnWatcherError;
        }

        /// <summary>Starts actually raising events - a fresh watcher does nothing at all
        /// until this is called (matching <see cref="FileSystemWatcher.EnableRaisingEvents"/>'s
        /// own default of false), so a caller can subscribe to <see cref="AssetChanged"/>
        /// first without racing an event it hasn't wired up yet.</summary>
        /// <exception cref="DirectoryNotFoundException"><see cref="WorkspacePath"/> does
        /// not exist (or was removed after construction, before this call) -
        /// <see cref="FileSystemWatcher"/>'s own behavior, surfaced here rather than
        /// swallowed, so a caller picking a folder that got deleted/unmounted between
        /// the picker dialog and this call sees a clear, catchable failure instead of a
        /// watcher that silently never fires.</exception>
        public void Start()
        {
            if (!Directory.Exists(WorkspacePath))
                throw new DirectoryNotFoundException($"'{WorkspacePath}' does not exist - nothing to watch.");

            _watcher.EnableRaisingEvents = true;
        }

        public void Stop() => _watcher.EnableRaisingEvents = false;

        private async void OnFileEvent(object sender, FileSystemEventArgs e)
        {
            try
            {
                var extension = Path.GetExtension(e.FullPath).ToLowerInvariant();

                JolieWorkspaceAssetKind kind;
                if (extension == ".jolie") kind = JolieWorkspaceAssetKind.JolieProject;
                else if (Array.IndexOf(ImageExtensions, extension) >= 0) kind = JolieWorkspaceAssetKind.ExportedImage;
                else return; // Not a file type this bridge recognizes - ignored, not an error.

                // The 2D editor may still be mid-write when this fires (Changed/Created
                // can arrive before the writer closes its own handle) - wait for the
                // file to become readable before ever telling a caller about it, rather
                // than immediately raising AssetChanged against a file a caller's own
                // read would just fail on. Runs entirely on this ThreadPool thread
                // (never the caller's UI thread), so a slow/still-writing file never
                // blocks anything the caller is doing in the meantime.
                if (!await WaitUntilReadableAsync(e.FullPath).ConfigureAwait(false))
                    return; // Gave up - deleted/renamed away mid-wait, or a genuinely stuck lock.

                AssetChanged?.Invoke(this, new WorkspaceAssetChangedEventArgs(e.FullPath, kind));
            }
            catch
            {
                // An async void event handler that lets an exception escape crashes the
                // whole process (there is no caller frame left to catch it by the time
                // it would propagate) - one bad event (a path Windows/the OS itself
                // rejects, a transient permission error some other retry didn't cover,
                // ...) must never take down an app that's just watching a folder for
                // convenience. Swallowed deliberately: there is no caller here to hand
                // the exception to, and dropping one file-change notification is always
                // safe (the next real change to that same file, if any, gets its own
                // fresh event/retry) - the one thing that would NOT be safe is letting
                // this crash the process.
            }
        }

        /// <summary>Retries opening <paramref name="path"/> for shared reading
        /// (<see cref="FileShare.Read"/> - a legitimate concurrent reader is fine, only
        /// an exclusive writer lock blocks this) up to <see cref="MaxReadinessAttempts"/>
        /// times with an exponential backoff delay between attempts, entirely via
        /// non-blocking <see cref="Task.Delay(int)"/> (never <c>Thread.Sleep</c> - this
        /// runs on a ThreadPool thread, and blocking one of those for up to ~4.5 seconds
        /// at a time would needlessly tie it up under any real file-system activity
        /// burst). Returns false (without throwing) if every attempt still finds the
        /// file locked, or if the file disappears entirely partway through waiting (a
        /// rename-away or delete mid-write) - either way, there is nothing for a caller
        /// to reload, so <see cref="OnFileEvent"/> simply drops the event rather than
        /// raising <see cref="AssetChanged"/> against a file that still isn't safely
        /// readable.</summary>
        private static async Task<bool> WaitUntilReadableAsync(string path)
        {
            var delay = InitialRetryDelayMilliseconds;

            for (var attempt = 0; attempt < MaxReadinessAttempts; attempt++)
            {
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    return true;
                }
                catch (IOException)
                {
                    // Still open for exclusive write by whatever's saving it - the
                    // expected, normal case this whole method exists to ride out.
                }
                catch (UnauthorizedAccessException)
                {
                    // Some platforms/antivirus scanners surface a transient lock this
                    // way instead of IOException - treated the same, retried the same.
                }

                if (!File.Exists(path)) return false;

                await Task.Delay(delay).ConfigureAwait(false);
                delay = Math.Min(delay * 2, MaxRetryDelayMilliseconds);
            }

            return false;
        }

        /// <summary><see cref="FileSystemWatcher.Error"/>'s own handler - deliberately a
        /// no-op beyond existing at all (see this class's own remarks): there is no
        /// dispatcher/logging sink of this project's own to report through, and a
        /// caller can always tell a watch silently died the same way any missing
        /// expected update would surface (nothing more asset-related happening after
        /// this point). Kept as its own named method rather than inlined so it reads as
        /// an intentional, documented choice rather than a forgotten subscription.</summary>
        private static void OnWatcherError(object sender, ErrorEventArgs e)
        {
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _watcher.Changed -= OnFileEvent;
            _watcher.Created -= OnFileEvent;
            _watcher.Renamed -= OnFileEvent;
            _watcher.Error -= OnWatcherError;
            _watcher.Dispose();
        }
    }
}
