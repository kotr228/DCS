using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace JolieCat3D.Engine.Viewport
{
    /// <summary>Saves viewport renders (single frames or animation sequences) to image files.</summary>
    public static class ViewportCaptureService
    {
        /// <summary>Renders at this multiple of the viewport's pixel size (sharper output).</summary>
        public const int DefaultOversamplingMultiplier = 2;

        public static void CaptureFrame(EditorViewport viewport, string outputPath, int oversamplingMultiplier = DefaultOversamplingMultiplier)
        {
            ArgumentNullException.ThrowIfNull(viewport);
            using var renderer = viewport.CreateCaptureRenderer();
            Save(Capture(viewport, renderer, oversamplingMultiplier), outputPath);
        }

        /// <summary>For each frame: <paramref name="onFrame"/> poses the scene, then the view is
        /// rendered and saved as <c>{baseFileName}_0000.png</c>, ... Yields to the dispatcher
        /// between frames so the window stays responsive.</summary>
        public static async Task<IReadOnlyList<string>> CaptureSequenceAsync(
            EditorViewport viewport,
            int frameCount,
            Action<int> onFrame,
            string outputDirectory,
            string baseFileName,
            int oversamplingMultiplier = DefaultOversamplingMultiplier,
            string extension = "png",
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(viewport);
            ArgumentNullException.ThrowIfNull(onFrame);
            ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
            ArgumentException.ThrowIfNullOrWhiteSpace(baseFileName);
            if (frameCount <= 0) return Array.Empty<string>();

            Directory.CreateDirectory(outputDirectory);
            var paths = new List<string>(frameCount);
            var trimmedExtension = extension.TrimStart('.');

            using var renderer = viewport.CreateCaptureRenderer();
            for (var i = 0; i < frameCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                onFrame(i);

                var path = Path.Combine(outputDirectory, $"{baseFileName}_{i:D4}.{trimmedExtension}");
                Save(Capture(viewport, renderer, oversamplingMultiplier), path);
                paths.Add(path);
                progress?.Report(i + 1);

                await Dispatcher.Yield(DispatcherPriority.Background);
            }

            return paths;
        }

        private static BitmapSource Capture(EditorViewport viewport, OffscreenRenderer renderer, int oversampling)
        {
            var (w, h) = viewport.PixelSize;
            oversampling = Math.Clamp(oversampling, 1, 4);
            return viewport.CaptureFrame(renderer, w * oversampling, h * oversampling);
        }

        private static void Save(BitmapSource bitmap, string path)
        {
            BitmapEncoder encoder = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 95 },
                ".bmp" => new BmpBitmapEncoder(),
                _ => new PngBitmapEncoder(),
            };
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            using var stream = File.Create(path);
            encoder.Save(stream);
        }
    }
}
