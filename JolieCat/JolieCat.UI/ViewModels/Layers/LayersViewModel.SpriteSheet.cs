using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using JolieCat.Core.Documents;
using SkiaSharp;

namespace JolieCat.UI.ViewModels.Layers
{
    /// <summary>Sprite sheet panel extras: finding the grid by itself, and playing the cells
    /// as an animation right in the panel.</summary>
    public partial class LayersViewModel
    {
        /// <summary>Detect Grid: columns, rows, padding and margins read from the empty
        /// space between the sprites. False if no grid shows.</summary>
        public bool DetectSpriteSheetGrid()
        {
            using var flat = Core.Rendering.SceneCompositor.Flatten(Scene, DocumentWidth, DocumentHeight);
            if (SpriteSheetTools.DetectGrid(flat) is not { } grid) return false;
            SpriteSheetColumns = grid.Columns;
            SpriteSheetRows = grid.Rows;
            SpriteSheetPaddingX = grid.PaddingX;
            SpriteSheetPaddingY = grid.PaddingY;
            SpriteSheetMarginX = grid.MarginX;
            SpriteSheetMarginY = grid.MarginY;
            if (_previewCells is not null) StartSpritePreview();
            return true;
        }

        /// <summary>Swaps / copies / clears / mirrors cells of the active layer. One undo step.</summary>
        public bool ChangeCells(SpriteSheetTools.CellAction action, int a, int b)
        {
            if (ActiveLayer?.Model is not { IsGroup: false, Adjustment: null } layer || layer.IsText || layer.IsShape || layer.IsFill) return false;
            using var changed = SpriteSheetTools.CellOperation(layer.Bitmap, SpriteSheetGrid, action, a, b);
            if (changed is null) return false;
            ReplaceLayerPixels(layer, changed, action switch
            {
                SpriteSheetTools.CellAction.Swap => "Swap Cells",
                SpriteSheetTools.CellAction.Copy => "Copy Cell",
                SpriteSheetTools.CellAction.Clear => "Clear Cell",
                _ => "Flip Cell",
            });
            if (_previewCells is not null) StartSpritePreview();
            return true;
        }

        /// <summary>Gives cell <paramref name="index"/> its own pivot (percent), or with nulls
        /// puts it back to the sheet's pivot. Undoable.</summary>
        public void SetCellPivot(int index, double? x, double? y)
        {
            var before = SpriteSheetGrid.CellPivotsText;
            if (x is { } px && y is { } py) SpriteSheetGrid.CellPivots[index] = (Math.Clamp(px, 0, 100), Math.Clamp(py, 0, 100));
            else SpriteSheetGrid.CellPivots.Remove(index);
            var after = SpriteSheetGrid.CellPivotsText;
            if (before == after) return;
            History.Push(new CellPivotsCommand(SpriteSheetGrid, before, after), "Cell Pivot");
            RaiseInvalidate();
        }

        private sealed class CellPivotsCommand : Core.History.IEditCommand
        {
            private readonly SpriteSheetGrid _grid;
            private readonly string _before, _after;
            public CellPivotsCommand(SpriteSheetGrid grid, string before, string after) => (_grid, _before, _after) = (grid, before, after);
            public void Undo() => _grid.CellPivotsText = _before;
            public void Redo() => _grid.CellPivotsText = _after;
        }

        [ObservableProperty] private bool isPreviewingSprites;
        [ObservableProperty] private BitmapSource? spritePreviewFrame;
        [ObservableProperty] private double spritePreviewFps = 8;

        /// <summary>Only the cells of this row play (0 = every cell in order).</summary>
        [ObservableProperty] private int spritePreviewRow;

        private List<BitmapSource>? _previewCells;
        private int _previewIndex;
        private DispatcherTimer? _previewTimer;

        partial void OnIsPreviewingSpritesChanged(bool value)
        {
            if (value) StartSpritePreview();
            else StopSpritePreview();
        }

        partial void OnSpritePreviewFpsChanged(double value)
        {
            if (_previewTimer is not null) _previewTimer.Interval = TimeSpan.FromSeconds(1 / Math.Clamp(value, 1, 60));
        }

        partial void OnSpritePreviewRowChanged(int value)
        {
            if (_previewCells is not null) StartSpritePreview();
        }

        /// <summary>The cells as pictures (from the image as it is now), in reading order.</summary>
        internal List<BitmapSource> SpritePreviewCells()
        {
            var cells = new List<BitmapSource>();
            using var flat = Core.Rendering.SceneCompositor.Flatten(Scene, DocumentWidth, DocumentHeight);
            var grid = SpriteSheetGrid;
            foreach (var (column, row, rect) in Cells(grid))
            {
                if (SpritePreviewRow > 0 && row != SpritePreviewRow - 1) continue;
                using var cell = SpriteSheetTools.Cell(flat, rect);
                if (IsEmpty(cell)) continue; // empty cells at the end of a sheet don't play
                cells.Add(Timeline.TimelineViewModel.FastThumbnail(cell, 160));
            }
            return cells;
        }

        private IEnumerable<(int Column, int Row, SKRect Rect)> Cells(SpriteSheetGrid grid)
        {
            for (var row = 0; row < Math.Max(1, grid.Rows); row++)
                for (var column = 0; column < Math.Max(1, grid.Columns); column++)
                    yield return (column, row, grid.GetCellRect(column, row, DocumentWidth, DocumentHeight));
        }

        private static bool IsEmpty(SKBitmap bitmap)
        {
            foreach (var p in bitmap.Pixels) if (p.Alpha > 0) return false;
            return true;
        }

        private void StartSpritePreview()
        {
            _previewCells = SpritePreviewCells();
            _previewIndex = 0;
            SpritePreviewFrame = _previewCells.Count > 0 ? _previewCells[0] : null;
            _previewTimer ??= new DispatcherTimer(DispatcherPriority.Render);
            _previewTimer.Interval = TimeSpan.FromSeconds(1 / Math.Clamp(SpritePreviewFps, 1, 60));
            _previewTimer.Tick -= PreviewTick;
            _previewTimer.Tick += PreviewTick;
            _previewTimer.Start();
        }

        private void StopSpritePreview()
        {
            _previewTimer?.Stop();
            _previewCells = null;
            SpritePreviewFrame = null;
        }

        private void PreviewTick(object? sender, EventArgs e)
        {
            if (_previewCells is not { Count: > 0 } cells) return;
            _previewIndex = (_previewIndex + 1) % cells.Count;
            SpritePreviewFrame = cells[_previewIndex];
        }
    }
}
