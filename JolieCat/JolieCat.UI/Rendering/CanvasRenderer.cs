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
        private static int CheckerSize = 16;

        // Left as a neutral dark checkerboard, not re-themed to the new warm palette -
        // every raster editor's own universal "no pixel data here" convention, which a
        // checkerboard only reads as at a glance if it stays a plain neutral gray
        // regardless of the app's own current theme.
        private static SKColor CheckerLight = new(0x3A, 0x3A, 0x3D);
        private static SKColor CheckerDark = new(0x2A, 0x2A, 0x2C);

        /// <summary>What the rulers count in (Edit &gt; Preferences).</summary>
        public enum RulerUnit { Pixels, Inches, Centimeters, Percent }

        public static RulerUnit RulerUnits { get; set; } = RulerUnit.Pixels;

        /// <summary>Pixels per inch, for rulers in inches or centimeters.</summary>
        public static double RulerPpi { get; set; } = 72;

        /// <summary>The transparency grid (Edit &gt; Preferences): cell size ("Small", "Medium",
        /// "Large") and colors ("Dark", "Medium", "Light").</summary>
        public static void SetTransparencyGrid(string size, string colors)
        {
            CheckerSize = size switch { "Small" => 8, "Large" => 32, _ => 16 };
            (CheckerLight, CheckerDark) = colors switch
            {
                "Light" => (new SKColor(0xFF, 0xFF, 0xFF), new SKColor(0xCC, 0xCC, 0xCC)),
                "Medium" => (new SKColor(0x9A, 0x9A, 0x9A), new SKColor(0x6E, 0x6E, 0x6E)),
                _ => (new SKColor(0x3A, 0x3A, 0x3D), new SKColor(0x2A, 0x2A, 0x2C)),
            };
            // The old shader isn't disposed: a canvas may still be drawing with it.
            CheckerShader = CreateCheckerShader();
        }

        /// <summary>Document pixels in one ruler unit along x (or y).</summary>
        internal static double PixelsPerUnit(RulerUnit unit, double ppi, int documentSize) => unit switch
        {
            RulerUnit.Inches => Math.Max(1e-6, ppi),
            RulerUnit.Centimeters => Math.Max(1e-6, ppi / 2.54),
            RulerUnit.Percent => Math.Max(1e-6, documentSize / 100.0),
            _ => 1,
        };

        /// <summary>A round ruler step (in units) at least <paramref name="minDevice"/> screen
        /// pixels apart; pixels never go below 1, other units down to 0.1.</summary>
        internal static double UnitStep(double devicePerUnit, bool wholeOnly, double minDevice = 60)
        {
            var raw = minDevice / Math.Max(devicePerUnit, 1e-9);
            var power = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            var step = 10 * power;
            foreach (var m in new[] { 1.0, 2.0, 5.0, 10.0 })
                if (m * power >= raw) { step = m * power; break; }
            return wholeOnly ? Math.Max(1, step) : Math.Max(0.1, step);
        }

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

            // Only the document area shows the image (an animated layer moved past the
            // edge is cut off there, as it will be when exported).
            canvas.Save();
            canvas.ClipRect(documentRect);
            canvas.ResetMatrix();
            if (viewModel.ViewChannel is >= 1 and <= 3)
            {
                // Channels panel: one channel alone, as gray.
                using var channelPaint = new SKPaint { ColorFilter = SKColorFilter.CreateColorMatrix(ViewModels.DocumentPanelsViewModel.ChannelMatrix(viewModel.ViewChannel)) };
                canvas.DrawSurface(frame, 0, 0, channelPaint);
            }
            else
            {
                canvas.DrawSurface(frame, 0, 0);
            }
            canvas.Restore();

            DrawOnionSkin(canvas, viewModel, sampling);
            DrawMotionPath(canvas, viewModel);
            DrawJoint(canvas, viewModel);
            viewModel.DrawLiveShape(canvas);

            DrawQuickMask(canvas, viewModel);
            DrawPixelGrid(canvas, info, viewModel);
            DrawSpriteSheetGridOverlay(canvas, viewModel);
            DrawArtboards(canvas, viewModel);
            DrawSelectionOverlay(canvas, viewModel);
            DrawCropOverlay(canvas, viewModel);
            DrawPerspectiveCropOverlay(canvas, viewModel);
            DrawTransformOverlay(canvas, viewModel);
            DrawWarpOverlay(canvas, viewModel);
            DrawPuppetOverlay(canvas, viewModel);
            DrawPenPathOverlay(canvas, viewModel);
            DrawCloneSourceMarker(canvas, viewModel);
            DrawColorSamplers(canvas, viewModel);
            DrawRuler(canvas, viewModel);
            if (viewModel.ObjectBoxDraft is { } objectBox)
            {
                // The Object Selection box being dragged.
                using var dash = new SKPaint
                {
                    Color = new SKColor(255, 255, 255, 230), Style = SKPaintStyle.Stroke, StrokeWidth = 1 / (float)viewModel.Zoom,
                    PathEffect = SKPathEffect.CreateDash(new[] { 5 / (float)viewModel.Zoom, 3 / (float)viewModel.Zoom }, 0),
                };
                canvas.DrawRect(objectBox, dash);
            }
            if (viewModel.TextBoxDraft is { } textBox)
            {
                // The paragraph text box being dragged out.
                using var dash = new SKPaint
                {
                    Color = new SKColor(255, 255, 255, 220), Style = SKPaintStyle.Stroke, StrokeWidth = 1 / (float)viewModel.Zoom,
                    PathEffect = SKPathEffect.CreateDash(new[] { 4 / (float)viewModel.Zoom, 3 / (float)viewModel.Zoom }, 0),
                };
                canvas.DrawRect(textBox, dash);
            }
            DrawGradientGuide(canvas, viewModel);
            DrawSpotHealingStroke(canvas, viewModel);
            DrawBrushOutline(canvas, viewModel);
            DrawGrid(canvas, info, viewModel);
            DrawGuides(canvas, info, viewModel);

            canvas.Restore();

            DrawRulers(canvas, info, viewModel);
        }

        /// <summary>Perspective Crop: the area outside the four corners dimmed, the
        /// corners joined, a 3x3 grid inside (it shows the perspective), and handles.</summary>
        private static void DrawPerspectiveCropOverlay(SKCanvas canvas, CanvasViewModel viewModel)
        {
            if (viewModel.PerspectiveQuad is not { } q) return;
            var onScreen = 1f / (float)viewModel.Zoom;

            using var shape = new SKPath();
            shape.AddPoly(q, close: true);
            using (var outside = new SKPath())
            {
                outside.AddRect(new SKRect(-1e5f, -1e5f, 1e5f, 1e5f));
                using var dimmed = outside.Op(shape, SKPathOp.Difference);
                using var dim = new SKPaint { Color = new SKColor(0, 0, 0, 110) };
                if (dimmed is not null) canvas.DrawPath(dimmed, dim);
            }

            using var grid = new SKPaint { Color = new SKColor(255, 255, 255, 120), Style = SKPaintStyle.Stroke, StrokeWidth = onScreen, IsAntialias = true };
            static SKPoint Lerp(SKPoint a, SKPoint b, float t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
            for (var k = 1; k < 3; k++)
            {
                var t = k / 3f;
                canvas.DrawLine(Lerp(q[0], q[1], t), Lerp(q[3], q[2], t), grid);
                canvas.DrawLine(Lerp(q[0], q[3], t), Lerp(q[1], q[2], t), grid);
            }

            using var edge = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f * onScreen, IsAntialias = true };
            canvas.DrawPath(shape, edge);
            foreach (var corner in q) DrawHandle(canvas, corner, onScreen);
        }

        /// <summary>Quick Mask: a half-transparent red over everything not selected
        /// (black in the mask), clear where it's white.</summary>
        private static void DrawQuickMask(SKCanvas canvas, CanvasViewModel viewModel)
        {
            if (viewModel.QuickMaskBitmap is not { } mask) return;
            var area = new SKRect(0, 0, mask.Width, mask.Height);
            canvas.SaveLayer(area, null);
            canvas.DrawRect(area, QuickMaskRed);
            using (var cut = new SKPaint { BlendMode = SKBlendMode.DstOut, ColorFilter = SKColorFilter.CreateLumaColor() })
                canvas.DrawBitmap(mask, 0, 0, cut);
            canvas.Restore();
        }

        private static readonly SKPaint QuickMaskRed = new() { Color = new SKColor(255, 0, 0, 128) };

        // ----- Grid, guides, rulers -----

        private static readonly SKColor GuideColor = new(0, 200, 255);

        /// <summary>The document area visible on screen (works for a rotated view too).</summary>
        private static SKRect VisibleDocumentRect(SKImageInfo info, CanvasViewModel viewModel)
        {
            if (!viewModel.ViewMatrix.TryInvert(out var toDocument)) return SKRect.Empty;
            var corners = toDocument.MapPoints(new[] { new SKPoint(0, 0), new SKPoint(info.Width, 0), new SKPoint(0, info.Height), new SKPoint(info.Width, info.Height) });
            return new SKRect(corners.Min(p => p.X), corners.Min(p => p.Y), corners.Max(p => p.X), corners.Max(p => p.Y));
        }

        private static void DrawGrid(SKCanvas canvas, SKImageInfo info, CanvasViewModel viewModel)
        {
            if (!viewModel.ShowGrid || viewModel.GridSize <= 0) return;
            var step = viewModel.GridSize;
            if (step * viewModel.Zoom < 4) return; // too dense to be useful

            var visible = VisibleDocumentRect(info, viewModel);
            var width = viewModel.Layers.DocumentWidth;
            var height = viewModel.Layers.DocumentHeight;
            var left = Math.Max(0, (int)Math.Floor(visible.Left / step) * step);
            var top = Math.Max(0, (int)Math.Floor(visible.Top / step) * step);
            var right = Math.Min(width, visible.Right);
            var bottom = Math.Min(height, visible.Bottom);

            var onScreen = 1f / (float)viewModel.Zoom;
            using var paint = new SKPaint { Color = new SKColor(120, 120, 140, 110), StrokeWidth = onScreen, IsAntialias = false };
            using var major = new SKPaint { Color = new SKColor(90, 90, 120, 170), StrokeWidth = onScreen, IsAntialias = false };
            for (var x = left; x <= right; x += step)
                canvas.DrawLine(x, 0, x, height, x % (step * 4) == 0 ? major : paint);
            for (var y = top; y <= bottom; y += step)
                canvas.DrawLine(0, y, width, y, y % (step * 4) == 0 ? major : paint);
        }

        /// <summary>The Ruler tool's line, with small crosses at its ends.</summary>
        private static void DrawRuler(SKCanvas canvas, CanvasViewModel viewModel)
        {
            if (viewModel.RulerLine is not { } line) return;
            var s = 1f / (float)viewModel.Zoom;
            using var dark = new SKPaint { Color = new SKColor(0, 0, 0, 170), StrokeWidth = 3 * s, IsAntialias = true, Style = SKPaintStyle.Stroke };
            using var light = new SKPaint { Color = new SKColor(255, 255, 255), StrokeWidth = 1.2f * s, IsAntialias = true, Style = SKPaintStyle.Stroke };
            foreach (var paint in new[] { dark, light })
            {
                canvas.DrawLine(line.Start, line.End, paint);
                foreach (var p in new[] { line.Start, line.End })
                {
                    canvas.DrawLine(p.X - 5 * s, p.Y, p.X + 5 * s, p.Y, paint);
                    canvas.DrawLine(p.X, p.Y - 5 * s, p.X, p.Y + 5 * s, paint);
                }
            }
        }

        /// <summary>Each artboard: a frame with its name and size above it.</summary>
        private static void DrawArtboards(SKCanvas canvas, CanvasViewModel viewModel)
        {
            var artboards = viewModel.Layers.Scene.Artboards;
            if (artboards.Count == 0) return;
            var s = 1f / (float)viewModel.Zoom;
            using var shadow = new SKPaint { Color = new SKColor(0, 0, 0, 150), Style = SKPaintStyle.Stroke, StrokeWidth = 3 * s, IsAntialias = true };
            using var frame = new SKPaint { Color = new SKColor(255, 190, 60), Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f * s, IsAntialias = true };
            using var font = new SKFont(SKTypeface.Default, 12 * s);
            using var label = new SKPaint { Color = new SKColor(255, 190, 60), IsAntialias = true };
            using var labelBack = new SKPaint { Color = new SKColor(0, 0, 0, 170) };
            foreach (var a in artboards)
            {
                var r = new SKRect(a.Bounds.Left, a.Bounds.Top, a.Bounds.Right, a.Bounds.Bottom);
                canvas.DrawRect(r, shadow);
                canvas.DrawRect(r, frame);
                var text = $"{a.Name}  {a.Bounds.Width}×{a.Bounds.Height}";
                // Above the frame (inside it at the top of the canvas); the Move tool drags it by this label.
                var labelRect = CanvasViewModel.ArtboardLabelRect(a, viewModel.Zoom);
                canvas.DrawRect(labelRect, labelBack);
                canvas.DrawText(text, r.Left + 4 * s, labelRect.Bottom - 4 * s, font, label);
            }
        }

        private static void DrawGuides(SKCanvas canvas, SKImageInfo info, CanvasViewModel viewModel)
        {
            var dragged = viewModel.DraggedGuide;
            if ((!viewModel.ShowGuides || viewModel.Guides.Count == 0) && dragged is null) return;

            // Guides run across the whole view, past the document's edges, like Photoshop's.
            var visible = VisibleDocumentRect(info, viewModel);
            using var paint = new SKPaint { Color = GuideColor, StrokeWidth = 1f / (float)viewModel.Zoom, IsAntialias = false };

            void Draw(Guide guide)
            {
                if (guide.IsVertical) canvas.DrawLine(guide.Position, visible.Top, guide.Position, visible.Bottom, paint);
                else canvas.DrawLine(visible.Left, guide.Position, visible.Right, guide.Position, paint);
            }

            if (viewModel.ShowGuides)
                foreach (var guide in viewModel.Guides) Draw(guide);
            if (dragged is not null) Draw(dragged);
        }

        /// <summary>A "nice" ruler step (1, 2, 5 x 10^n document pixels) at least
        /// <paramref name="minDevice"/> screen pixels apart.</summary>
        internal static double RulerStep(double zoom, double minDevice = 60)
        {
            var raw = minDevice / Math.Max(zoom, 1e-6);
            var power = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            foreach (var m in new[] { 1.0, 2.0, 5.0, 10.0 })
                if (m * power >= raw) return Math.Max(1, m * power);
            return Math.Max(1, 10 * power);
        }

        private static void DrawRulers(SKCanvas canvas, SKImageInfo info, CanvasViewModel viewModel)
        {
            if (!viewModel.ShowRulers || viewModel.IsViewRotated) return;

            var size = CanvasViewModel.RulerSize;
            var zoom = viewModel.Zoom;
            var origin = viewModel.ViewMatrix.MapPoint(new SKPoint(0, 0)); // document (0,0) on screen
            var whole = RulerUnits == RulerUnit.Pixels;
            var unitX = PixelsPerUnit(RulerUnits, RulerPpi, viewModel.Layers.DocumentWidth);
            var unitY = PixelsPerUnit(RulerUnits, RulerPpi, viewModel.Layers.DocumentHeight);

            using var background = new SKPaint { Color = new SKColor(0x2E, 0x29, 0x29) };
            using var line = new SKPaint { Color = new SKColor(0x8A, 0x83, 0x7C), StrokeWidth = 1, IsAntialias = false };
            using var font = new SKFont(SKTypeface.Default, 10);
            using var text = new SKPaint { Color = new SKColor(0xC9, 0xC2, 0xB8), IsAntialias = true };
            using var cursor = new SKPaint { Color = GuideColor, StrokeWidth = 1, IsAntialias = false };

            canvas.DrawRect(0, 0, info.Width, size, background);
            canvas.DrawRect(0, 0, size, info.Height, background);

            static string Label(double v, double step) => (step < 1 ? Math.Round(v, 1) : Math.Round(v)).ToString(System.Globalization.CultureInfo.InvariantCulture);

            // Top ruler: document x, in ruler units.
            var stepX = UnitStep(zoom * unitX, whole);
            var minorX = stepX / 5;
            var firstX = Math.Floor(-origin.X / zoom / unitX / minorX) * minorX;
            for (var i = 0; i < 4000; i++)
            {
                var v = firstX + i * minorX;
                var x = (float)(origin.X + v * unitX * zoom);
                if (x > info.Width) break;
                if (x < size) continue;
                var isMajor = Math.Abs(v / stepX - Math.Round(v / stepX)) < 1e-6;
                canvas.DrawLine(x, isMajor ? 2 : size - 5, x, size, line);
                if (isMajor) canvas.DrawText(Label(v, stepX), x + 3, 11, font, text);
            }

            // Left ruler: document y, numbers written downwards.
            var stepY = UnitStep(zoom * unitY, whole);
            var minorY = stepY / 5;
            var firstY = Math.Floor(-origin.Y / zoom / unitY / minorY) * minorY;
            for (var i = 0; i < 4000; i++)
            {
                var v = firstY + i * minorY;
                var y = (float)(origin.Y + v * unitY * zoom);
                if (y > info.Height) break;
                if (y < size) continue;
                var isMajor = Math.Abs(v / stepY - Math.Round(v / stepY)) < 1e-6;
                canvas.DrawLine(isMajor ? 2 : size - 5, y, size, y, line);
                if (isMajor)
                {
                    canvas.Save();
                    canvas.RotateDegrees(-90, 11, y - 3);
                    canvas.DrawText(Label(v, stepY), 11, y - 3, font, text);
                    canvas.Restore();
                }
            }

            // Where the pointer is, on both rulers.
            if (viewModel.HoverDocumentPoint is { } hover)
            {
                var p = viewModel.ViewMatrix.MapPoint(hover);
                if (p.X >= size) canvas.DrawLine(p.X, 0, p.X, size, cursor);
                if (p.Y >= size) canvas.DrawLine(0, p.Y, size, p.Y, cursor);
            }

            canvas.DrawLine(0, size, info.Width, size, line);
            canvas.DrawLine(size, 0, size, info.Height, line);
            canvas.DrawRect(0, 0, size, size, background);
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
            // The outline follows the tip's shape: an ellipse turned by its angle.
            var (angle, roundness) = viewModel.BrushOutlineShape;
            canvas.Save();
            canvas.Translate(point.X, point.Y);
            canvas.RotateDegrees(-angle);
            var oval = new SKRect(-radius, -radius * roundness, radius, radius * roundness);
            canvas.DrawOval(oval, dark);
            canvas.DrawOval(oval, light);
            canvas.Restore();
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

        /// <summary>Bones: the selected layer's joint (what it turns around) as a ringed cross.</summary>
        private static void DrawJoint(SKCanvas canvas, CanvasViewModel viewModel)
        {
            var s = 1f / (float)viewModel.Zoom;
            if (viewModel.BendSource?.Invoke() is { } b)
            {
                // The bend point: a pink diamond.
                using var outline = new SKPaint { Color = new SKColor(0, 0, 0, 170), StrokeWidth = 3 * s, IsAntialias = true, Style = SKPaintStyle.Stroke };
                using var pink = new SKPaint { Color = new SKColor(255, 110, 200), StrokeWidth = 1.5f * s, IsAntialias = true, Style = SKPaintStyle.Stroke };
                using var diamond = new SKPath();
                diamond.MoveTo(b.X, b.Y - 7 * s); diamond.LineTo(b.X + 7 * s, b.Y); diamond.LineTo(b.X, b.Y + 7 * s); diamond.LineTo(b.X - 7 * s, b.Y); diamond.Close();
                canvas.DrawPath(diamond, outline);
                canvas.DrawPath(diamond, pink);
            }
            if (viewModel.JointSource?.Invoke() is not { } p) return;
            using var dark = new SKPaint { Color = new SKColor(0, 0, 0, 170), StrokeWidth = 3 * s, IsAntialias = true, Style = SKPaintStyle.Stroke };
            using var light = new SKPaint { Color = new SKColor(120, 220, 255), StrokeWidth = 1.5f * s, IsAntialias = true, Style = SKPaintStyle.Stroke };
            foreach (var paint in new[] { dark, light })
            {
                canvas.DrawCircle(p, 6 * s, paint);
                canvas.DrawLine(p.X - 10 * s, p.Y, p.X + 10 * s, p.Y, paint);
                canvas.DrawLine(p.X, p.Y - 10 * s, p.X, p.Y + 10 * s, paint);
            }
        }

        /// <summary>The selected animated layer's path: a line through its position at every
        /// frame, a dot per frame, a square per keyframe, the current frame ringed.</summary>
        private static void DrawMotionPath(SKCanvas canvas, CanvasViewModel viewModel)
        {
            if (viewModel.MotionPathSource?.Invoke() is not { Count: > 1 } path) return;
            var onScreen = 1f / (float)viewModel.Zoom;
            using var shadow = new SKPaint { Color = new SKColor(0, 0, 0, 140), StrokeWidth = 3 * onScreen, IsAntialias = true, Style = SKPaintStyle.Stroke };
            using var line = new SKPaint { Color = new SKColor(255, 214, 10, 220), StrokeWidth = 1.2f * onScreen, IsAntialias = true, Style = SKPaintStyle.Stroke };
            using var dot = new SKPaint { Color = new SKColor(255, 214, 10), IsAntialias = true };
            using var key = new SKPaint { Color = SKColors.White, IsAntialias = true };
            using var ring = new SKPaint { Color = SKColors.White, StrokeWidth = 1.5f * onScreen, IsAntialias = true, Style = SKPaintStyle.Stroke };

            using (var polyline = new SKPath())
            {
                polyline.MoveTo(path[0].Point);
                for (var i = 1; i < path.Count; i++) polyline.LineTo(path[i].Point);
                canvas.DrawPath(polyline, shadow);
                canvas.DrawPath(polyline, line);
            }
            foreach (var (p, isKey, isCurrent) in path)
            {
                if (isKey)
                {
                    var s = 4 * onScreen;
                    canvas.DrawRect(p.X - s, p.Y - s, 2 * s, 2 * s, shadow);
                    canvas.DrawRect(p.X - s, p.Y - s, 2 * s, 2 * s, key);
                }
                else
                {
                    canvas.DrawCircle(p, 1.8f * onScreen, dot);
                }
                if (isCurrent) canvas.DrawCircle(p, 7 * onScreen, ring);
            }
        }

        /// <summary>Onion skin: the frames before (reddish) and after (greenish) the
        /// current one, faintly over it, for drawing frame-by-frame animation.</summary>
        private static void DrawOnionSkin(SKCanvas canvas, CanvasViewModel viewModel, SKSamplingOptions sampling)
        {
            if (viewModel.OnionSkinSource?.Invoke() is not { Count: > 0 } layers) return;
            foreach (var (layer, opacity, before) in layers)
            {
                // Tint: keep the drawing's shape, color it red (before) or green (after).
                var tint = before ? new SKColor(230, 60, 60) : new SKColor(40, 190, 90);
                using var filter = SKColorFilter.CreateColorMatrix(new[]
                {
                    0.3f, 0.3f, 0.3f, 0, tint.Red / 255f * 0.6f,
                    0.3f, 0.3f, 0.3f, 0, tint.Green / 255f * 0.6f,
                    0.3f, 0.3f, 0.3f, 0, tint.Blue / 255f * 0.6f,
                    0, 0, 0, opacity, 0,
                });
                using var paint = new SKPaint { ColorFilter = filter };
                using var image = SKImage.FromBitmap(layer.Bitmap);
                canvas.DrawImage(image, 0, 0, sampling, paint);
            }
        }

        /// <summary>Color Sampler points: a ringed crosshair with its number.</summary>
        private static void DrawColorSamplers(SKCanvas canvas, CanvasViewModel viewModel)
        {
            if (viewModel.ColorSamplers.Count == 0) return;
            var onScreen = 1f / (float)viewModel.Zoom;
            using var dark = new SKPaint { Color = SKColors.Black, StrokeWidth = onScreen * 3, IsAntialias = true, Style = SKPaintStyle.Stroke };
            using var light = new SKPaint { Color = SKColors.White, StrokeWidth = onScreen, IsAntialias = true, Style = SKPaintStyle.Stroke };
            using var font = new SKFont(SKTypeface.Default, 11 * onScreen);
            using var textDark = new SKPaint { Color = SKColors.Black, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = onScreen * 3 };
            using var textLight = new SKPaint { Color = SKColors.White, IsAntialias = true };

            for (var i = 0; i < viewModel.ColorSamplers.Count; i++)
            {
                var p = viewModel.ColorSamplers[i];
                float ring = 5 * onScreen, arm = 9 * onScreen;
                foreach (var paint in new[] { dark, light })
                {
                    canvas.DrawCircle(p, ring, paint);
                    canvas.DrawLine(p.X - arm, p.Y, p.X - ring, p.Y, paint);
                    canvas.DrawLine(p.X + ring, p.Y, p.X + arm, p.Y, paint);
                    canvas.DrawLine(p.X, p.Y - arm, p.X, p.Y - ring, paint);
                    canvas.DrawLine(p.X, p.Y + ring, p.X, p.Y + arm, paint);
                }
                var label = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                canvas.DrawText(label, p.X + arm, p.Y + arm + 4 * onScreen, font, textDark);
                canvas.DrawText(label, p.X + arm, p.Y + arm + 4 * onScreen, font, textLight);
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
        private static SKShader CheckerShader = CreateCheckerShader();

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

            // Each cell's pivot: a small cross.
            using var pivotPaint = new SKPaint { Color = new SKColor(255, 120, 60, 220), StrokeWidth = 1.5f * onScreenScale, IsAntialias = true };
            var arm = 4f * onScreenScale;
            foreach (var (column, row, rect) in grid.EnumerateCells(viewModel.Layers.DocumentWidth, viewModel.Layers.DocumentHeight))
            {
                var p = grid.PivotIn(rect, row * Math.Max(1, grid.Columns) + column);
                canvas.DrawLine(p.X - arm, p.Y, p.X + arm, p.Y, pivotPaint);
                canvas.DrawLine(p.X, p.Y - arm, p.X, p.Y + arm, pivotPaint);
            }
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

        /// <summary>Puppet Warp: the (bent) mesh, faintly, and the pins.</summary>
        private static void DrawPuppetOverlay(SKCanvas canvas, CanvasViewModel viewModel)
        {
            if (viewModel.Puppet is not { } puppet) return;
            var onScreen = 1f / (float)viewModel.Zoom;
            var mesh = puppet.Mesh;

            if (viewModel.ShowPuppetMesh)
            {
                using var line = new SKPaint { Color = new SKColor(120, 200, 255, 110), StrokeWidth = onScreen, IsAntialias = true };
                for (var r = 0; r < mesh.Rows; r++)
                    for (var c = 0; c < mesh.Columns - 1; c++)
                        canvas.DrawLine(mesh.WarpedGrid[r, c], mesh.WarpedGrid[r, c + 1], line);
                for (var c = 0; c < mesh.Columns; c++)
                    for (var r = 0; r < mesh.Rows - 1; r++)
                        canvas.DrawLine(mesh.WarpedGrid[r, c], mesh.WarpedGrid[r + 1, c], line);
            }

            using var ring = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = onScreen * 1.5f, IsAntialias = true };
            using var fill = new SKPaint { Color = new SKColor(255, 214, 10), IsAntialias = true };
            using var active = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            for (var i = 0; i < puppet.Pins.Count; i++)
            {
                var p = puppet.Pins[i].Current;
                canvas.DrawCircle(p, 5 * onScreen, fill);
                canvas.DrawCircle(p, 5 * onScreen, ring);
                if (i == viewModel.ActivePuppetPin) canvas.DrawCircle(p, 2 * onScreen, active);
            }
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
