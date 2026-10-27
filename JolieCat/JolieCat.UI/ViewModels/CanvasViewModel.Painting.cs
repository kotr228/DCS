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
        // ================= Painting =================

        /// <summary>Bounding box of a stroke segment, padded by the brush radius.</summary>
        private static SKRect StrokeArea(SKPoint from, SKPoint to, float radius)
        {
            var pad = radius + 2;
            return new SKRect(Math.Min(from.X, to.X) - pad, Math.Min(from.Y, to.Y) - pad,
                Math.Max(from.X, to.X) + pad, Math.Max(from.Y, to.Y) + pad);
        }

        /// <summary>Brush/Eraser paint with a single circular "dab", stamped once at the
        /// pointer-down point - the stroke's first mark, before any spacing has a
        /// previous dab to measure from. Pencil instead keeps its plain hard-edged single
        /// pixel (<c>DrawPoint</c>), matching its established always-aliased behavior.</summary>
        private void PaintDot(Core.Documents.Layer layer, SKPoint point)
        {
            if (_toolbox.ActiveTool.Type == ToolType.Pencil)
            {
                using var pencilPaint = CreatePencilPaint();
                WithSelectionClip(layer.PaintCanvas, canvas => canvas.DrawPoint(point, pencilPaint));
                return;
            }

            var size = Math.Max(1f, (float)((_toolbox.CurrentToolOptions as PaintToolOptionsViewModel)?.Size ?? 24));
            var radius = size / 2f;

            using var dabPaint = CreateDabPaint(radius);
            var baseAlpha = dabPaint.Color.Alpha;
            WithSelectionClip(layer.PaintCanvas, canvas => DrawDab(canvas, point, radius, dabPaint, baseAlpha, _lastPressure),
                StrokeArea(point, point, radius));

            // The next dab (from the first PaintLineTo call of this stroke) should land
            // one full spacing interval past this one.
            _distanceUntilNextDab = ComputeDabSpacing(size);
        }

        /// <summary>Brush/Eraser paint between two consecutive pointer-move samples: not
        /// a single stroked line (which can't vary its edge across its own width the way
        /// a soft brush needs to), but a series of circular dabs stamped at even
        /// intervals along the segment - <see cref="_distanceUntilNextDab"/> carries any
        /// leftover distance from one segment into the next so spacing stays even across
        /// the whole stroke, not just within one mouse-move sample. Pencil keeps its
        /// plain hard-edged <c>DrawLine</c>.</summary>
        private void PaintLineTo(Core.Documents.Layer layer, SKPoint from, SKPoint to, float fromPressure = 1f, float toPressure = 1f)
        {
            if (_toolbox.ActiveTool.Type == ToolType.Pencil)
            {
                using var pencilPaint = CreatePencilPaint();
                WithSelectionClip(layer.PaintCanvas, canvas => canvas.DrawLine(from, to, pencilPaint));
                return;
            }

            var size = Math.Max(1f, (float)((_toolbox.CurrentToolOptions as PaintToolOptionsViewModel)?.Size ?? 24));
            var radius = size / 2f;
            var stepDistance = ComputeDabSpacing(size);

            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            var segmentLength = MathF.Sqrt(dx * dx + dy * dy);
            if (segmentLength <= 0f) return;

            using var dabPaint = CreateDabPaint(radius);
            var baseAlpha = dabPaint.Color.Alpha;

            WithSelectionClip(layer.PaintCanvas, canvas =>
            {
                var nextDabAt = _distanceUntilNextDab;
                while (nextDabAt <= segmentLength)
                {
                    var t = nextDabAt / segmentLength;
                    var pressure = fromPressure + (toPressure - fromPressure) * t;
                    DrawDab(canvas, new SKPoint(from.X + dx * t, from.Y + dy * t), radius, dabPaint, baseAlpha, pressure);
                    nextDabAt += stepDistance;
                }

                _distanceUntilNextDab = nextDabAt - segmentLength;
            }, StrokeArea(from, to, radius));
        }

        /// <summary>Stamps one dab centered at <paramref name="center"/>. Translates the
        /// canvas rather than rebuilding <paramref name="dabPaint"/>'s shader per dab
        /// position - <see cref="CreateDabPaint"/>'s radial gradient is centered at the
        /// local origin once, and reused (via this translate) for every dab in a stroke.</summary>
        /// <remarks>Pen pressure is applied per dab without rebuilding the paint: size by
        /// scaling the canvas (the soft-edge gradient scales with it), opacity through the
        /// paint's alpha, which Skia also applies on top of a shader.</remarks>
        private void DrawDab(SKCanvas canvas, SKPoint center, float radius, SKPaint dabPaint, byte baseAlpha, float pressure)
        {
            var options = _toolbox.CurrentToolOptions as PaintToolOptionsViewModel;
            var sizeScale = options?.PressureAffectsSize == true ? Math.Max(0.05f, pressure) : 1f;
            var alphaScale = options?.PressureAffectsOpacity == true ? pressure : 1f;

            dabPaint.Color = dabPaint.Color.WithAlpha((byte)Math.Clamp(baseAlpha * alphaScale, 0, 255));

            if (IsRetouchTool(_toolbox.ActiveTool.Type))
            {
                DrawRetouchDab(canvas, center, radius, sizeScale, dabPaint);
                return;
            }

            canvas.Save();
            canvas.Translate(center.X, center.Y);
            canvas.Scale(sizeScale);
            canvas.DrawCircle(0, 0, radius, dabPaint);
            canvas.Restore();
        }

        /// <summary>Distance (in pixels) between consecutive dabs - Spacing is a percentage
        /// of the brush's own diameter, matching how Spacing is conventionally expressed
        /// (100% spacing = dabs just touching; well under 100% = a smooth continuous
        /// stroke; well over 100% = a visibly dotted/dashed one).</summary>
        private float ComputeDabSpacing(float size)
        {
            var options = _toolbox.CurrentToolOptions as PaintToolOptionsViewModel;
            var spacingPercent = Math.Clamp(options?.Spacing ?? 20, 1, 500);
            return Math.Max(1f, size * (float)(spacingPercent / 100.0));
        }

        /// <summary>One circular dab's fill paint: solid out to Hardness% of its radius,
        /// then fading linearly to fully transparent at the edge - a real soft/hard brush
        /// edge via a radial-gradient alpha falloff, rather than a flat hard-edged circle
        /// with no Hardness control at all. Hardness 100 skips the gradient entirely (a
        /// plain solid fill) since a fade ending exactly at the edge is indistinguishable
        /// from none, and the eraser gets the same falloff (through its established
        /// DstOut blend mode) so it can un-paint just as softly as the brush paints.</summary>
        private SKPaint CreateDabPaint(float radius)
        {
            var isEraser = _toolbox.ActiveTool.Type == ToolType.Eraser;
            var options = _toolbox.CurrentToolOptions as PaintToolOptionsViewModel;
            var hardness = Math.Clamp(options?.Hardness ?? 100, 0, 100);
            var opacityPercent = options?.Opacity ?? 100;
            var alpha = (byte)Math.Clamp(opacityPercent / 100.0 * 255, 0, 255);

            // Color only matters for its alpha channel when erasing - DstOut (not Clear)
            // removes destination alpha in proportion to the source's, so the eraser's
            // Opacity slider genuinely does partial erasing instead of always punching
            // fully through regardless of it; RGB is irrelevant. See PaintDot/PaintLineTo's
            // callers for why this is shared with the brush's own coloring.
            var color = isEraser ? SKColors.Black.WithAlpha(alpha) : BrushColor.WithAlpha(alpha);

            var paint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                IsAntialias = true,
                BlendMode = isEraser ? SKBlendMode.DstOut : SKBlendMode.SrcOver,
            };

            if (hardness >= 100 || radius <= 0f)
            {
                paint.Color = color;
            }
            else
            {
                // Centered at the local origin (0,0), not the dab's actual canvas
                // position - DrawDab translates the canvas per dab instead of rebuilding
                // this shader for every dab's position.
                paint.Shader = SKShader.CreateRadialGradient(
                    new SKPoint(0, 0), radius,
                    new[] { color, color, color.WithAlpha(0) },
                    new[] { 0f, (float)(hardness / 100.0), 1f },
                    SKShaderTileMode.Clamp);
            }

            return paint;
        }

        /// <summary>Pencil's plain hard-edged, non-antialiased stroke paint - unaffected
        /// by Hardness/Spacing, which only apply to the Brush/Eraser's dab-based painting.</summary>
        private SKPaint CreatePencilPaint()
        {
            var options = _toolbox.CurrentToolOptions as PaintToolOptionsViewModel;
            var size = (float)(options?.Size ?? 24);
            var opacityPercent = options?.Opacity ?? 100;
            var alpha = (byte)Math.Clamp(opacityPercent / 100.0 * 255, 0, 255);

            return new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(1f, size),
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                IsAntialias = false,
                BlendMode = SKBlendMode.SrcOver,
                Color = BrushColor.WithAlpha(alpha),
            };
        }

        private void FloodFill(Core.Documents.Layer layer, SKPoint point)
        {
            var bitmap = layer.PaintBitmap;
            var x0 = (int)point.X;
            var y0 = (int)point.Y;
            if (x0 < 0 || y0 < 0 || x0 >= bitmap.Width || y0 >= bitmap.Height)
                return;

            var selection = Layers.Scene.Selection;
            if (!selection.Contains(x0, y0))
                return;

            var options = _toolbox.CurrentToolOptions as FillToolOptionsViewModel;
            var tolerance = (float)(options?.Tolerance ?? 32);
            var contiguous = options?.Contiguous ?? true;

            var width = bitmap.Width;
            var height = bitmap.Height;
            var pixels = bitmap.Pixels;
            var targetColor = pixels[y0 * width + x0];
            var fillColor = BrushColor;

            if (ColorTolerance.IsWithin(targetColor, fillColor, 0))
                return;

            if (contiguous)
                FloodFillContiguous(pixels, width, height, x0, y0, targetColor, fillColor, tolerance, selection);
            else
                FloodFillGlobal(pixels, width, height, targetColor, fillColor, tolerance, selection);

            bitmap.Pixels = pixels;
        }

        /// <summary>
        /// Stack-based flood fill, seeded from (x0, y0). Each pixel is marked visited
        /// the moment it's pushed - not when it's popped - so it can only ever enter the
        /// stack once, bounding the stack to one entry per pixel instead of letting
        /// duplicates pile up on a large uniform region (a fresh, blank layer is exactly
        /// that).
        /// </summary>
        private static void FloodFillContiguous(
            SKColor[] pixels, int width, int height, int x0, int y0,
            SKColor targetColor, SKColor fillColor, float tolerance, Selection selection)
        {
            var visited = new bool[width * height];
            var stack = new Stack<(int X, int Y)>();

            visited[y0 * width + x0] = true;
            stack.Push((x0, y0));

            while (stack.Count > 0)
            {
                var (x, y) = stack.Pop();
                pixels[y * width + x] = fillColor;

                TryVisit(x + 1, y);
                TryVisit(x - 1, y);
                TryVisit(x, y + 1);
                TryVisit(x, y - 1);
            }

            void TryVisit(int x, int y)
            {
                if (x < 0 || x >= width || y < 0 || y >= height)
                    return;

                var index = y * width + x;
                if (visited[index] || !selection.Contains(x, y) || !ColorTolerance.IsWithin(pixels[index], targetColor, tolerance))
                    return;

                visited[index] = true;
                stack.Push((x, y));
            }
        }

        private static void FloodFillGlobal(SKColor[] pixels, int width, int height, SKColor targetColor, SKColor fillColor, float tolerance, Selection selection)
        {
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = y * width + x;
                    if (selection.Contains(x, y) && ColorTolerance.IsWithin(pixels[index], targetColor, tolerance))
                        pixels[index] = fillColor;
                }
            }
        }
    }
}
