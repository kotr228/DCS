using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Documents;
using JolieCat.Core.History;
using JolieCat.Shared.Enums;
using JolieCat.UI.Media;
using JolieCat.UI.ViewModels.Layers;
using JolieCat.UI.ViewModels.ToolOptions;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    public partial class CanvasViewModel
    {
        // ================= Warp =================

        private bool _isWarping;
        private Core.Documents.Layer? _warpLayer;
        private SKBitmap? _warpOriginalBitmap;
        private Core.Transform.MeshWarp? _warpMesh;
        private int _warpDragRow = -1;
        private int _warpDragCol = -1;

        // ================= Warp =================

        /// <summary>The live control-point grid, or null while the Warp tool isn't
        /// active - <see cref="Rendering.CanvasRenderer"/> draws the grid lines/points
        /// from this alongside <see cref="LivePreviewLayer"/>'s warped preview.</summary>
        public Core.Transform.MeshWarp? WarpMesh => _isWarping ? _warpMesh : null;

        private void StartWarp()
        {
            var layer = Layers.ActiveLayer?.Model;
            if (layer is null || layer.IsGroup) return;
            RasterizeIfText(layer);

            var gridSize = Math.Clamp((_toolbox.CurrentToolOptions as WarpToolOptionsViewModel)?.GridSize ?? 3, 2, 4);

            _warpLayer = layer;
            _warpOriginalBitmap?.Dispose();
            _warpOriginalBitmap = layer.Bitmap.Copy();
            _warpMesh = new Core.Transform.MeshWarp(gridSize, gridSize, layer.Bitmap.Width, layer.Bitmap.Height);
            _isWarping = true;
        }

        /// <summary>Bakes the live-warped mesh into the active layer's actual bitmap
        /// (see <see cref="Core.Transform.MeshWarp.Bake"/>) and records it as one
        /// undo/redo entry - a no-op if Warp isn't active.</summary>
        public void CommitWarp()
        {
            if (!_isWarping || _warpLayer is null || _warpOriginalBitmap is null || _warpMesh is null)
            {
                EndWarp();
                return;
            }

            if (_warpLayer is { HasSmartFilters: true, SmartFilterSource: { } source })
            {
                // Smart Filters: warp the untouched original, then run the filters again.
                var beforeSource = source.Pixels;
                using (var bakedSource = _warpMesh.Bake(source))
                    source.Pixels = bakedSource.Pixels;
                _warpLayer.RenderSmartFilters();
                Layers.History.Push(new Core.History.SmartFilterSourceCommand(Layers.Scene, _warpLayer, beforeSource, source.Pixels), "Warp");
                EndWarp();
                RaiseInvalidate();
                return;
            }

            var before = _warpLayer.Bitmap.Pixels;
            using (var baked = _warpMesh.Bake(_warpOriginalBitmap))
                _warpLayer.Bitmap.Pixels = baked.Pixels;

            Layers.History.Push(PixelEdit(_warpLayer, before, contentOnly: true));
            EndWarp();
            RaiseInvalidate();
        }

        /// <summary>Discards the in-progress warp - the active layer's actual bitmap
        /// was never touched (only ever live-previewed), so this just drops the
        /// preview state, not undoing a write.</summary>
        public void CancelWarp()
        {
            EndWarp();
            RaiseInvalidate();
        }

        private void EndWarp()
        {
            _isWarping = false;
            _warpLayer = null;
            _warpOriginalBitmap?.Dispose();
            _warpOriginalBitmap = null;
            _warpMesh = null;
            _warpDragRow = -1;
            _warpDragCol = -1;
        }
    }
}
