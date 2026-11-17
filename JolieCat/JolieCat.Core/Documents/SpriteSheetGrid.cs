using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace JolieCat.Core.Documents
{
    /// <summary>
    /// A <see cref="Shared.Enums.ProjectType.SpriteSheet"/> project's slicing grid: an
    /// evenly spaced <see cref="Columns"/> by <see cref="Rows"/> layout over the whole
    /// document, with an optional <see cref="MarginX"/>/<see cref="MarginY"/> border
    /// around the grid and <see cref="PaddingX"/>/<see cref="PaddingY"/> spacing between
    /// cells. Cell size itself is always derived from the document's own size plus
    /// these settings (rather than edited directly) - the common "this sheet is N
    /// columns by M rows" sprite-sheet convention, and it keeps column/row count and
    /// cell size from ever disagreeing with the document's actual dimensions. Pure data
    /// plus geometry, no rendering - <c>JolieCat.UI.Rendering.CanvasRenderer</c> draws
    /// the overlay from <see cref="EnumerateCells"/>, <c>JolieCat.Core.Export.ImageExportService</c>
    /// slices frames from it, and the Marquee tools snap to it via <see cref="SnapPoint"/>.
    /// </summary>
    public sealed class SpriteSheetGrid
    {
        public int Columns { get; set; } = 4;

        public int Rows { get; set; } = 4;

        /// <summary>Spacing between adjacent cells, in document pixels.</summary>
        public int PaddingX { get; set; }
        public int PaddingY { get; set; }

        /// <summary>Border around the whole grid before the first row/column starts, in document pixels.</summary>
        public int MarginX { get; set; }
        public int MarginY { get; set; }

        /// <summary>Where each sprite's pivot sits in its cell, in percent of the cell
        /// (50, 100 = middle of the bottom edge, where a character stands). Game engines
        /// place and turn the sprite around this point.</summary>
        public double PivotX { get; set; } = 50;
        public double PivotY { get; set; } = 100;

        /// <summary>Names of the rows, comma-separated, top row first ("walk, jump, attack"):
        /// each named row is exported as an animation. Empty: none.</summary>
        public string RowNames { get; set; } = string.Empty;

        /// <summary>The name of <paramref name="row"/> (0 = top), or null if it has none.</summary>
        public string? RowName(int row)
        {
            var names = RowNames.Split(',');
            return row >= 0 && row < names.Length && names[row].Trim() is { Length: > 0 } name ? name : null;
        }

        /// <summary>The pivot of the cell at <paramref name="rect"/>, in document pixels.</summary>
        public SKPoint PivotIn(SKRect rect) =>
            new(rect.Left + rect.Width * (float)(PivotX / 100), rect.Top + rect.Height * (float)(PivotY / 100));

        /// <summary>Cells with their own pivot (in percent), by cell number (0 = top-left,
        /// row by row). The others use <see cref="PivotX"/> / <see cref="PivotY"/>.</summary>
        public Dictionary<int, (double X, double Y)> CellPivots { get; } = new();

        /// <summary>Cell <paramref name="index"/>'s pivot in percent of the cell.</summary>
        public (double X, double Y) PivotFor(int index) => CellPivots.TryGetValue(index, out var p) ? p : (PivotX, PivotY);

        /// <summary>The pivot of cell <paramref name="index"/> placed at <paramref name="rect"/>, in document pixels.</summary>
        public SKPoint PivotIn(SKRect rect, int index)
        {
            var (x, y) = PivotFor(index);
            return new(rect.Left + rect.Width * (float)(x / 100), rect.Top + rect.Height * (float)(y / 100));
        }

        /// <summary>The cell pivots as text ("3:50,80;7:20,100"), for saving.</summary>
        public string CellPivotsText
        {
            get => string.Join(";", CellPivots.OrderBy(p => p.Key).Select(p =>
                string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{p.Key}:{p.Value.X:0.##},{p.Value.Y:0.##}")));
            set
            {
                CellPivots.Clear();
                foreach (var part in (value ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var bits = part.Split(':', ',');
                    if (bits.Length == 3 && int.TryParse(bits[0], out var i)
                        && double.TryParse(bits[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)
                        && double.TryParse(bits[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y))
                        CellPivots[i] = (Math.Clamp(x, 0, 100), Math.Clamp(y, 0, 100));
                }
            }
        }

        /// <summary>Each cell's size for a document of (<paramref name="documentWidth"/>,
        /// <paramref name="documentHeight"/>) - the margins and inter-cell padding are
        /// fixed, so the remaining space divides evenly across <see cref="Columns"/>/
        /// <see cref="Rows"/>. Never smaller than 1px, even if the margins/padding
        /// configured would otherwise leave no room at all - an overlay/slice a user can
        /// still see and adjust rather than one that silently vanishes to nothing.</summary>
        public SKSize GetCellSize(int documentWidth, int documentHeight)
        {
            var columns = Math.Max(1, Columns);
            var rows = Math.Max(1, Rows);

            var width = (documentWidth - MarginX * 2f - PaddingX * (columns - 1)) / columns;
            var height = (documentHeight - MarginY * 2f - PaddingY * (rows - 1)) / rows;

            return new SKSize(Math.Max(1f, width), Math.Max(1f, height));
        }

        /// <summary>The document-space rectangle for one cell at (<paramref name="column"/>,
        /// <paramref name="row"/>) - zero-based, column increasing left-to-right, row
        /// top-to-bottom.</summary>
        public SKRect GetCellRect(int column, int row, int documentWidth, int documentHeight)
        {
            var cellSize = GetCellSize(documentWidth, documentHeight);
            var x = MarginX + column * (cellSize.Width + PaddingX);
            var y = MarginY + row * (cellSize.Height + PaddingY);

            return SKRect.Create(x, y, cellSize.Width, cellSize.Height);
        }

        /// <summary>Every cell in the grid, row-major (row 0's columns left-to-right,
        /// then row 1's, and so on) - the order <c>ImageExportService.ExportSpriteSheetCells</c>
        /// numbers exported frames in.</summary>
        public IEnumerable<(int Column, int Row, SKRect Rect)> EnumerateCells(int documentWidth, int documentHeight)
        {
            var columns = Math.Max(1, Columns);
            var rows = Math.Max(1, Rows);

            for (var row = 0; row < rows; row++)
                for (var column = 0; column < columns; column++)
                    yield return (column, row, GetCellRect(column, row, documentWidth, documentHeight));
        }

        /// <summary>Snaps <paramref name="point"/> to the nearest grid line along each
        /// axis independently - a cell's near or far edge alike - the Marquee tools'
        /// own "snap to grid intersections" behavior for a Sprite Sheet project.</summary>
        public SKPoint SnapPoint(SKPoint point, int documentWidth, int documentHeight)
        {
            var cellSize = GetCellSize(documentWidth, documentHeight);
            var columns = Math.Max(1, Columns);
            var rows = Math.Max(1, Rows);

            var x = SnapAxis(point.X, MarginX, cellSize.Width, PaddingX, columns);
            var y = SnapAxis(point.Y, MarginY, cellSize.Height, PaddingY, rows);
            return new SKPoint(x, y);
        }

        private static float SnapAxis(float value, float margin, float cellExtent, float padding, int count)
        {
            var best = margin;
            var bestDistance = MathF.Abs(value - margin);

            for (var i = 0; i <= count; i++)
            {
                var lineStart = margin + i * (cellExtent + padding);
                var distanceToStart = MathF.Abs(value - lineStart);
                if (distanceToStart < bestDistance)
                {
                    bestDistance = distanceToStart;
                    best = lineStart;
                }

                if (i >= count) continue;

                var lineEnd = lineStart + cellExtent;
                var distanceToEnd = MathF.Abs(value - lineEnd);
                if (distanceToEnd < bestDistance)
                {
                    bestDistance = distanceToEnd;
                    best = lineEnd;
                }
            }

            return best;
        }
    }
}
