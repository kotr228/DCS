using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using JolieCat.UI.Media;

namespace JolieCat.UI.ViewModels
{
    /// <summary>Session safety: autosave for crash recovery, restoring after a crash,
    /// and asking about unsaved work when the app closes.</summary>
    public partial class MainViewModel
    {
        /// <summary>How often unsaved documents are copied to the recovery folder.</summary>
        public static readonly TimeSpan AutosaveInterval = TimeSpan.FromMinutes(2);

        internal DocumentRecovery Recovery { get; } = new();

        private DispatcherTimer? _autosaveTimer;

        /// <summary>Called once the main window is shown: offers to restore documents
        /// left behind by a crash, then starts the autosave timer.</summary>
        public void StartSession()
        {
            RestoreAfterCrash();

            _autosaveTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = AutosaveInterval };
            _autosaveTimer.Tick += async (_, _) => await AutosaveNowAsync();
            _autosaveTimer.Start();
        }

        /// <summary>Writes recovery copies of unsaved documents (skipped while a real
        /// save is running or a dialog/tool gesture is in progress).</summary>
        public Task AutosaveNowAsync() => IsSaving ? Task.CompletedTask : Recovery.AutosaveAsync(Documents, BuildTimelineData);

        private void RestoreAfterCrash()
        {
            var leftovers = Recovery.FindLeftovers();
            if (leftovers.Count == 0) return;

            var names = string.Join("\n", leftovers.Select(l => "  • " + l.Title));
            var answer = MessageBox.Show(Application.Current.MainWindow!,
                $"JolieCat didn't close properly last time. These documents had unsaved changes:\n\n{names}\n\nRestore them?",
                "Restore documents", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (answer == MessageBoxResult.Yes)
            {
                var startDocument = Documents.Count == 1 && !Documents[0].IsDirty && Documents[0].FilePath is null ? Documents[0] : null;

                foreach (var leftover in leftovers)
                {
                    var before = Documents.Count;
                    OpenPath(leftover.ProjectPath);
                    if (Documents.Count == before) continue; // couldn't be read

                    var restored = Documents[^1];
                    restored.Title = leftover.Title;
                    // Saving goes back to the original project, if it was one.
                    restored.FilePath = leftover.OriginalPath;
                    restored.Layers.History.MarkModified(); // still unsaved
                }

                // Drop the empty "Untitled 1" the app started with.
                if (startDocument is not null && Documents.Count > 1)
                {
                    Documents.Remove(startDocument);
                    startDocument.Dispose();
                    ActiveDocument ??= Documents[^1];
                }

                // The restored copies were opened from the recovery folder; it can't be
                // listed as a recent file.
                foreach (var leftover in leftovers) RecentFiles.Remove(leftover.ProjectPath);
                RecentFilesStore.Save(RecentFiles);
                OnPropertyChanged(nameof(HasRecentFiles));
            }

            Recovery.DeleteAll();
        }

        /// <summary>Window closing: asks about each unsaved document. Returns false if
        /// the user cancelled (the window should stay open).</summary>
        public async Task<bool> PrepareToExitAsync()
        {
            if (IsSaving) return false;
            if (!await ConfirmCloseAllAsync()) return false;

            _autosaveTimer?.Stop();
            Recovery.DeleteAll();
            return true;
        }
    }
}
