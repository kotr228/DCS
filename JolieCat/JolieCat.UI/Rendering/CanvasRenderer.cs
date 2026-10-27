using System;
using System.Linq;
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

        // Left as a neutral dark checkerboard, not re-themed to the new warm palette -
        // every raster editor's own universal "no pixel data here" convention, which a
        // checkerboard only reads as at a glance if it stays a plain neutral gray
        // regardless of the app's own current theme.
        private static readonly SKColor CheckerLight = new(0x3A, 0x3A, 0x3D);
        private static readonly SKColor CheckerDark = new(0x2A, 0x2A, 0x2C);

        // "Main Background / Canvas Framing" - matches App.xaml's WindowBackgroundBrush.
        // Neutral dark charcoal, not the brand beige - the workspace itself is never a
        // solid brand-color fill; beige/gold are reserved for small accents only.
        private static readonly SKColor OutsideDocumentColor = new(0x1A, 0x17, 0x16);
        private static readonly SKColor CropDarkenColor = new(0, 0, 0, 160);
        private static readonly SKColor HandleBorderColor = new(0xC2, 0x9B, 0x58); // matches AccentBrush (Golden Ochre)

        public static void Render(SKCanvas canvas, SKImageInfo info, CanvasViewModel viewModel)
        {
            // Solid fill for the area outside the document bounds - distinguishes the
            // "desk" from the canvas itself, like the gray surround in Photoshop.
            canvas.Clear(OutsideDocumentColor);

            canvas.Save();
            canvas.Concat(viewModel.ViewMatrix); // pan, zoom and (optional) view rotation

            var documentRect = new SKRect(0, 0, viewModel.Layers.DocumentWidth, viewModel.Layers.DocumentHeight);

            DrawCheckerboard(canvas, documentRect);

            // Zoomed out, bilinear filtering keeps photos smooth instead of jagged;
            // zoomed in, nearest neighbour shows crisp pixels.
            var sampling = viewModel.Zoom < 1.0 || viewModel.IsViewRotated ? new SKSamplingOptions(SKFilterMode.Linear) : default;

            // While Free Transform/Warp/a filter dialog is live on a layer, its
            // uncommitted preview bitmap stands in for that layer's content.
            var previewLayer = viewModel.LivePreviewLayer;
            using var previewBitmap = viewModel.BuildLivePreviewBitmap();

            if (viewModel.RenderCache is not LayerRenderCache cache)
                viewModel.RenderCache = cache = new LayerRenderCache();

            // Layers are composited onto transparency in their own surface (see
            // LayerRenderCache), and the result goes over the checkerboard - so blend
            // modes and adjustment layers only ever see the image, never the checkerboard.
            var frame = cache.Compose(info, canvas.TotalMatrix, viewModel.Layers.Scene,
                viewModel.Layers.DocumentWidth, viewModel.Layers.DocumentHeight,
                viewModel.Layers.ContentVersion, sampling, previewLayer, previewBitmap);

            canvas.Save();
            canvas.ResetMatrix();
            canvas.DrawSurface(frame, 0, 0);
            canvas.Restore();

            viewModel.DrawLiveShape(canvas);

            DrawPixelGrid(canvas, info, viewModel);
            DrawSpriteSheetGridOverlay(canvas, viewModel);
            DrawSelectionOverlay(canvas, viewModel);
            DrawCropOverlay(canvas, viewModel);
            DrawTransformOverlay(canvas, viewModel);
            DrawWarpOverlay(canvas, viewModel);
            DrawPenPathOverlay(canvas, viewModel);
            DrawCloneSourceMarker(canvas, viewModel);
            DrawGradientGuide(canvas, viewModel);
            DrawSpotHealingStroke(canvas, viewModel);
            DrawBrushOutline(canvas, viewModel);

            canvas.Restore();
        }

        /// <summary>Zoom level from which individual pixels get grid lines.</summary>
        private const double PixelGridMinZoom = 8.0;

        /// <summary>Thin lines between pixels at high zoom - only the visible part of
        /// the document, so the cost doesn't grow with document size.</summary>
        private static void DrawPixelGrid(SKCanvas canvas, SKImageInfo info, CanvasViewModel viewModel)
        {
            if (viewModel.Zoom < PixelGridMinZoom) return;

            var zoom = (float)viewModel.Zoom;

            // The visible part of the document: the screen's corners mapped back into
            // document space (works for a rotated view too).
            if (!viewModel.ViewMatrix.TryInvert(out var toDocument)) return;
            var corners = toDocument.MapPoints(new[] { new SKPoint(0, 0), new SKPoint(info.Width, 0), new SKPoint(0, info.Height), new SKPoint(info.Width, info.Height) });
            var left = Math.Max(0, (int)Math.Floor(corners.Min(p => p.X)));
            var top = Math.Max(0, (int)Math.Floor(corners.Min(p => p.Y)));
            var right = Math.Min(viewModel.Layers.DocumentWidth, (int)Math.Ceiling(corners.Max(p => p.X)));
            var bottom = Math.Min(viewModel.Layers.DocumentHeight, (int)Math.Ceiling(corners.Max(p => p.Y)));
            if (right <= left || bottom <= top) return;

            using var paint = new SKPaint { Color = new SKColor(128, 128, 128, 70), StrokeWidth = 1f / zoom, IsAntialias = false };
            for (var x = left; x <= right; x++) canvas.DrawLine(x, top, x, bottom, paint);
            for (var y = top; y <= bottom; y++) canvas.DrawLine(left, y, right, y, paint);
        }

        /// <summary>Circle showing the brush size under the pointer (black and white,
        /// so it reads on any image).</summary>
        private static void DrawBrushOutline(SKCanvas canvas, CanvasViewModel viewModel)
        {
            if (viewModel.HoverDocumentPoint is not { } point || viewModel.BrushOutlineDiameter is not { } diameter) return;

            var onScreen = 1f / (float)viewModel.Zoom;
            var radius = diameter / 2f;
            if (radius / onScreen < 3f) return; // tiny brush: the crosshair cursor is enough

            using var dark = new SKPaint { Color = new SKColor(0, 0, 0, 160), Style = SKPaintStyle.Stroke, StrokeWidth = onScreen * 3, IsAntialias = true };
            using var light = new SKPaint { Color = new SKColor(255, 255, 255, 220), Style = SKPaintStyle.Stroke, StrokeWidth = onScreen, IsAntialias = true };
            canvas.DrawCircle(point, radius, dark);
            canvas.DrawCircle(point, radius, light);
        }

        /// <summary>The line being dragged with the Gradient tool, with a dot at each end.</summary>
        /// <summary>The Spot Healing Brush's stroke, shown as a translucent dark band
        /// until the mouse is released and the area is healed.</summary>
        private static void DrawSpotHealingStroke(SKCanvas canvas, CanvasViewModel viewModel)
        {
            if (viewModel.SpotHealingStroke is not { } path) return;

            using var paint = new SKPaint
            {
                Color = new SKColor(0, 0, 0, 110),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = viewModel.SpotHealingStrokeWidth,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                IsAntialias = true,
            };
            canvas.DrawPath(path, paint);
        }

        private static void DrawGradientGuide(SKCanvas canvas, CanvasViewModel viewModel)
        {
            if (viewModel.GradientGuide is not { } guide) return;

            var onScreen = 1f / (float)viewModel.Zoom;
            using var dark = new SKPaint { Color = new SKColor(0, 0, 0, 180), StrokeWidth = onScreen * 3, IsAntialias = true };
            using var light = new SKPaint { Color = SKColors.White, StrokeWidth = onScreen, IsAntialias = true };
            canvas.DrawLine(guide.Start, guide.End, dark);
            canvas.DrawLine(guide.Start, guide.End, light);
            foreach (var point in new[] { guide.Start, guide.End })
            {
                canvas.DrawCircle(point, 4 * onScreen, dark);
                canvas.DrawCircle(point, 3 * onScreen, light);
            }
        }

        /// <summary>Crosshair where the Clone Stamp / Healing Brush is sampling from.</summary>
        private static void DrawCloneSourceMarker(SKCanvas canvas, CanvasViewModel viewModel)
        {
            if (viewModel.CloneSourceMarker is not { } source) return;

            var onScreen = 1f / (float)viewModel.Zoom;
            var arm = 8f * onScreen;
            using var dark = new SKPaint { Color = SKColors.Black, StrokeWidth = onScreen * 3, IsAntialias = true };
            using var light = new SKPaint { Color = SKColors.White, StrokeWidth = onScreen, IsAntialias = true };
            foreach (var paint in new[] { dark, light })
            {
                canvas.DrawLine(source.X - arm, source.Y, source.X + arm, source.Y, paint);
                canvas.DrawLine(source.X, source.Y - arm, source.X, source.Y + arm, paint);
            }
        }

        /// <summary>One 2×2-cell tile, repeated by a shader - a single draw call per frame
        /// instead of one rectangle per cell (tens of thousands on a large document).</summary>
        private static readonly SKShader CheckerShader = CreateCheckerShader();

        private static SKShader CreateCheckerShader()
        {
            var tile = new SKBitmap(CheckerSize * 2, CheckerSize * 2, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var tileCanvas = new SKCanvas(tile))
            using (var darkPaint = new SKPaint { Color = CheckerDark })
            {
                tileCanvas.Clear(CheckerLight);
                tileCanvas.DrawRect(CheckerSize, 0, CheckerSize, CheckerSize, darkPaint);
                tileCanvas.DrawRect(0, CheckerSize, CheckerSize, CheckerSize, darkPaint);
            }

            return SKShader.CreateBitmap(tile, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
        }

        private static void DrawCheckerboard(SKCanvas canvas, SKRect documentRect)
        {
            using var paint = new SKPaint { Shader = CheckerShader, Style = SKPaintStyle.Fill };
            canvas.DrawRect(documentRect, paint);
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

        /// <summary>Draws the Sprite Sheet slicing grid's cell boundaries over the
        /// composited document - a no-op unless the active document is a SpriteSheet
        /// project (<see cref="CanvasViewModel.SpriteSheetGrid"/> null). A thin dashed
        /// line (rather than solid, like the selection marching ants) so it reads as a
        /// guide overlay distinct from an actual selection outline.</summary>
        private static void DrawSpriteSheetGridOverlay(SKCanvas canvas, CanvasViewModel viewModel)
        {
            if (viewModel.SpriteSheetGrid is not { } grid) return;

            var onScreenScale = 1f / (float)viewModel.Zoom;
            using var paint = new SKPaint
            {
                Color = new SKColor(0x00, 0xD8, 0xFF, 200),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = onScreenScale,
                IsAntialias = false,
                PathEffect = SKPathEffect.CreateDash(new[] { 4f * onScreenScale, 4f * onScreenScale }, 0),
            };

            foreach (var (_, _, rect) in grid.EnumerateCells(viewModel.Layers.DocumentWidth, viewModel.Layers.DocumentHeight))
                canvas.DrawRect(rect, paint);
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

        /// <summary>Draws the Pen tool's working path: its Bezier outline (plus a
        /// rubber-band segment to the pointer while anchors are still being placed),
        /// each control handle as a thin line from its anchor with a small round handle
        /// at its end, and each anchor itself as a square handle - the same square/round
        /// visual distinction Free Transform already uses for its own scale-versus-
        /// rotate handles. A no-op while nothing has been drawn with the Pen tool
        /// (<see cref="CanvasViewModel.PenPath"/> is null).</summary>
        private static void DrawPenPathOverlay(SKCanvas canvas, CanvasViewModel viewModel)
        {
            var path = viewModel.PenPath;
            if (path is null || path.Anchors.Count == 0) return;

            var onScreenScale = 1f / (float)viewModel.Zoom;

            using (var pathPaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = onScreenScale, IsAntialias = true })
            using (var skPath = path.ToSKPath())
                canvas.DrawPath(skPath, pathPaint);

            if (viewModel.PenHoverPoint is { } hover)
            {
                using var rubberBandPaint = new SKPaint
                {
                    Color = new SKColor(255, 255, 255, 150),
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = onScreenScale,
                    PathEffect = SKPathEffect.CreateDash(new[] { 3f * onScreenScale, 3f * onScreenScale }, 0),
                };
                canvas.DrawLine(path.Anchors[^1].Position, hover, rubberBandPaint);
            }

            using (var handleLinePaint = new SKPaint { Color = new SKColor(255, 255, 255, 180), StrokeWidth = onScreenScale, IsAntialias = true })
            {
                foreach (var anchor in path.Anchors)
                {
                    if (anchor.InHandle is { } inHandle)
                    {
                        canvas.DrawLine(anchor.Position, inHandle, handleLinePaint);
                        DrawRoundHandle(canvas, inHandle, onScreenScale);
                    }

                    if (anchor.OutHandle is { } outHandle)
                    {
                        canvas.DrawLine(anchor.Position, outHandle, handleLinePaint);
                        DrawRoundHandle(canvas, outHandle, onScreenScale);
                    }
                }
            }

            foreach (var anchor in path.Anchors)
                DrawHandle(canvas, anchor.Position, onScreenScale);
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
