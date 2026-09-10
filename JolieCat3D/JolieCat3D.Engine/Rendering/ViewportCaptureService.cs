using System.IO;
using System.Windows.Media;
using HelixToolkit.Wpf;

namespace JolieCat3D.Engine.Rendering
{
    /// <summary>
    /// Renders a <see cref="HelixViewport3D"/>'s current frame straight to an image file
    /// (<see cref="CaptureFrame"/>), or a whole numbered sequence of them
    /// (<see cref="CaptureSequence"/>) - the actual "bake the 3D viewport out to
    /// pictures" step <c>JolieCat3D.UI</c>'s own animation-export menu item drives, one
    /// frame per call to a caller-supplied <c>onFrame</c> callback that moves the scene
    /// to that frame (typically <c>AnimationTimeline.CurrentFrame</c>'s setter, then
    /// <c>AnimationTimeline.Apply()</c>, then <see cref="Scene3DRenderer.Refresh"/> - all
    /// three live in <c>JolieCat3D.Service</c>/this same project, but neither is
    /// referenced here directly: this class only needs SOME state change to have
    /// happened before it captures the next frame, not what that state change actually
    /// is, keeping it just as usable for a hand-built preview scene with no
    /// <c>AnimationTimeline</c> involved at all).
    ///
    /// Built entirely on <see cref="Viewport3DHelper.SaveBitmap"/> - HelixToolkit.Wpf's
    /// own bitmap exporter (see <c>BitmapExporter</c>, which <c>SaveBitmap</c> uses
    /// internally) - rather than a hand-rolled <c>RenderTargetBitmap</c> capture, so the
    /// exact same rendering path (lights, materials, anti-aliasing) that already draws
    /// the interactive viewport is what gets captured, with no separate "offscreen
    /// render" code of this project's own to keep in sync with it.
    /// </summary>
    public static class ViewportCaptureService
    {
        /// <summary>The default oversampling multiplier <see cref="CaptureFrame"/>/
        /// <see cref="CaptureSequence"/> use when a caller doesn't ask for a specific
        /// one - renders at 2x the viewport's own on-screen pixel dimensions before
        /// downsampling into the saved file (<see cref="Viewport3DHelper.SaveBitmap"/>'s
        /// own "m" parameter), a cheap, standard supersampling anti-aliasing trick that's
        /// also what makes this "high-res" rather than merely "a screenshot" - a
        /// captured frame is always sharper than the interactive viewport itself, not
        /// just a copy of its exact current pixels.</summary>
        public const int DefaultOversamplingMultiplier = 2;

        /// <summary>Renders <paramref name="viewport"/>'s CURRENT content (whatever the
        /// scene looked like the moment this is called - this method itself never
        /// advances or re-renders anything) to <paramref name="outputPath"/>, in
        /// whichever of Png/Jpg/Bmp its own extension names (falling back to Png for any
        /// other/missing extension, the safest default - a lossless format never loses
        /// data a caller might have expected to keep). A transparent background (rather
        /// than opaque black, <see cref="HelixViewport3D"/>'s own WPF default) unless
        /// <paramref name="background"/> says otherwise - a captured 3D frame is as
        /// likely to be composited over something else afterward (e.g. brought into
        /// JolieCat 2D as a layer) as it is to be viewed standalone, and a transparent
        /// PNG loses nothing for the standalone case while an opaque one would actively
        /// ruin the compositing one.</summary>
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

        /// <summary>Captures <paramref name="frameCount"/> frames as one numbered image
        /// sequence in <paramref name="outputDirectory"/> - <c>"{baseFileName}_{index:D4}.{extension}"</c>
        /// each, a zero-padded, sortable-by-plain-filename naming convention any video-
        /// editing or compositing tool (and <c>Interop.ClipbarReader</c>'s own "Frame
        /// NNN" scheme, for that matter) already expects. For each frame in turn: calls
        /// <paramref name="onFrame"/> with that frame's own index (0-based) so the caller
        /// can move the scene there (see this class's own remarks), THEN captures it via
        /// <see cref="CaptureFrame"/> - the callback always runs immediately before its
        /// own frame's capture, never batched ahead of time, so a callback that mutates
        /// shared state (like advancing a live <c>AnimationTimeline</c>) behaves exactly
        /// as if a person were scrubbing to each frame and saving it by hand, just
        /// automated. Returns every frame's own written path, in order - empty (no work
        /// done at all, not even directory creation) for <paramref name="frameCount"/>
        /// &lt;= 0.</summary>
        public static IReadOnlyList<string> CaptureSequence(
            HelixViewport3D viewport,
            int frameCount,
            Action<int> onFrame,
            string outputDirectory,
            string baseFileName,
            int oversamplingMultiplier = DefaultOversamplingMultiplier,
            Brush? background = null,
            string extension = "png")
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
                onFrame(i);

                var path = Path.Combine(outputDirectory, $"{baseFileName}_{i:D4}.{trimmedExtension}");
                CaptureFrame(viewport, path, oversamplingMultiplier, background);
                paths.Add(path);
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
    }
}
