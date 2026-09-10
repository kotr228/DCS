using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HelixToolkit.Wpf;

namespace JolieCat3D.Engine.Rendering
{
    /// <summary>
    /// Renders a <see cref="HelixViewport3D"/>'s current frame straight to an image file
    /// (<see cref="CaptureFrame"/>, <see cref="CaptureFrameAsync"/>), or a whole numbered
    /// sequence of them (<see cref="CaptureSequenceAsync"/>) - the actual "bake the 3D
    /// viewport out to pictures" step <c>JolieCat3D.UI</c>'s own animation-export menu
    /// item drives, one frame per call to a caller-supplied <c>onFrame</c> callback that
    /// moves the scene to that frame (typically <c>AnimationTimeline.CurrentFrame</c>'s
    /// setter, then <c>AnimationTimeline.Apply()</c>, then <see cref="Scene3DRenderer.Refresh"/> -
    /// all three live in <c>JolieCat3D.Service</c>/this same project, but neither is
    /// referenced here directly: this class only needs SOME state change to have
    /// happened before it captures the next frame, not what that state change actually
    /// is, keeping it just as usable for a hand-built preview scene with no
    /// <c>AnimationTimeline</c> involved at all).
    ///
    /// A WPF <c>Visual</c> (a <see cref="HelixViewport3D"/>'s own <c>Viewport3D</c>
    /// included) can only ever be rendered by the thread that owns it - every method
    /// here must therefore be called (and, for the async ones, awaited onward from)
    /// that same UI/dispatcher thread; <see cref="CaptureSequenceAsync"/>'s own periodic
    /// <see cref="Dispatcher.Yield(DispatcherPriority)"/> is what keeps that thread from
    /// ever being pegged for the sequence's whole duration despite every render staying
    /// on it.
    /// </summary>
    public static class ViewportCaptureService
    {
        /// <summary>The default oversampling multiplier every capture method here uses
        /// when a caller doesn't ask for a specific one - renders at 2x the viewport's
        /// own on-screen pixel dimensions before downsampling into the saved file
        /// (<see cref="Viewport3DHelper.SaveBitmap"/>'s/<see cref="Viewport3DHelper.RenderBitmap"/>'s
        /// own "m" parameter), a cheap, standard supersampling anti-aliasing trick that's
        /// also what makes this "high-res" rather than merely "a screenshot" - a
        /// captured frame is always sharper than the interactive viewport itself, not
        /// just a copy of its exact current pixels.</summary>
        public const int DefaultOversamplingMultiplier = 2;

        /// <summary>Renders <paramref name="viewport"/>'s CURRENT content (whatever the
        /// scene looked like the moment this is called - this method itself never
        /// advances or re-renders anything) to <paramref name="outputPath"/>, entirely
        /// synchronously on the calling thread (which - see this class's own remarks -
        /// must be <paramref name="viewport"/>'s own dispatcher thread). A single one-off
        /// capture (a "Save Viewport Image" button, say) is squarely what this is for;
        /// an animation SEQUENCE should use <see cref="CaptureSequenceAsync"/> instead,
        /// which keeps the same UI thread responsive across many frames the way one
        /// synchronous call here never could.</summary>
        public static void CaptureFrame(HelixViewport3D viewport, string outputPath, int oversamplingMultiplier = DefaultOversamplingMultiplier, Brush? background = null)
        {
            ArgumentNullException.ThrowIfNull(viewport);
            ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            Viewport3DHelper.SaveBitmap(
                viewport.Viewport,
                outputPath,
                background ?? Brushes.Transparent,
                Math.Max(1, oversamplingMultiplier),
                GetOutputFormat(outputPath));
        }

        /// <summary>The async counterpart to <see cref="CaptureFrame"/>, and the one
        /// <see cref="CaptureSequenceAsync"/> itself calls per frame: renders
        /// <paramref name="viewport"/>'s current content to a <see cref="RenderTargetBitmap"/>
        /// (this half MUST stay on the calling/UI thread - a <c>Viewport3D</c> is
        /// thread-affine, the same reason <see cref="CaptureFrame"/> is fully
        /// synchronous), then hands that (frozen, so cross-thread-safe) bitmap to a
        /// background thread to actually encode and write to disk - the one part of a
        /// single capture that can be genuinely slow (a large
        /// <paramref name="oversamplingMultiplier"/>, a slow disk) and has no need
        /// whatsoever to run on the UI thread, so it doesn't. Awaiting this from the UI
        /// thread frees it to keep pumping input/paint/etc. for as long as the
        /// encode+write takes, resuming back on it afterward (<c>ConfigureAwait(true)</c>)
        /// since the NEXT frame's own render step, if this is mid-sequence, needs to be
        /// back on that same thread too.</summary>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/>
        /// was already cancelled, or was cancelled while the background encode/write was
        /// in flight.</exception>
        public static async Task<string> CaptureFrameAsync(
            HelixViewport3D viewport,
            string outputPath,
            int oversamplingMultiplier = DefaultOversamplingMultiplier,
            Brush? background = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(viewport);
            ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
            cancellationToken.ThrowIfCancellationRequested();

            // MUST be frozen before it's ever touched off this thread (see this method's
            // own remarks) - a RenderTargetBitmap always CAN freeze once rendered, so
            // this is not a conditional/best-effort step: if it somehow couldn't, letting
            // Freeze() itself throw here (on the UI thread, with a clear message) is far
            // more diagnosable than the confusing cross-thread-access exception that would
            // otherwise surface later, deep inside the background encode step.
            var bitmap = Viewport3DHelper.RenderBitmap(viewport.Viewport, background ?? Brushes.Transparent, Math.Max(1, oversamplingMultiplier));
            bitmap.Freeze();

            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var format = GetOutputFormat(outputPath);

            await Task.Run(() =>
            {
                var encoder = CreateEncoder(format);
                encoder.Frames.Add(BitmapFrame.Create(bitmap));

                using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
                encoder.Save(stream);
            }, cancellationToken).ConfigureAwait(true);

            return outputPath;
        }

        /// <summary>Captures <paramref name="frameCount"/> frames as one numbered image
        /// sequence in <paramref name="outputDirectory"/> - <c>"{baseFileName}_{index:D4}.{extension}"</c>
        /// each, a zero-padded, sortable-by-plain-filename naming convention any video-
        /// editing or compositing tool (and <c>Interop.ClipbarReader</c>'s own "Frame
        /// NNN" scheme, for that matter) already expects. For each frame in turn: calls
        /// <paramref name="onFrame"/> with that frame's own index (0-based) so the caller
        /// can move the scene there (see this class's own remarks) and force whatever
        /// viewport refresh it needs, THEN captures it via <see cref="CaptureFrameAsync"/>
        /// - the callback always runs immediately before its own frame's capture, never
        /// batched ahead of time, so a callback that mutates shared state (like
        /// advancing a live <c>AnimationTimeline</c>) behaves exactly as if a person were
        /// scrubbing to each frame and saving it by hand, just automated.
        ///
        /// Never freezes the UI thread across the WHOLE sequence: each frame's own
        /// encode+write already runs off it (see <see cref="CaptureFrameAsync"/>), and
        /// the explicit <see cref="Dispatcher.Yield(DispatcherPriority)"/> after every
        /// frame additionally lets any input/paint/etc. queued at
        /// <see cref="DispatcherPriority.Background"/> or higher run before the next
        /// frame starts, even for a scene cheap enough that the encode+write step alone
        /// wouldn't have left much of a gap. <paramref name="cancellationToken"/> is
        /// checked once per frame (not mid-frame) - cancelling stops before starting the
        /// next frame's own <paramref name="onFrame"/> call, leaving every frame already
        /// written on disk exactly as captured, never a half-written file. Returns every
        /// frame's own written path, in order (only the ones captured before a
        /// cancellation, if any) - empty (no work done at all, not even directory
        /// creation) for <paramref name="frameCount"/> &lt;= 0.</summary>
        public static async Task<IReadOnlyList<string>> CaptureSequenceAsync(
            HelixViewport3D viewport,
            int frameCount,
            Action<int> onFrame,
            string outputDirectory,
            string baseFileName,
            int oversamplingMultiplier = DefaultOversamplingMultiplier,
            Brush? background = null,
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

            for (var i = 0; i < frameCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                onFrame(i);

                var path = Path.Combine(outputDirectory, $"{baseFileName}_{i:D4}.{trimmedExtension}");
                await CaptureFrameAsync(viewport, path, oversamplingMultiplier, background, cancellationToken).ConfigureAwait(true);
                paths.Add(path);

                progress?.Report(i + 1);

                await Dispatcher.Yield(DispatcherPriority.Background);
            }

            return paths;
        }

        /// <summary>The <see cref="BitmapExporter.OutputFormat"/> matching
        /// <paramref name="path"/>'s own extension - Png unless it's specifically ".jpg"/
        /// ".jpeg" or ".bmp", per <see cref="CaptureFrame"/>'s own remarks on why Png is
        /// the safe fallback.</summary>
        private static BitmapExporter.OutputFormat GetOutputFormat(string path) =>
            Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => BitmapExporter.OutputFormat.Jpg,
                ".bmp" => BitmapExporter.OutputFormat.Bmp,
                _ => BitmapExporter.OutputFormat.Png,
            };

        /// <summary>A fresh <see cref="BitmapEncoder"/> matching <paramref name="format"/> -
        /// <see cref="CaptureFrameAsync"/>'s own equivalent of
        /// <see cref="Viewport3DHelper.SaveBitmap"/>'s internal format dispatch, needed
        /// here because the async path encodes by hand (see
        /// <see cref="CaptureFrameAsync"/>'s own remarks on why) rather than delegating
        /// the whole render+encode+write to that HelixToolkit helper the way the
        /// synchronous <see cref="CaptureFrame"/> still does.</summary>
        private static BitmapEncoder CreateEncoder(BitmapExporter.OutputFormat format) => format switch
        {
            BitmapExporter.OutputFormat.Jpg => new JpegBitmapEncoder(),
            BitmapExporter.OutputFormat.Bmp => new BmpBitmapEncoder(),
            _ => new PngBitmapEncoder(),
        };
    }
}
