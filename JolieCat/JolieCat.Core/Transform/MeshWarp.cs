using System;
using SkiaSharp;

namespace JolieCat.Core.Transform
{
    /// <summary>
    /// A rectangular control-point grid over a bitmap, and the triangulated-mesh
    /// rendering that turns a distorted copy of that grid into an actual pixel warp -
    /// the basic Mesh Deformation/Warp tool. Every triangle samples <see cref="Source"/>
    /// via its own original (undistorted) grid coordinates as texture coordinates and
    /// is positioned at the corresponding distorted grid point, so
    /// <see cref="SKCanvas.DrawVertices"/> stretches/compresses each cell's own patch of
    /// source pixels independently - verified against a real render (a displaced center
    /// point stretches its neighboring cells with no gap or tear at the seams, while
    /// untouched cells stay pixel-for-pixel where they started).
    /// </summary>
    public sealed class MeshWarp
    {
        /// <summary>Control points in row-major order, e.g. [row, col] - both grids are
        /// always the same (<see cref="Rows"/>, <see cref="Columns"/>) shape.</summary>
        public SKPoint[,] OriginalGrid { get; }

        /// <summary>The live/distorted grid - identical to <see cref="OriginalGrid"/>
        /// until a control point is dragged. Mutate this (not <see cref="OriginalGrid"/>)
        /// to warp the mesh.</summary>
        public SKPoint[,] WarpedGrid { get; }

        public int Rows { get; }

        public int Columns { get; }

        /// <summary>Builds an evenly-spaced <paramref name="rows"/> x <paramref name="columns"/>
        /// control-point grid spanning exactly (0,0)-(<paramref name="width"/>,<paramref name="height"/>) -
        /// a bitmap's own bounds, so the corner control points always sit exactly on the
        /// bitmap's own corners.</summary>
        public MeshWarp(int rows, int columns, float width, float height)
        {
            if (rows < 2 || columns < 2) throw new ArgumentOutOfRangeException(nameof(rows), "A mesh needs at least a 2x2 grid (one cell).");

            Rows = rows;
            Columns = columns;
            OriginalGrid = new SKPoint[rows, columns];
            WarpedGrid = new SKPoint[rows, columns];

            for (var row = 0; row < rows; row++)
            {
                for (var col = 0; col < columns; col++)
                {
                    var point = new SKPoint(col * width / (columns - 1), row * height / (rows - 1));
                    OriginalGrid[row, col] = point;
                    WarpedGrid[row, col] = point;
                }
            }
        }

        /// <summary>Resets every point in <see cref="WarpedGrid"/> back to
        /// <see cref="OriginalGrid"/> - Escape/Reset for the Warp tool.</summary>
        public void Reset()
        {
            for (var row = 0; row < Rows; row++)
                for (var col = 0; col < Columns; col++)
                    WarpedGrid[row, col] = OriginalGrid[row, col];
        }

        /// <summary>The index of whichever control point is closest to <paramref name="point"/> -
        /// the Warp tool's own hit-testing (drag whichever point you clicked nearest to)
        /// delegates here rather than requiring an exact hit on a tiny handle.</summary>
        public (int Row, int Col) FindNearestPoint(SKPoint point)
        {
            var bestRow = 0;
            var bestCol = 0;
            var bestDistance = float.MaxValue;

            for (var row = 0; row < Rows; row++)
            {
                for (var col = 0; col < Columns; col++)
                {
                    var candidate = WarpedGrid[row, col];
                    var dx = candidate.X - point.X;
                    var dy = candidate.Y - point.Y;
                    var distance = dx * dx + dy * dy;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestRow = row;
                        bestCol = col;
                    }
                }
            }

            return (bestRow, bestCol);
        }

        /// <summary>Renders <paramref name="source"/> warped from <see cref="OriginalGrid"/>
        /// to <see cref="WarpedGrid"/>'s current (possibly distorted) shape, onto
        /// <paramref name="canvas"/> - used identically for the tool's own live preview
        /// (drawn each frame while dragging) and its final commit (drawn once into a
        /// fresh bitmap - see <c>CanvasViewModel</c>'s Warp commit).</summary>
        public void Render(SKCanvas canvas, SKBitmap source)
        {
            ArgumentNullException.ThrowIfNull(canvas);
            ArgumentNullException.ThrowIfNull(source);

            var cellsPerRow = Columns - 1;
            var cellsPerColumn = Rows - 1;
            var triangleCount = cellsPerRow * cellsPerColumn * 2;
            var positions = new SKPoint[triangleCount * 3];
            var texCoords = new SKPoint[triangleCount * 3];
            var t = 0;

            for (var row = 0; row < cellsPerColumn; row++)
            {
                for (var col = 0; col < cellsPerRow; col++)
                {
                    void AddVertex(int r, int c)
                    {
                        positions[t] = WarpedGrid[r, c];
                        texCoords[t] = OriginalGrid[r, c];
                        t++;
                    }

                    AddVertex(row, col); AddVertex(row, col + 1); AddVertex(row + 1, col);
                    AddVertex(row, col + 1); AddVertex(row + 1, col + 1); AddVertex(row + 1, col);
                }
            }

            using var vertices = SKVertices.CreateCopy(SKVertexMode.Triangles, positions, texCoords, colors: null);
            using var shader = SKShader.CreateBitmap(source, SKShaderTileMode.Clamp, SKShaderTileMode.Clamp);
            using var paint = new SKPaint { Shader = shader, IsAntialias = true };
            canvas.DrawVertices(vertices, SKBlendMode.Src, paint);
        }

        /// <summary>Bakes the current warp into a new bitmap the same size as
        /// <paramref name="source"/> - the Warp tool's commit step.</summary>
        public SKBitmap Bake(SKBitmap source)
        {
            ArgumentNullException.ThrowIfNull(source);

            var result = new SKBitmap(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(result);
            canvas.Clear(SKColors.Transparent);
            Render(canvas, source);
            return result;
        }
    }
}
