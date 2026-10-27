using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Actions;
using JolieCat.Core.Documents;
using JolieCat.Core.Rendering;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    public enum BatchFormat
    {
        Png,
        Jpeg,
        WebP,
    }

    /// <summary>
    /// File > Automate > Batch: plays an action on every image in a folder and saves
    /// the results to another folder. Files are processed off-screen - no tabs open -
    /// one at a time, so the window stays responsive and Cancel works between files.
    /// </summary>
    public partial class BatchViewModel : ObservableObject
    {
        public static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif" };

        private CancellationTokenSource? _cancel;

        public BatchViewModel(IEnumerable<RecordedAction> actions, RecordedAction? selected = null)
        {
            foreach (var action in actions) Actions.Add(action);
            selectedAction = selected ?? Actions.FirstOrDefault();
        }

        public ObservableCollection<RecordedAction> Actions { get; } = new();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunCommand))]
        private RecordedAction? selectedAction;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunCommand))]
        private string sourceFolder = string.Empty;

        [ObservableProperty]
        private bool includeSubfolders;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunCommand))]
        private string outputFolder = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(UsesQuality))]
        private BatchFormat format = BatchFormat.Jpeg;

        [ObservableProperty]
        private double quality = 90;

        /// <summary>Added to each file name, e.g. "_edited".</summary>
        [ObservableProperty]
        private string suffix = string.Empty;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunCommand), nameof(CancelRunCommand))]
        private bool isRunning;

        [ObservableProperty]
        private double progress;

        [ObservableProperty]
        private string status = "Choose an action and the folders, then press Run.";

        public bool UsesQuality => Format != BatchFormat.Png;

        public IReadOnlyList<BatchFormat> Formats { get; } = Enum.GetValues<BatchFormat>();

        /// <summary>Plays one step on a document (MainViewModel's player).</summary>
        public Func<ActionStep, DocumentViewModel, bool> Player { get; set; } = MainViewModel.PlayActionStep;

        public List<string> FindFiles() =>
            Directory.Exists(SourceFolder)
                ? Directory.EnumerateFiles(SourceFolder, "*", IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                    .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : new List<string>();

        private bool CanRun() => !IsRunning && SelectedAction is not null
            && !string.IsNullOrWhiteSpace(SourceFolder) && !string.IsNullOrWhiteSpace(OutputFolder);

        [RelayCommand(CanExecute = nameof(CanRun))]
        private async Task RunAsync()
        {
            if (SelectedAction is null) return;
            if (!Directory.Exists(SourceFolder)) { Status = "The source folder doesn't exist."; return; }
            if (SamePath(SourceFolder, OutputFolder) && string.IsNullOrEmpty(Suffix))
            {
                Status = "The output folder is the source folder: add a file name suffix so the originals aren't overwritten.";
                return;
            }

            var files = FindFiles();
            if (files.Count == 0) { Status = "No images (PNG, JPEG, WebP, BMP, GIF) in the source folder."; return; }

            IsRunning = true;
            _cancel = new CancellationTokenSource();
            int done = 0, failed = 0;
            var firstError = string.Empty;
            try
            {
                for (var i = 0; i < files.Count; i++)
                {
                    if (_cancel.IsCancellationRequested) break;
                    Status = $"{i + 1} / {files.Count}: {Path.GetFileName(files[i])}";
                    Progress = (double)i / files.Count;
                    // Let the window repaint (and Cancel be clicked) between files.
                    await Dispatcher.Yield(DispatcherPriority.Background);

                    var error = ProcessFile(files[i]);
                    if (error is null) done++;
                    else
                    {
                        failed++;
                        if (firstError.Length == 0) firstError = $"{Path.GetFileName(files[i])}: {error}";
                    }
                }
            }
            finally
            {
                IsRunning = false;
                Progress = 1;
            }

            Status = (_cancel.IsCancellationRequested ? "Cancelled. " : string.Empty)
                + $"Saved {done} file(s) to {OutputFolder}."
                + (failed > 0 ? $" {failed} failed - {firstError}" : string.Empty);
        }

        [RelayCommand(CanExecute = nameof(IsRunning))]
        private void CancelRun() => _cancel?.Cancel();

        /// <summary>Opens <paramref name="path"/> off-screen, plays the action, saves the
        /// flattened result. Returns null on success, else what went wrong.</summary>
        public string? ProcessFile(string path)
        {
            if (SelectedAction is null) return "No action selected.";
            try
            {
                using var decoded = SKBitmap.Decode(path);
                if (decoded is null) return "not an image JolieCat can read";

                var document = new DocumentViewModel(new ToolboxViewModel(), Path.GetFileNameWithoutExtension(path),
                    Shared.Enums.ProjectType.StandardImage, decoded.Width, decoded.Height, null);
                document.Layers.Scene.Layers[0].Canvas.DrawBitmap(decoded, 0, 0);

                foreach (var step in SelectedAction.Steps)
                    if (!Player(step, document))
                        return $"step \"{step.Title}\" couldn't run";

                var width = document.Layers.DocumentWidth;
                var height = document.Layers.DocumentHeight;
                using var flat = SceneCompositor.Flatten(document.Layers.Scene, width, height);
                Directory.CreateDirectory(OutputFolder);
                File.WriteAllBytes(OutputPathFor(path), Encode(flat));
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                return ex.Message;
            }
        }

        /// <summary>Where the result for <paramref name="source"/> is saved. Files from
        /// subfolders keep their relative folder under the output folder.</summary>
        public string OutputPathFor(string source)
        {
            var relativeFolder = Path.GetDirectoryName(Path.GetRelativePath(SourceFolder, source)) ?? string.Empty;
            var extension = Format switch { BatchFormat.Jpeg => ".jpg", BatchFormat.WebP => ".webp", _ => ".png" };
            var folder = Path.Combine(OutputFolder, relativeFolder);
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, Path.GetFileNameWithoutExtension(source) + Suffix + extension);
        }

        private byte[] Encode(SKBitmap bitmap)
        {
            SKBitmap? opaque = null;
            try
            {
                var toEncode = bitmap;
                if (Format == BatchFormat.Jpeg)
                {
                    // JPEG has no transparency: put it on white, like Photoshop's Save for Web.
                    opaque = new SKBitmap(bitmap.Width, bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
                    using var canvas = new SKCanvas(opaque);
                    canvas.Clear(SKColors.White);
                    canvas.DrawBitmap(bitmap, 0, 0);
                    toEncode = opaque;
                }

                using var image = SKImage.FromBitmap(toEncode);
                var skFormat = Format switch { BatchFormat.Jpeg => SKEncodedImageFormat.Jpeg, BatchFormat.WebP => SKEncodedImageFormat.Webp, _ => SKEncodedImageFormat.Png };
                using var data = image.Encode(skFormat, (int)Math.Clamp(Quality, 1, 100));
                return data.ToArray();
            }
            finally
            {
                opaque?.Dispose();
            }
        }

        private static bool SamePath(string a, string b)
        {
            try
            {
                return string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}
