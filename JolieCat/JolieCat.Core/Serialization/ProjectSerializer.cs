using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using JolieCat.Core.Documents;
using JolieCat.Shared.Enums;
using SkiaSharp;

namespace JolieCat.Core.Serialization
{
    /// <summary>
    /// Reads and writes the proprietary <c>.jolie</c> project format: a zip archive (the
    /// same "structured container" approach Office's own .docx/.pptx use) holding one PNG
    /// per layer under "layers/" plus a "manifest.json" describing the scene, each
    /// layer's metadata and which PNG belongs to it, and the timeline's tracks/clips/
    /// keyframes. PNG-per-layer (rather than a raw pixel dump) keeps the file both
    /// smaller and independently inspectable/recoverable; the zip container gives
    /// compression "for free" without a bespoke binary format to version.
    /// </summary>
    public static class ProjectSerializer
    {
        private const string ManifestEntryName = "manifest.json";

        public static void Save(
            string path,
            Scene scene,
            IReadOnlyList<TimelineTrackData> timelineTracks,
            double timelineTotalFrames,
            double timelineFrameRate,
            ProjectType projectType = ProjectType.StandardImage,
            Documents.SpriteSheetGrid? spriteSheetGrid = null)
        {
            ArgumentNullException.ThrowIfNull(scene);
            if (scene.Layers.Count == 0)
                throw new InvalidOperationException("Cannot save a scene with no layers.");

            var manifest = new ProjectManifest
            {
                SceneName = scene.Name,
                ProjectType = projectType.ToString(),
                SpriteSheetGrid = spriteSheetGrid is null
                    ? new SpriteSheetGridData()
                    : new SpriteSheetGridData
                    {
                        Columns = spriteSheetGrid.Columns,
                        Rows = spriteSheetGrid.Rows,
                        PaddingX = spriteSheetGrid.PaddingX,
                        PaddingY = spriteSheetGrid.PaddingY,
                        MarginX = spriteSheetGrid.MarginX,
                        MarginY = spriteSheetGrid.MarginY,
                    },
                DocumentWidth = scene.Layers[0].Bitmap.Width,
                DocumentHeight = scene.Layers[0].Bitmap.Height,
                ActiveLayerIndex = scene.ActiveLayer is null ? -1 : IndexOf(scene, scene.ActiveLayer),
                TimelineTotalFrames = timelineTotalFrames,
                TimelineFrameRate = timelineFrameRate,
                TimelineTracks = timelineTracks.ToList(),
            };

            // Every layer is expected to be exactly the document's size (the same
            // invariant the renderer and every painting/selection tool already assume -
            // see Scene.ResizeLayers/AddLayer) - verified here, before writing a single
            // byte, rather than silently trusting it. A layer that violated it would
            // still get written at its own (wrong) size, and Load would have no way to
            // tell that size was wrong versus intentional - exactly the kind of
            // inconsistency that reads as a layer's content having shifted or been
            // clipped on reopen.
            for (var i = 1; i < scene.Layers.Count; i++)
            {
                var layer = scene.Layers[i];
                if (layer.Bitmap.Width != manifest.DocumentWidth || layer.Bitmap.Height != manifest.DocumentHeight)
                    throw new InvalidOperationException(
                        $"Layer '{layer.Name}' is {layer.Bitmap.Width}x{layer.Bitmap.Height}, but the document is " +
                        $"{manifest.DocumentWidth}x{manifest.DocumentHeight} - every layer must match the document's " +
                        "size to save correctly.");
            }

            using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create);

            for (var i = 0; i < scene.Layers.Count; i++)
            {
                var layer = scene.Layers[i];
                var entryName = $"layers/{i}.png";
                var maskEntryName = layer.Mask is not null ? $"layers/{i}.mask.png" : null;

                manifest.Layers.Add(new LayerManifestEntry
                {
                    Name = layer.Name,
                    Type = layer.Type.ToString(),
                    IsVisible = layer.IsVisible,
                    IsLocked = layer.IsLocked,
                    Opacity = layer.Opacity,
                    BlendMode = layer.BlendMode.ToString(),
                    BitmapEntryName = entryName,
                    HasMask = layer.Mask is not null,
                    IsMaskEnabled = layer.Mask?.IsEnabled ?? true,
                    MaskEntryName = maskEntryName,
                });

                var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                using (var entryStream = entry.Open())
                    layer.Bitmap.Encode(entryStream, SKEncodedImageFormat.Png, 100);

                if (layer.Mask is { } mask)
                {
                    var maskEntry = archive.CreateEntry(maskEntryName!, CompressionLevel.Optimal);
                    using var maskEntryStream = maskEntry.Open();
                    mask.Bitmap.Encode(maskEntryStream, SKEncodedImageFormat.Png, 100);
                }
            }

            var manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
            using var manifestStream = manifestEntry.Open();
            JsonSerializer.Serialize(manifestStream, manifest, new JsonSerializerOptions { WriteIndented = true });
        }

        /// <summary>Runs <see cref="Save"/> on a background thread pool thread via
        /// <see cref="Task.Run(Action)"/>, so a caller on a UI thread (encoding every
        /// layer to PNG and writing the zip archive is genuinely CPU/IO-bound work, not
        /// instantaneous) can <c>await</c> it without blocking that thread - see
        /// <c>JolieCat.UI.ViewModels.MainViewModel.SaveProjectAsync</c>, which locks the
        /// UI for exactly this call's duration rather than the whole synchronous method
        /// running on the dispatcher thread.</summary>
        public static Task SaveAsync(
            string path,
            Scene scene,
            IReadOnlyList<TimelineTrackData> timelineTracks,
            double timelineTotalFrames,
            double timelineFrameRate,
            ProjectType projectType = ProjectType.StandardImage,
            Documents.SpriteSheetGrid? spriteSheetGrid = null) =>
            Task.Run(() => Save(path, scene, timelineTracks, timelineTotalFrames, timelineFrameRate, projectType, spriteSheetGrid));

        public static ProjectLoadResult Load(string path)
        {
            using var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read);
            using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read);

            var manifestEntry = archive.GetEntry(ManifestEntryName)
                ?? throw new InvalidDataException($"'{path}' is not a valid .jolie project - missing {ManifestEntryName}.");

            ProjectManifest manifest;
            using (var manifestStream = manifestEntry.Open())
            {
                manifest = JsonSerializer.Deserialize<ProjectManifest>(manifestStream)
                    ?? throw new InvalidDataException($"'{path}' is not a valid .jolie project - empty manifest.");
            }

            var scene = new Scene(string.IsNullOrWhiteSpace(manifest.SceneName) ? "Untitled Scene" : manifest.SceneName);

            foreach (var layerEntry in manifest.Layers)
            {
                var bitmapEntry = archive.GetEntry(layerEntry.BitmapEntryName)
                    ?? throw new InvalidDataException($"'{path}' is missing layer bitmap '{layerEntry.BitmapEntryName}'.");

                // Buffered into a seekable MemoryStream before decoding - a
                // ZipArchiveEntry's own stream is forward-only (CanSeek is false,
                // Length throws), and SKBitmap.Decode(Stream) on a non-seekable
                // source silently returns a bitmap of the correct size but with
                // every pixel zeroed for a large/high-entropy PNG (confirmed with a
                // real ZipArchive round-trip: a small, highly-compressible test
                // image decoded fine this way, but a ~450KB, 1853x1853 one came
                // back completely blank) - decoding from a plain seekable
                // MemoryStream or by file path both work correctly on the exact
                // same bytes, which is what pointed at the non-seekable stream
                // itself as the cause rather than anything about the PNG data or
                // the zip entry.
                using var bitmapStream = bitmapEntry.Open();
                using var bufferedStream = new MemoryStream();
                bitmapStream.CopyTo(bufferedStream);
                bufferedStream.Position = 0;
                using var decoded = SKBitmap.Decode(bufferedStream)
                    ?? throw new InvalidDataException($"'{path}' has an unreadable layer bitmap '{layerEntry.BitmapEntryName}'.");

                var type = Enum.TryParse<LayerType>(layerEntry.Type, out var parsedType) ? parsedType : LayerType.Raster;

                // Built at the manifest's own declared document size, not decoded.Width/
                // Height - every layer is supposed to already be exactly that size (see
                // Save's own check before writing), but trusting each PNG's own reported
                // size instead of the document's would let a mismatched layer (a
                // corrupted file, one hand-edited outside this app, or a project saved
                // by some future/older version that didn't enforce the invariant) come
                // back sized differently from the rest of the scene - which the renderer
                // and every painting tool assume never happens, and which reads as that
                // layer's content having shifted toward the top-left corner (the corner
                // every layer bitmap is always drawn from) or been clipped.
                var layer = new Layer(layerEntry.Name, manifest.DocumentWidth, manifest.DocumentHeight, type)
                {
                    IsVisible = layerEntry.IsVisible,
                    IsLocked = layerEntry.IsLocked,
                    Opacity = layerEntry.Opacity,
                    BlendMode = Enum.TryParse<BlendMode>(layerEntry.BlendMode, out var parsedBlend) ? parsedBlend : BlendMode.Normal,
                };

                if (decoded.Width == manifest.DocumentWidth && decoded.Height == manifest.DocumentHeight)
                    layer.Bitmap.Pixels = decoded.Pixels;
                else
                    layer.Canvas.DrawBitmap(decoded, 0, 0);

                if (layerEntry.HasMask && layerEntry.MaskEntryName is { } maskEntryName)
                {
                    var maskEntry = archive.GetEntry(maskEntryName)
                        ?? throw new InvalidDataException($"'{path}' is missing layer mask '{maskEntryName}'.");

                    // Same buffered-stream decode as the layer's own bitmap above - see
                    // that comment for why decoding straight from a ZipArchiveEntry's
                    // stream isn't safe for a large/high-entropy PNG.
                    using var maskStream = maskEntry.Open();
                    using var bufferedMaskStream = new MemoryStream();
                    maskStream.CopyTo(bufferedMaskStream);
                    bufferedMaskStream.Position = 0;
                    using var decodedMask = SKBitmap.Decode(bufferedMaskStream)
                        ?? throw new InvalidDataException($"'{path}' has an unreadable layer mask '{maskEntryName}'.");

                    var mask = layer.AddMask();
                    mask.IsEnabled = layerEntry.IsMaskEnabled;
                    if (decodedMask.Width == manifest.DocumentWidth && decodedMask.Height == manifest.DocumentHeight)
                        mask.Bitmap.Pixels = decodedMask.Pixels;
                    else
                        mask.Canvas.DrawBitmap(decodedMask, 0, 0);
                }

                scene.AddLayer(layer);
            }

            if (manifest.ActiveLayerIndex >= 0 && manifest.ActiveLayerIndex < scene.Layers.Count)
                scene.ActiveLayer = scene.Layers[manifest.ActiveLayerIndex];

            var projectType = Enum.TryParse<ProjectType>(manifest.ProjectType, out var parsedProjectType)
                ? parsedProjectType
                : ProjectType.StandardImage;

            var spriteSheetGrid = new Documents.SpriteSheetGrid
            {
                Columns = manifest.SpriteSheetGrid.Columns,
                Rows = manifest.SpriteSheetGrid.Rows,
                PaddingX = manifest.SpriteSheetGrid.PaddingX,
                PaddingY = manifest.SpriteSheetGrid.PaddingY,
                MarginX = manifest.SpriteSheetGrid.MarginX,
                MarginY = manifest.SpriteSheetGrid.MarginY,
            };

            return new ProjectLoadResult(scene, manifest.TimelineTracks, manifest.TimelineTotalFrames, manifest.TimelineFrameRate, projectType, spriteSheetGrid);
        }

        private static int IndexOf(Scene scene, Layer layer)
        {
            for (var i = 0; i < scene.Layers.Count; i++)
                if (ReferenceEquals(scene.Layers[i], layer)) return i;

            return -1;
        }
    }

    /// <summary>Everything <see cref="ProjectSerializer.Load"/> hands back - a freshly
    /// built <see cref="Scene"/> plus the timeline data a UI layer can use to rebuild its
    /// own timeline view models (kept as plain data here so this project needs no
    /// reference to <c>JolieCat.UI</c>'s timeline types).</summary>
    public sealed record ProjectLoadResult(
        Scene Scene,
        IReadOnlyList<TimelineTrackData> TimelineTracks,
        double TimelineTotalFrames,
        double TimelineFrameRate,
        ProjectType ProjectType,
        Documents.SpriteSheetGrid SpriteSheetGrid);
}
