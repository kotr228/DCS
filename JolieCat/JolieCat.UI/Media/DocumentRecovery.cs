using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using JolieCat.Core.Documents;
using JolieCat.Core.Serialization;
using JolieCat.UI.ViewModels;

namespace JolieCat.UI.Media
{
    /// <summary>
    /// Crash recovery. Every few minutes each document with unsaved changes is written to
    /// %LOCALAPPDATA%\JolieCat\Recovery as "{id}.jolie" plus a small "{id}.info" file
    /// (title and original path). Saving or closing a document deletes its copy, and a
    /// normal exit deletes all of them - so anything left there at startup means the app
    /// didn't shut down cleanly, and those documents are offered for restoring.
    /// </summary>
    public sealed class DocumentRecovery
    {
        private readonly string _folder;
        private bool _isWriting;

        public DocumentRecovery(string? folder = null)
        {
            _folder = folder ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JolieCat", "Recovery");
        }

        public sealed record RecoveredDocument(string ProjectPath, string Title, string? OriginalPath);

        /// <summary>Writes a recovery copy of every document changed since its last
        /// autosave. The layers are copied on the calling (UI) thread - quick, and safe
        /// against edits - and encoded/written on a background thread.</summary>
        public async Task AutosaveAsync(IEnumerable<DocumentViewModel> documents, Func<DocumentViewModel, IReadOnlyList<TimelineTrackData>> timeline)
        {
            if (_isWriting) return;
            _isWriting = true;

            try
            {
                foreach (var document in documents.ToList())
                {
                    if (document.SmartObjectHostLayer is not null) continue;

                    var counter = document.Layers.History.ChangeCounter;
                    if (!document.IsDirty || counter == document.LastAutosavedChange) continue;

                    var snapshot = document.Layers.Scene.CaptureLayers();
                    var activeIndex = IndexOfActive(document.Layers.Scene);
                    var tracks = timeline(document);
                    var totalFrames = document.Timeline.TotalFrames;
                    var frameRate = document.Timeline.FrameRate;
                    var projectType = document.ProjectType;
                    var grid = document.Layers.SpriteSheetGrid;
                    var (projectPath, infoPath) = PathsFor(document);
                    var info = $"{document.Title}\n{document.FilePath}";

                    await Task.Run(() =>
                    {
                        Directory.CreateDirectory(_folder);
                        using var copy = new Scene(document.Layers.Scene.Name);
                        copy.RestoreLayers(snapshot, activeIndex);
                        ProjectSerializer.Save(projectPath, copy, tracks, totalFrames, frameRate, projectType, grid);
                        File.WriteAllText(infoPath, info);
                    });

                    document.LastAutosavedChange = counter;
                }
            }
            catch (Exception)
            {
                // Autosave must never interrupt work; the next round tries again.
            }
            finally
            {
                _isWriting = false;
            }
        }

        /// <summary>Deletes <paramref name="document"/>'s recovery copy (after a save or close).</summary>
        public void Delete(DocumentViewModel document)
        {
            var (projectPath, infoPath) = PathsFor(document);
            TryDelete(projectPath);
            TryDelete(infoPath);
            document.LastAutosavedChange = -1;
        }

        /// <summary>Deletes every recovery copy (normal exit, or the user declined restoring).</summary>
        public void DeleteAll()
        {
            if (!Directory.Exists(_folder)) return;
            foreach (var file in Directory.GetFiles(_folder)) TryDelete(file);
        }

        /// <summary>Recovery copies left behind by a session that didn't exit normally.</summary>
        public IReadOnlyList<RecoveredDocument> FindLeftovers()
        {
            if (!Directory.Exists(_folder)) return Array.Empty<RecoveredDocument>();

            var found = new List<RecoveredDocument>();
            foreach (var project in Directory.GetFiles(_folder, "*.jolie"))
            {
                var infoPath = Path.ChangeExtension(project, ".info");
                var lines = File.Exists(infoPath) ? File.ReadAllLines(infoPath) : Array.Empty<string>();
                var title = lines.Length > 0 && lines[0].Length > 0 ? lines[0] : "Recovered";
                var original = lines.Length > 1 && lines[1].Length > 0 ? lines[1] : null;
                found.Add(new RecoveredDocument(project, title, original));
            }

            return found;
        }

        private (string Project, string Info) PathsFor(DocumentViewModel document)
        {
            var baseName = Path.Combine(_folder, document.RecoveryId.ToString("N"));
            return (baseName + ".jolie", baseName + ".info");
        }

        private static int IndexOfActive(Scene scene)
        {
            for (var i = 0; i < scene.Layers.Count; i++)
                if (ReferenceEquals(scene.Layers[i], scene.ActiveLayer)) return i;
            return -1;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
