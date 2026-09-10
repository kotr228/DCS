using System.IO.Compression;
using System.Text.Json;

namespace JolieCat3D.Service.Interop
{
    /// <summary>
    /// Reads a <c>.jolie</c> project file - the same zip-container-plus-"manifest.json"
    /// format <c>JolieCat.Core.Serialization.ProjectSerializer</c> writes (a PNG per
    /// layer under "layers/", named by <see cref="JolieLayerManifestEntry.BitmapEntryName"/>) -
    /// well enough to pull one layer's own bitmap out as a real, standalone PNG file
    /// ready for <see cref="TwoDAssetBridge.LoadTextureMaterial"/> (or a
    /// <see cref="Core.Materials.Material.DiffuseTexturePath"/> assignment directly).
    ///
    /// This is an INDEPENDENT reimplementation, not a shared type or a project
    /// reference into the separate <c>JolieCat</c> 2D solution (both solutions happen
    /// to live in the same repository, but the established architecture - see this
    /// namespace's other classes' own remarks - deliberately keeps `JolieCat3D` from
    /// depending on `JolieCat`'s own assemblies, the same "protocol compatibility, not
    /// code sharing" reasoning any file-format importer already applies to a
    /// completely external tool's own files). Layer PIXELS are extracted as raw PNG
    /// bytes, never decoded - <c>JolieCat3D.Core</c>/<c>JolieCat3D.Service</c> have no
    /// image codec of their own (see <c>TwoDAssetBridge</c>'s own remarks), and none is
    /// needed here: a zip entry's bytes for a layer are already a complete, valid PNG
    /// file on their own, so "extracting a layer's texture" is just copying bytes to a
    /// new file, not decoding/re-encoding pixels.
    /// </summary>
    public static class JolieProjectReader
    {
        private const string ManifestEntryName = "manifest.json";

        /// <summary>Reads and deserializes just the "manifest.json" entry - the scene
        /// name, document size, sprite-sheet grid, and every layer's own metadata (but
        /// not any layer's pixels - see <see cref="ExtractLayerBitmap"/> for those).</summary>
        /// <exception cref="InvalidDataException"><paramref name="jolieFilePath"/> isn't
        /// a valid <c>.jolie</c> project (not a zip file at all, or missing/unreadable
        /// "manifest.json").</exception>
        public static JolieProjectManifestInfo ReadManifest(string jolieFilePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(jolieFilePath);

            using var fileStream = new FileStream(jolieFilePath, FileMode.Open, FileAccess.Read);
            using var archive = OpenArchive(fileStream, jolieFilePath);

            var manifestEntry = archive.GetEntry(ManifestEntryName)
                ?? throw new InvalidDataException($"'{jolieFilePath}' is not a valid .jolie project - missing {ManifestEntryName}.");

            using var manifestStream = manifestEntry.Open();
            return JsonSerializer.Deserialize<JolieProjectManifestInfo>(manifestStream)
                ?? throw new InvalidDataException($"'{jolieFilePath}' is not a valid .jolie project - empty manifest.");
        }

        /// <summary>Extracts one layer's own bitmap (by its manifest
        /// <see cref="JolieLayerManifestEntry.BitmapEntryName"/>, e.g. "layers/0.png")
        /// to <paramref name="outputPngPath"/> - overwriting it if it already exists (so
        /// re-extracting the SAME output path after the source <c>.jolie</c> file
        /// changes, per <see cref="JolieWorkspaceWatcher"/>'s own live-refresh use case,
        /// just works). A plain byte copy, not a decode/re-encode - see this class's own
        /// remarks.</summary>
        /// <exception cref="InvalidDataException"><paramref name="bitmapEntryName"/>
        /// isn't an entry in <paramref name="jolieFilePath"/>.</exception>
        public static void ExtractLayerBitmap(string jolieFilePath, string bitmapEntryName, string outputPngPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(jolieFilePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(bitmapEntryName);
            ArgumentException.ThrowIfNullOrWhiteSpace(outputPngPath);

            using var fileStream = new FileStream(jolieFilePath, FileMode.Open, FileAccess.Read);
            using var archive = OpenArchive(fileStream, jolieFilePath);

            var bitmapEntry = archive.GetEntry(bitmapEntryName)
                ?? throw new InvalidDataException($"'{jolieFilePath}' is missing layer bitmap '{bitmapEntryName}'.");

            var outputDirectory = Path.GetDirectoryName(outputPngPath);
            if (!string.IsNullOrEmpty(outputDirectory)) Directory.CreateDirectory(outputDirectory);

            using var entryStream = bitmapEntry.Open();
            using var outputStream = new FileStream(outputPngPath, FileMode.Create, FileAccess.Write);
            entryStream.CopyTo(outputStream);
        }

        /// <summary>Extracts the manifest's own <see cref="JolieProjectManifestInfo.ActiveLayerIndex"/>
        /// layer (falling back to the LAST layer - typically the most "finished-looking"
        /// one for a simple flattenable image, being drawn on top of everything below it -
        /// if the active index is invalid or the project has no layers at all, in which
        /// case an <see cref="InvalidDataException"/> is thrown instead) to a
        /// deterministic file inside <paramref name="outputDirectory"/>, named after the
        /// project file itself (so re-extracting the same <c>.jolie</c> path always
        /// overwrites the same output file - what makes live refresh via
        /// <see cref="JolieWorkspaceWatcher"/> work with no extra bookkeeping of its own).
        /// Returns the extracted file's own path.</summary>
        public static string ExtractActiveLayerTexture(string jolieFilePath, string outputDirectory)
        {
            var manifest = ReadManifest(jolieFilePath);
            if (manifest.Layers.Count == 0)
                throw new InvalidDataException($"'{jolieFilePath}' has no layers to use as a texture.");

            var layerIndex = manifest.ActiveLayerIndex >= 0 && manifest.ActiveLayerIndex < manifest.Layers.Count
                ? manifest.ActiveLayerIndex
                : manifest.Layers.Count - 1;

            return ExtractLayerTexture(jolieFilePath, manifest, layerIndex, outputDirectory);
        }

        /// <summary>Extracts one specific layer, found by name (case-insensitive) - null
        /// if no layer in the project has that name.</summary>
        public static string? ExtractLayerTextureByName(string jolieFilePath, string layerName, string outputDirectory)
        {
            var manifest = ReadManifest(jolieFilePath);
            var layerIndex = manifest.Layers.FindIndex(layer => string.Equals(layer.Name, layerName, StringComparison.OrdinalIgnoreCase));

            return layerIndex < 0 ? null : ExtractLayerTexture(jolieFilePath, manifest, layerIndex, outputDirectory);
        }

        private static string ExtractLayerTexture(string jolieFilePath, JolieProjectManifestInfo manifest, int layerIndex, string outputDirectory)
        {
            var layer = manifest.Layers[layerIndex];
            var outputFileName = $"{Path.GetFileNameWithoutExtension(jolieFilePath)}.{SanitizeForFileName(layer.Name)}.png";
            var outputPath = Path.Combine(outputDirectory, outputFileName);

            ExtractLayerBitmap(jolieFilePath, layer.BitmapEntryName, outputPath);
            return outputPath;
        }

        /// <summary>Converts the manifest's own literal
        /// <see cref="JolieSpriteSheetGridInfo"/> into this project's own
        /// <see cref="SpriteSheetGridInfo"/>, ready for
        /// <see cref="TwoDAssetBridge.GetCellRect"/> - only meaningful when
        /// <see cref="JolieProjectManifestInfo.ProjectType"/> is "SpriteSheet", the same
        /// way the source manifest's own grid field is only meaningful under the same
        /// condition (see <c>ProjectManifest.SpriteSheetGrid</c>'s own remarks).</summary>
        public static SpriteSheetGridInfo ToSpriteSheetGridInfo(JolieSpriteSheetGridInfo grid) => new(
            Columns: grid.Columns,
            Rows: grid.Rows,
            MarginX: grid.MarginX,
            MarginY: grid.MarginY,
            PaddingX: grid.PaddingX,
            PaddingY: grid.PaddingY);

        private static ZipArchive OpenArchive(FileStream fileStream, string jolieFilePath)
        {
            try
            {
                return new ZipArchive(fileStream, ZipArchiveMode.Read);
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidDataException($"'{jolieFilePath}' is not a valid .jolie project (not a readable zip archive).", ex);
            }
        }

        /// <summary>A layer name almost certainly contains characters a file name can't
        /// (at minimum, JolieCat.UI lets a user name a layer anything at all, spaces and
        /// punctuation included) - replaces every character that isn't a letter, digit,
        /// space, hyphen, or underscore with an underscore, so the resulting file name is
        /// always valid on every platform this project targets.</summary>
        private static string SanitizeForFileName(string name)
        {
            var chars = name.Select(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' ? c : '_').ToArray();
            var sanitized = new string(chars).Trim();
            return sanitized.Length == 0 ? "layer" : sanitized;
        }
    }
}
