using System;
using JolieCat.Core.Documents;
using JolieCat.Core.Rendering;
using JolieCat.UI.ViewModels;
using JolieCat.UI.ViewModels.Layers;
using SkiaSharp;

namespace JolieCat.UI.Rendering
{
    /// <summary>
    /// Draws one frame of the center canvas from <see cref="CanvasViewModel"/>'s state: a
    /// checkerboard "transparency" backdrop clipped to the document bounds, every visible
    /// layer composited back-to-front with its own opacity and blend mode, the
    /// marching-ants marquee overlay when a selection is active, and (while active) the
    /// Crop/Free Transform/Warp tools' own interactive overlays - all inside the pan/zoom
    /// transform so they move and scale together. This class owns no state of its own; it
    /// only reads the view model and paints.
    /// </summary>
    public static class CanvasRenderer
    {
        private const int CheckerSize = 16;

        private static readonly SKColor CheckerLight = new(0x3A, 0x3A, 0x3D);
        private static readonly SKColor CheckerDark = new(0x2A, 0x2A, 0x2C);
        private static readonly SKColor OutsideDocumentColor = new(0x12, 0x12, 0x13);
        private static readonly SKColor CropDarkenColor = new(0, 0, 0, 160);
        private static readonly SKColor HandleBorderColor = new(0x3D, 0x8B, 0xFD); // matches AccentBrush

        public static void Render(SKCanvas canvas, SKImageInfo info, CanvasViewModel viewModel)
        {
            // Solid fill for the area outside the document bounds - distinguishes the
            // "desk" from the canvas itself, like the gray surround in Photoshop.
            canvas.Clear(OutsideDocumentColor);

            canvas.Save();
            canvas.Translate((float)viewModel.PanX, (float)viewModel.PanY);
            canvas.Scale((float)viewModel.Zoom);

            var documentRect = new SKRect(0, 0, viewModel.Layers.DocumentWidth, viewModel.Layers.DocumentHeight);

            DrawCheckerboard(canvas, documentRect);

            // Scene.Layers is already back-to-front, so drawing in list order composites
            // correctly with no extra sorting. Masking, opacity, and blend mode are all
            // handled by SceneCompositor - the same code JolieCat.Core's image export
            // and Scene.MergeLayerDown use, so a masked layer looks identical here, in a
            // merge, and in an exported file. While Free Transform/Warp is live on the
            // active layer, its own (as-yet-uncommitted) preview bitmap substitutes for
            // that one layer's committed content - see CanvasViewModel's own remarks.
            var previewLayer = viewModel.LivePreviewLayer;
            using var previewBitmap = viewModel.BuildLivePreviewBitmap();
            SceneCompositor.DrawLayers(canvas, viewModel.Layers.Scene, viewModel.Layers.DocumentWidth, viewModel.Layers.DocumentHeight, previewLayer, previewBitmap);

            DrawSelectionOverlay(canvas, viewModel);
            DrawCropOverlay(canvas, viewModel);
            DrawTransformOverlay(canvas, viewModel);
            DrawWarpOverlay(canvas, viewModel);

            canvas.Restore();
        }

        private static void DrawCheckerboard(SKCanvas canvas, SKRect documentRect)
        {
            using var lightPaint = new SKPaint { Color = CheckerLight, Style = SKPaintStyle.Fill };
            using var darkPaint = new SKPaint { Color = CheckerDark, Style = SKPaintStyle.Fill };

            canvas.Save();
            canvas.ClipRect(documentRect);

            for (var y = 0; y < documentRect.Height; y += CheckerSize)
            {
                for (var x = 0; x < documentRect.Width; x += CheckerSize)
                {
                    var isLight = (x / CheckerSize + y / CheckerSize) % 2 == 0;
                    canvas.DrawRect(x, y, CheckerSize, CheckerSize, isLight ? lightPaint : darkPaint);
                }
            }

            canvas.Restore();
        }

        /// <summary>
        /// One dashed "marching ants" outline, uniform across every selection tool: while
        /// a marquee/lasso/polygon is still being drawn, that's <see cref="CanvasViewModel.LiveSelectionPath"/>;
        /// once committed (including Magic Wand/Quick Selection's region-based result,
        /// which was never a simple path to begin with), it's the committed
        /// <c>Scene.Selection</c>'s own <see cref="Selection.Path"/> - the same path
        /// <see cref="CanvasViewModel"/> clips painting to, so the outline always matches
        /// exactly what the selection actually constrains.
        /// </summary>
        private static void DrawSelectionOverlay(SKCanvas canvas, CanvasViewModel viewModel)
        {
            var path = viewModel.LiveSelectionPath;

            if (path is null)
            {
                if (!viewModel.Layers.Scene.Selection.HasSelection) return;
                path = viewModel.Layers.Scene.Selection.Path;
                if (path is null) return;
            }

            // Stroke width and dash length are scaled down by zoom so the outline reads
            // as a crisp ~1px dashed line on screen at any zoom level, matching how a
            // real selection outline behaves rather than zooming with the pixels.
            var onScreenScale = 1f / (float)viewModel.Zoom;

            using var paint = new SKPaint
            {
                Color = SKColors.White,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = onScreenScale,
                IsAntialias = false,
                PathEffect = SKPathEffect.CreateDash(new[] { 4f * onScreenScale, 4f * onScreenScale }, 0),
            };

            canvas.DrawPath(path, paint);
        }

        /// <summary>Darkens everything outside the live crop rectangle, draws rule-of-
        /// thirds guide lines and a plain border inside it, then its own handles - a
        /// no-op while the Crop tool isn't active.</summary>
        private static void DrawCropOverlay(SKCanvas canvas, CanvasViewModel viewModel)
        {
            if (viewModel.CropRect is not { } cropRect) return;

            var onScreenScale = 1f / (float)viewModel.Zoom;

            // ClipRect(..., Difference) + DrawColor (not Clear, which ignores the
            // clip) darkens exactly the area outside cropRect within whatever's
            // currently visible - the surrounding "desk" and any part of the
            // document outside the rectangle alike.
            canvas.Save();
            canvas.ClipRect(cropRect, SKClipOperation.Difference);
            canvas.DrawColor(CropDarkenColor, SKBlendMode.SrcOver);
            canvas.Restore();

            using (var guidePaint = new SKPaint { Color = new SKColor(255, 255, 255, 90), StrokeWidth = onScreenScale })
            {
                for (var i = 1; i <= 2; i++)
                {
                    var x = cropRect.Left + cropRect.Width * i / 3f;
                    var y = cropRect.Top + cropRect.Height * i / 3f;
                    canvas.DrawLine(x, cropRect.Top, x, cropRect.Bottom, guidePaint);
                    canvas.DrawLine(cropRect.Left, y, cropRect.Right, y, guidePaint);
                }
            }

            using (var borderPaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = onScreenScale })
                canvas.DrawRect(cropRect, borderPaint);

            DrawHandle(canvas, new SKPoint(cropRect.Left, cropRect.Top), onScreenScale);
            DrawHandle(canvas, new SKPoint(cropRect.MidX, cropRect.Top), onScreenScale);
            DrawHandle(canvas, new SKPoint(cropRect.Right, cropRect.Top), onScreenScale);
            DrawHandle(canvas, new SKPoint(cropRect.Right, cropRect.MidY), onScreenScale);
            DrawHandle(canvas, new SKPoint(cropRect.Right, cropRect.Bottom), onScreenScale);
            DrawHandle(canvas, new SKPoint(cropRect.MidX, cropRect.Bottom), onScreenScale);
            DrawHandle(canvas, new SKPoint(cropRect.Left, cropRect.Bottom), onScreenScale);
            DrawHandle(canvas, new SKPoint(cropRect.Left, cropRect.MidY), onScreenScale);
        }

        /// <summary>Draws the Free Transform box's outline, its four corner handles,
        /// and its rotate handle (connected to the box's top edge by a thin line) - a
        /// no-op while Free Transform isn't active. The live-transformed preview of
        /// the layer's own pixels is drawn separately, as part of the normal layer
        /// pass in <see cref="Render"/> - this only draws the interactive chrome
        /// on top of it.</summary>
        private static void DrawTransformOverlay(SKCanvas canvas, CanvasViewModel viewModel)
        {
            var corners = viewModel.TransformCorners();
            if (corners is null) return;

            var onScreenScale = 1f / (float)viewModel.Zoom;

            using (var boxPaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = onScreenScale, IsAntialias = true })
            using (var path = new SKPath())
            {
                path.MoveTo(corners[0]);
                path.LineTo(corners[1]);
                path.LineTo(corners[2]);
                path.LineTo(corners[3]);
                path.Close();
                canvas.DrawPath(path, boxPaint);
            }

            foreach (var corner in corners)
                DrawHandle(canvas, corner, onScreenScale);

            if (viewModel.TransformRotateHandle() is { } rotateHandle)
            {
                var topMid = new SKPoint((corners[0].X + corners[1].X) / 2f, (corners[0].Y + corners[1].Y) / 2f);
                using (var linePaint = new SKPaint { Color = SKColors.White, StrokeWidth = onScreenScale, IsAntialias = true })
                    canvas.DrawLine(topMid, rotateHandle, linePaint);

                DrawRoundHandle(canvas, rotateHandle, onScreenScale);
            }
        }

        /// <summary>Draws the Warp tool's control-point grid (both axes of connecting
        /// lines, then a handle at each point) at its current, possibly-distorted
        /// positions - a no-op while Warp isn't active. Like Free Transform, the
        /// warped preview of the layer's own pixels is drawn separately as part of
        /// the normal layer pass in <see cref="Render"/>.</summary>
        private static void DrawWarpOverlay(SKCanvas canvas, CanvasViewModel viewModel)
        {
            var mesh = viewModel.WarpMesh;
            if (mesh is null) return;

            var onScreenScale = 1f / (float)viewModel.Zoom;

            using (var linePaint = new SKPaint { Color = new SKColor(255, 255, 255, 180), StrokeWidth = onScreenScale, IsAntialias = true })
            {
                for (var row = 0; row < mesh.Rows; row++)
                    for (var col = 0; col < mesh.Columns - 1; col++)
                        canvas.DrawLine(mesh.WarpedGrid[row, col], mesh.WarpedGrid[row, col + 1], linePaint);

                for (var col = 0; col < mesh.Columns; col++)
                    for (var row = 0; row < mesh.Rows - 1; row++)
                        canvas.DrawLine(mesh.WarpedGrid[row, col], mesh.WarpedGrid[row + 1, col], linePaint);
            }

            for (var row = 0; row < mesh.Rows; row++)
                for (var col = 0; col < mesh.Columns; col++)
                    DrawHandle(canvas, mesh.WarpedGrid[row, col], onScreenScale);
        }

        /// <summary>A small square handle, sized to read as a fixed ~8 screen pixels
        /// regardless of zoom (<paramref name="onScreenScale"/> is <c>1/Zoom</c>) -
        /// shared by the Crop, Free Transform, and Warp overlays.</summary>
        private static void DrawHandle(SKCanvas canvas, SKPoint center, float onScreenScale)
        {
            var half = 5f * onScreenScale;
            var rect = SKRect.Create(center.X - half, center.Y - half, half * 2, half * 2);

            using var fill = new SKPaint { Color = SKColors.White, IsAntialias = true };
            using var border = new SKPaint { Color = HandleBorderColor, Style = SKPaintStyle.Stroke, StrokeWidth = onScreenScale, IsAntialias = true };
            canvas.DrawRect(rect, fill);
            canvas.DrawRect(rect, border);
        }

        /// <summary>A small round handle - Free Transform's rotate handle, kept visually
        /// distinct from the square scale handles.</summary>
        private static void DrawRoundHandle(SKCanvas canvas, SKPoint center, float onScreenScale)
        {
            var radius = 5f * onScreenScale;
            var rect = SKRect.Create(center.X - radius, center.Y - radius, radius * 2, radius * 2);

            using var fill = new SKPaint { Color = SKColors.White, IsAntialias = true };
            using var border = new SKPaint { Color = HandleBorderColor, Style = SKPaintStyle.Stroke, StrokeWidth = onScreenScale, IsAntialias = true };
            canvas.DrawOval(rect, fill);
            canvas.DrawOval(rect, border);
        }
    }
}
