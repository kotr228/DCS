using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
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
            double timelineFrameRate)
        {
            ArgumentNullException.ThrowIfNull(scene);
            if (scene.Layers.Count == 0)
                throw new InvalidOperationException("Cannot save a scene with no layers.");

            var manifest = new ProjectManifest
            {
                SceneName = scene.Name,
                DocumentWidth = scene.Layers[0].Bitmap.Width,
                DocumentHeight = scene.Layers[0].Bitmap.Height,
                ActiveLayerIndex = scene.ActiveLayer is null ? -1 : IndexOf(scene, scene.ActiveLayer),
                TimelineTotalFrames = timelineTotalFrames,
                TimelineFrameRate = timelineFrameRate,
                TimelineTracks = timelineTracks.ToList(),
            };

            using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create);

            for (var i = 0; i < scene.Layers.Count; i++)
            {
                var layer = scene.Layers[i];
                var entryName = $"layers/{i}.png";

                manifest.Layers.Add(new LayerManifestEntry
                {
                    Name = layer.Name,
                    Type = layer.Type.ToString(),
                    IsVisible = layer.IsVisible,
                    IsLocked = layer.IsLocked,
                    Opacity = layer.Opacity,
                    BlendMode = layer.BlendMode.ToString(),
                    BitmapEntryName = entryName,
                });

                var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                layer.Bitmap.Encode(entryStream, SKEncodedImageFormat.Png, 100);
            }

            var manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
            using var manifestStream = manifestEntry.Open();
            JsonSerializer.Serialize(manifestStream, manifest, new JsonSerializerOptions { WriteIndented = true });
        }

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

                using var bitmapStream = bitmapEntry.Open();
                using var decoded = SKBitmap.Decode(bitmapStream)
                    ?? throw new InvalidDataException($"'{path}' has an unreadable layer bitmap '{layerEntry.BitmapEntryName}'.");

                var type = Enum.TryParse<LayerType>(layerEntry.Type, out var parsedType) ? parsedType : LayerType.Raster;
                var layer = new Layer(layerEntry.Name, decoded.Width, decoded.Height, type)
                {
                    IsVisible = layerEntry.IsVisible,
                    IsLocked = layerEntry.IsLocked,
                    Opacity = layerEntry.Opacity,
                    BlendMode = Enum.TryParse<BlendMode>(layerEntry.BlendMode, out var parsedBlend) ? parsedBlend : BlendMode.Normal,
                };
                layer.Bitmap.Pixels = decoded.Pixels;

                scene.AddLayer(layer);
            }

            if (manifest.ActiveLayerIndex >= 0 && manifest.ActiveLayerIndex < scene.Layers.Count)
                scene.ActiveLayer = scene.Layers[manifest.ActiveLayerIndex];

            return new ProjectLoadResult(scene, manifest.TimelineTracks, manifest.TimelineTotalFrames, manifest.TimelineFrameRate);
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
        double TimelineFrameRate);
}
