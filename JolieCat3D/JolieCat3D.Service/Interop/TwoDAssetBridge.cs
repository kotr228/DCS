using System.Numerics;
using System.Text.RegularExpressions;
using JolieCat3D.Core.Materials;

namespace JolieCat3D.Service.Interop
{
    /// <summary>
    /// Loads a texture, sprite-sheet cell, or clipbar (numbered frame-sequence) image -
    /// any of the asset shapes <c>JolieCat</c>'s own 2D editor workspace exports -
    /// directly into a <see cref="Material"/>'s texture slot
    /// (<see cref="Material.DiffuseTexturePath"/>/<see cref="Material.DiffuseTextureOffset"/>/
    /// <see cref="Material.DiffuseTextureScale"/>), with no project reference into the
    /// separate JolieCat 2D solution at all - this recognizes that project's own
    /// exported file-naming and grid-layout conventions independently (see
    /// <see cref="SpriteSheetGridInfo"/>'s own remarks), the same "protocol
    /// compatibility, not code sharing" reasoning a file-format importer already applies
    /// to a completely external tool's own files (see <c>MeshFileService</c>'s own
    /// remarks on OBJ/STL).
    ///
    /// <c>JolieCat3D.Core</c>/<c>JolieCat3D.Service</c> have no image codec of their own
    /// (deliberately - matching <c>MeshFileService</c>'s own disclosed FBX scope cut:
    /// decoding PNG/JPEG bytes correctly, from scratch, with no way to validate the
    /// result in this environment, risks a decoder that looks plausible but silently
    /// produces wrong pixels), so every method here that needs an image's own pixel
    /// dimensions takes them as caller-supplied parameters rather than opening the file
    /// itself - <c>JolieCat3D.UI</c>, which already has a real image decoder available
    /// (WPF's own <c>BitmapImage</c>, the same one <c>JolieCat3D.Engine.Geometry.MaterialFactory</c>
    /// uses to actually render a textured material), is expected to have read them
    /// first.
    /// </summary>
    public static class TwoDAssetBridge
    {
        private static readonly Regex ClipbarFramePattern = new(@"^(?<index>\d{3,})$", RegexOptions.Compiled);

        /// <summary>A material sampling the WHOLE image at <paramref name="imagePath"/> -
        /// the simple "just use this exported texture" case, with no sprite-sheet
        /// cropping at all (offset (0,0), scale (1,1) - <see cref="Material"/>'s own
        /// defaults).</summary>
        public static Material LoadTextureMaterial(string imagePath, string? name = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);

            return new Material(name ?? Path.GetFileNameWithoutExtension(imagePath))
            {
                DiffuseTexturePath = imagePath,
            };
        }

        /// <summary>
        /// The pixel rectangle of <paramref name="grid"/>'s cell at
        /// (<paramref name="column"/>, <paramref name="row"/>) within an
        /// <paramref name="imageWidth"/> x <paramref name="imageHeight"/> source image -
        /// the same <c>width = (imageWidth - marginX*2 - paddingX*(columns-1)) / columns</c>
        /// / <c>x = marginX + column*(cellWidth+paddingX)</c> formulas <c>JolieCat</c>'s
        /// own 2D <c>SpriteSheetGrid.GetCellSize</c>/<c>GetCellRect</c> use,
        /// reimplemented independently here (see this class's own remarks on why no
        /// project reference exists to share that code directly) so a grid exported
        /// from the 2D editor divides identically here.
        /// </summary>
        public static (double X, double Y, double Width, double Height) GetCellRect(
            SpriteSheetGridInfo grid, int column, int row, double imageWidth, double imageHeight)
        {
            if (grid.Columns <= 0 || grid.Rows <= 0)
                throw new ArgumentException("A sprite sheet grid needs at least one column and row.", nameof(grid));
            if (column < 0 || column >= grid.Columns)
                throw new ArgumentOutOfRangeException(nameof(column));
            if (row < 0 || row >= grid.Rows)
                throw new ArgumentOutOfRangeException(nameof(row));

            var cellWidth = (imageWidth - grid.MarginX * 2 - grid.PaddingX * (grid.Columns - 1)) / grid.Columns;
            var cellHeight = (imageHeight - grid.MarginY * 2 - grid.PaddingY * (grid.Rows - 1)) / grid.Rows;

            var x = grid.MarginX + column * (cellWidth + grid.PaddingX);
            var y = grid.MarginY + row * (cellHeight + grid.PaddingY);

            return (x, y, cellWidth, cellHeight);
        }

        /// <summary>
        /// A material sampling exactly one cell of a sprite sheet image - the pixel
        /// rectangle from <see cref="GetCellRect"/> converted into the UV-space
        /// <see cref="Material.DiffuseTextureOffset"/>/<see cref="Material.DiffuseTextureScale"/>
        /// that <see cref="Material"/> itself actually stores (0-1 fractions of the
        /// whole image, not pixels - the same WPF-<c>ImageBrush.Viewbox</c>-with-
        /// <c>ViewboxUnits=RelativeToBoundingBox</c> shape <c>JolieCat3D.Engine</c>'s
        /// <c>MaterialFactory</c> builds from it), with V flipped
        /// (<c>1 - y/imageHeight - scaleV</c>) since image pixel rows count down from
        /// the top while UV/texture-coordinate V conventionally counts up from the
        /// bottom - the same flip <c>JolieCat3D.Engine.Geometry.MeshGeometryFactory</c>'s
        /// own <c>Primitives</c>-authored UVs already assume.
        /// </summary>
        public static Material LoadSpriteSheetCellMaterial(
            string imagePath, SpriteSheetGridInfo grid, int column, int row,
            double imageWidth, double imageHeight, string? name = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
            var (x, y, width, height) = GetCellRect(grid, column, row, imageWidth, imageHeight);

            var offsetU = x / imageWidth;
            var scaleU = width / imageWidth;
            var scaleV = height / imageHeight;
            var offsetV = 1.0 - (y / imageHeight) - scaleV;

            return new Material(name ?? $"{Path.GetFileNameWithoutExtension(imagePath)}_{column}_{row}")
            {
                DiffuseTexturePath = imagePath,
                DiffuseTextureOffset = new Vector2((float)offsetU, (float)offsetV),
                DiffuseTextureScale = new Vector2((float)scaleU, (float)scaleV),
            };
        }

        /// <summary>
        /// Finds every file in <paramref name="folderPath"/> matching <c>JolieCat</c>'s
        /// own <c>ImageExportService.ExportSpriteSheetCells</c> clipbar-frame naming
        /// convention - <c>{baseName}_{index:D3}.{extension}</c> (e.g.
        /// <c>"Untitled 1_000.png"</c>, <c>"Untitled 1_001.png"</c>, ...) - ordered by
        /// frame index, for any extension present (matching whichever image format the
        /// 2D editor happened to export, without this needing to know it in advance).
        /// </summary>
        public static IReadOnlyList<string> FindClipbarFrameSequence(string folderPath, string baseName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(baseName);

            if (!Directory.Exists(folderPath)) return Array.Empty<string>();

            var prefix = baseName + "_";

            var frames = new List<(int Index, string Path)>();
            foreach (var path in Directory.EnumerateFiles(folderPath))
            {
                var fileName = Path.GetFileNameWithoutExtension(path);
                if (!fileName.StartsWith(prefix, StringComparison.Ordinal)) continue;

                var indexPart = fileName[prefix.Length..];
                var match = ClipbarFramePattern.Match(indexPart);
                if (!match.Success) continue;

                frames.Add((int.Parse(match.Groups["index"].Value), path));
            }

            return frames.OrderBy(frame => frame.Index).Select(frame => frame.Path).ToList();
        }

        /// <summary>One whole-image <see cref="Material"/> (see <see cref="LoadTextureMaterial"/>)
        /// per frame found by <see cref="FindClipbarFrameSequence"/>, in frame order -
        /// the 3D side's equivalent of a 2D clipbar/flipbook animation, each frame ready
        /// to be assigned to a mesh's <see cref="Core.Geometry.Mesh.Material"/> in
        /// sequence (e.g. one per keyframe of an animation timeline this project doesn't
        /// itself own).</summary>
        public static IReadOnlyList<Material> LoadClipbarFrameMaterials(string folderPath, string baseName) =>
            FindClipbarFrameSequence(folderPath, baseName)
                .Select(path => LoadTextureMaterial(path))
                .ToList();
    }
}
