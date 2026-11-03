using System;
using JolieCat.Core.Documents;
using SkiaSharp;
using LayerStyleRef = JolieCat.Core.Effects.LayerStyle;
using PatternTilesRef = JolieCat.Core.Effects.PatternTiles;

namespace JolieCat.Core.Rendering
{
    /// <summary>
    /// Composites a <see cref="Scene"/>'s layers - respecting visibility, opacity, blend
    /// mode, and (if present and enabled) each layer's mask - onto a destination canvas,
    /// or into a brand new flattened bitmap. The one place this logic lives: shared by
    /// <c>JolieCat.UI</c>'s live canvas renderer, <see cref="Scene.MergeLayerDown"/>, and
    /// <c>JolieCat.Core</c>'s image-export pipeline, so masking behaves identically
    /// everywhere instead of drifting apart across independent copies.
    /// </summary>
    public static class SceneCompositor
    {
        /// <summary>Draws every visible layer in <paramref name="scene"/>, back-to-front,
        /// onto <paramref name="canvas"/> - the document-space compositing step shared by
        /// the live canvas (inside its own pan/zoom transform) and a flattened export
        /// (drawn 1:1 with no transform at all). If <paramref name="previewLayer"/> is
        /// given, its content is drawn from <paramref name="previewBitmap"/> instead of
        /// its own (untouched) <see cref="Layer.Bitmap"/> - still in its normal position
        /// in the stack, and still with its own opacity/blend mode/mask - used by the
        /// Free Transform/Warp tools' live preview so a layer mid-transform renders
        /// correctly relative to the layers above and below it, rather than the preview
        /// only ever being drawable on top of everything.</summary>
        public static void DrawLayers(SKCanvas canvas, Scene scene, int documentWidth, int documentHeight, Layer? previewLayer = null, SKBitmap? previewBitmap = null) =>
            DrawLayers(canvas, scene, 0, scene?.Layers.Count ?? 0, documentWidth, documentHeight, default, null, previewLayer, previewBitmap);

        /// <summary>Composites every visible layer onto <paramref name="target"/> (which
        /// must be document-sized), adjustment layers included - they read back what's
        /// already in the bitmap.</summary>
        public static void DrawLayersInto(SKBitmap target, Scene scene, int from = 0, int to = int.MaxValue)
        {
            ArgumentNullException.ThrowIfNull(target);
            using var canvas = new SKCanvas(target);
            DrawLayers(canvas, scene, from, Math.Min(to, scene.Layers.Count), target.Width, target.Height, default, () => SKImage.FromBitmap(target));
        }

        /// <summary>Draws the visible layers with index in [<paramref name="from"/>,
        /// <paramref name="to"/>) - used by the live canvas to cache the layers below
        /// and above the active one separately. <paramref name="sampling"/> controls
        /// filtering when the canvas is scaled (default: nearest neighbour).</summary>
        /// <param name="backdrop">Returns a snapshot of everything drawn on the canvas so
        /// far, in the canvas's device pixels - what adjustment layers filter. Without
        /// it adjustment layers are skipped.</param>
        public static void DrawLayers(SKCanvas canvas, Scene scene, int from, int to, int documentWidth, int documentHeight,
            SKSamplingOptions sampling, Func<SKImage>? backdrop, Layer? previewLayer = null, SKBitmap? previewBitmap = null)
        {
            ArgumentNullException.ThrowIfNull(canvas);
            ArgumentNullException.ThrowIfNull(scene);

            DrawRange(canvas, scene, Math.Max(0, from), Math.Min(to, scene.Layers.Count), null,
                documentWidth, documentHeight, sampling, backdrop, previewLayer, previewBitmap);
        }

        /// <summary>
        /// Draws layers [lo, hi) that belong to <paramref name="parentId"/> (null = top
        /// level). A group's members come right below it in the list, so meeting one
        /// means the whole group starts here: it's drawn as one unit (through
        /// <see cref="DrawGroup"/>) and the loop continues after the group's own entry.
        /// </summary>
        private static void DrawRange(SKCanvas canvas, Scene scene, int lo, int hi, Guid? parentId,
            int documentWidth, int documentHeight, SKSamplingOptions sampling, Func<SKImage>? backdrop,
            Layer? previewLayer, SKBitmap? previewBitmap)
        {
            var i = lo;
            while (i < hi)
            {
                var layer = scene.Layers[i];
                var unit = scene.MemberOf(layer, parentId) ?? layer;

                if (unit.IsGroup)
                {
                    var groupIndex = scene.IndexOf(unit);
                    if (groupIndex < i) groupIndex = i; // defensive: a broken structure never loops
                    if (unit.IsVisible)
                        DrawGroup(canvas, scene, unit, i, Math.Min(groupIndex, hi), documentWidth, documentHeight,
                            sampling, backdrop, previewLayer, previewBitmap);
                    i = groupIndex + 1;
                    continue;
                }

                // A clipping group: this layer plus the clipped layers right above it.
                var clipEnd = i;
                if (!layer.IsClipped && !layer.IsAdjustment)
                    while (clipEnd + 1 < hi && scene.Layers[clipEnd + 1] is { IsClipped: true, IsGroup: false } next && next.ParentId == layer.ParentId)
                        clipEnd++;

                if (clipEnd > i)
                {
                    if (layer.IsVisible)
                        DrawClippingGroup(canvas, scene, i, clipEnd, documentWidth, documentHeight, sampling, backdrop, previewLayer, previewBitmap);
                    i = clipEnd + 1;
                    continue;
                }

                if (layer.IsVisible)
                {
                    var bitmap = ReferenceEquals(layer, previewLayer) && previewBitmap is not null ? previewBitmap : layer.Bitmap;
                    DrawLayer(canvas, layer, bitmap, documentWidth, documentHeight, sampling, backdrop);
                }
                i++;
            }
        }

        /// <summary>
        /// Draws a base layer (index <paramref name="baseIndex"/>) with the clipped
        /// layers above it, up to <paramref name="lastIndex"/>. Everything is drawn
        /// together - the base at full strength, each clipped layer with its own
        /// opacity and blend mode - then cut down to the base's own shape, and only
        /// then blended onto the canvas with the base's opacity and blend mode
        /// (Photoshop's clipping groups behave the same way).
        /// </summary>
        private static void DrawClippingGroup(SKCanvas canvas, Scene scene, int baseIndex, int lastIndex,
            int documentWidth, int documentHeight, SKSamplingOptions sampling, Func<SKImage>? backdrop,
            Layer? previewLayer, SKBitmap? previewBitmap)
        {
            var baseLayer = scene.Layers[baseIndex];
            var baseBitmap = ReferenceEquals(baseLayer, previewLayer) && previewBitmap is not null ? previewBitmap : baseLayer.Bitmap;
            var bounds = new SKRect(0, 0, documentWidth, documentHeight);

            using var groupPaint = new SKPaint
            {
                Color = SKColors.White.WithAlpha((byte)Math.Clamp(baseLayer.Opacity * 255.0, 0, 255)),
                BlendMode = Layer.ToSkiaBlendMode(baseLayer.BlendMode),
            };
            canvas.SaveLayer(bounds, groupPaint);

            DrawLayerAsBase(canvas, baseLayer, baseBitmap, documentWidth, documentHeight, sampling);

            for (var j = baseIndex + 1; j <= lastIndex; j++)
            {
                var clipped = scene.Layers[j];
                if (!clipped.IsVisible) continue;
                var bitmap = ReferenceEquals(clipped, previewLayer) && previewBitmap is not null ? previewBitmap : clipped.Bitmap;
                DrawLayer(canvas, clipped, bitmap, documentWidth, documentHeight, sampling, backdrop);
            }

            // Keep only what lies on the base's own pixels (its mask included).
            using (var cut = new SKPaint { BlendMode = SKBlendMode.DstIn })
            {
                canvas.SaveLayer(bounds, cut);
                DrawBitmap(canvas, baseBitmap, sampling, null);
                if (baseLayer.Mask is { IsEnabled: true } mask)
                {
                    using var maskPaint = new SKPaint { BlendMode = SKBlendMode.DstIn, ColorFilter = SKColorFilter.CreateLumaColor() };
                    DrawBitmap(canvas, mask.Bitmap, sampling, maskPaint);
                }
                canvas.Restore();
            }

            canvas.Restore();
        }

        /// <summary>The base of a clipping group: its pixels (masked, styled) at full
        /// opacity and Normal blending - its own opacity/blend apply to the whole group.</summary>
        private static void DrawLayerAsBase(SKCanvas canvas, Layer layer, SKBitmap bitmap, int documentWidth, int documentHeight, SKSamplingOptions sampling)
        {
            var bounds = new SKRect(0, 0, documentWidth, documentHeight);
            if (layer.Style is { HasAnyEffect: true } style)
            {
                DrawStyled(canvas, layer, bitmap, style, bounds, null, sampling, applyMask: true);
                return;
            }

            canvas.SaveLayer(bounds, null);
            DrawBitmap(canvas, bitmap, sampling, null);
            if (layer.Mask is { IsEnabled: true } mask)
            {
                using var maskPaint = new SKPaint { BlendMode = SKBlendMode.DstIn, ColorFilter = SKColorFilter.CreateLumaColor() };
                DrawBitmap(canvas, mask.Bitmap, sampling, maskPaint);
            }
            canvas.Restore();
        }

        /// <summary>A Normal, fully opaque group just draws its members in place ("pass
        /// through"); otherwise they're composited together first, then blended onto
        /// what's below with the group's opacity and blend mode.</summary>
        private static void DrawGroup(SKCanvas canvas, Scene scene, Layer group, int lo, int hi,
            int documentWidth, int documentHeight, SKSamplingOptions sampling, Func<SKImage>? backdrop,
            Layer? previewLayer, SKBitmap? previewBitmap)
        {
            if (group.Opacity <= 0) return;

            var mask = group.Mask is { IsEnabled: true } m ? m : null;

            if (group.Style is { HasAnyEffect: true } style)
            {
                // A styled group is drawn on its own first, then its effects are built
                // from that combined picture - like Photoshop, the group is isolated.
                using var merged = new SKBitmap(documentWidth, documentHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
                merged.Erase(SKColors.Transparent);
                using (var groupCanvas = new SKCanvas(merged))
                {
                    DrawRange(groupCanvas, scene, lo, hi, group.Id, documentWidth, documentHeight, sampling, null, previewLayer, previewBitmap);
                    if (mask is not null)
                    {
                        using var maskPaint = new SKPaint { BlendMode = SKBlendMode.DstIn, ColorFilter = SKColorFilter.CreateLumaColor() };
                        DrawBitmap(groupCanvas, mask.Bitmap, sampling, maskPaint);
                    }
                }

                using var groupPaint = new SKPaint
                {
                    Color = SKColors.White.WithAlpha((byte)Math.Clamp(group.Opacity * 255.0, 0, 255)),
                    BlendMode = Layer.ToSkiaBlendMode(group.BlendMode),
                };
                DrawStyled(canvas, group, merged, style, new SKRect(0, 0, documentWidth, documentHeight), groupPaint, default, applyMask: false);
                return;
            }

            var isolated = group.BlendMode != Shared.Enums.BlendMode.Normal || group.Opacity < 1 || mask is not null;
            if (isolated)
            {
                using var paint = new SKPaint
                {
                    Color = SKColors.White.WithAlpha((byte)Math.Clamp(group.Opacity * 255.0, 0, 255)),
                    BlendMode = Layer.ToSkiaBlendMode(group.BlendMode),
                };
                canvas.SaveLayer(new SKRect(0, 0, documentWidth, documentHeight), paint);
            }

            DrawRange(canvas, scene, lo, hi, group.Id, documentWidth, documentHeight, sampling, backdrop, previewLayer, previewBitmap);

            if (mask is not null)
            {
                // The group's mask hides parts of everything inside it.
                using var maskPaint = new SKPaint { BlendMode = SKBlendMode.DstIn, ColorFilter = SKColorFilter.CreateLumaColor() };
                DrawBitmap(canvas, mask.Bitmap, sampling, maskPaint);
            }

            if (isolated) canvas.Restore();
        }

        /// <summary>Draws a group's members alone - without the group's own opacity and
        /// blend mode (Merge Group applies those to the result layer instead).</summary>
        public static void DrawGroupMembers(SKCanvas canvas, Scene scene, Layer group, int documentWidth, int documentHeight, Func<SKImage>? backdrop)
        {
            var (start, end) = scene.BlockOf(group);
            DrawRange(canvas, scene, start, end, group.Id, documentWidth, documentHeight, default, backdrop, null, null);
        }

        /// <summary>Draws one layer - masked, if it has an enabled mask - onto
        /// <paramref name="canvas"/> with its own opacity and blend mode.</summary>
        public static void DrawLayer(SKCanvas canvas, Layer layer, int documentWidth, int documentHeight) =>
            DrawLayer(canvas, layer, layer.Bitmap, documentWidth, documentHeight);

        /// <summary>Draws one layer exactly like <see cref="DrawLayer(SKCanvas, Layer, int, int)"/>,
        /// but sourcing its color content from <paramref name="bitmap"/> instead of the
        /// layer's own <see cref="Layer.Bitmap"/> - <paramref name="layer"/> still
        /// supplies its own opacity, blend mode, and mask. Lets a caller substitute a
        /// live-transformed/warped preview bitmap for one layer while every other
        /// layer (and this one's own compositing behavior) stays exactly as it would
        /// normally be.</summary>
        public static void DrawLayer(SKCanvas canvas, Layer layer, SKBitmap bitmap, int documentWidth, int documentHeight) =>
            DrawLayer(canvas, layer, bitmap, documentWidth, documentHeight, default, null);

        public static void DrawLayer(SKCanvas canvas, Layer layer, SKBitmap bitmap, int documentWidth, int documentHeight,
            SKSamplingOptions sampling, Func<SKImage>? backdrop)
        {
            if (layer.IsGroup) return; // groups have no pixels; DrawLayers draws their members

            if (layer.Adjustment is not null)
            {
                DrawAdjustment(canvas, layer, documentWidth, documentHeight, sampling, backdrop);
                return;
            }

            using var layerPaint = new SKPaint
            {
                Color = SKColors.White.WithAlpha((byte)Math.Clamp(layer.Opacity * 255.0, 0, 255)),
                BlendMode = Layer.ToSkiaBlendMode(layer.BlendMode),
            };

            if (layer.Style is { HasAnyEffect: true } style)
            {
                DrawStyled(canvas, layer, bitmap, style, new SKRect(0, 0, documentWidth, documentHeight), layerPaint, sampling, applyMask: true);
                return;
            }

            if (layer.Mask is { IsEnabled: true } mask)
            {
                // The mask has to clip this layer's own alpha *before* its opacity/blend
                // mode are applied against whatever's already on `canvas`. SaveLayer gives
                // an offscreen buffer only as large as the visible clip (not the whole
                // document), and applies layerPaint's opacity/blend when it's restored.
                var bounds = new SKRect(0, 0, documentWidth, documentHeight);
                canvas.SaveLayer(bounds, layerPaint);
                DrawBitmap(canvas, bitmap, sampling, null);

                // CreateLumaColor turns the grayscale mask into alpha; DstIn multiplies that
                // alpha into the layer content drawn just above (standard Skia luma-mask idiom).
                using (var maskPaint = new SKPaint { BlendMode = SKBlendMode.DstIn, ColorFilter = SKColorFilter.CreateLumaColor() })
                    DrawBitmap(canvas, mask.Bitmap, sampling, maskPaint);

                canvas.Restore();
            }
            else
            {
                DrawBitmap(canvas, bitmap, sampling, layerPaint);
            }
        }

        /// <summary>
        /// Draws a layer with its layer style. Bottom to top: drop shadow, outer glow,
        /// stroke (outside), then the content itself, tinted by the color overlay. All
        /// of it is grouped so the layer's opacity and blend mode apply to the whole.
        /// Each effect is the layer's (masked) pixels drawn through a Skia image filter,
        /// so the effects follow whatever is painted on the layer.
        /// </summary>
        private static void DrawStyled(SKCanvas canvas, Layer layer, SKBitmap bitmap, LayerStyleRef style, SKRect bounds,
            SKPaint? groupPaint, SKSamplingOptions sampling, bool applyMask)
        {
            var mask = applyMask && layer.Mask is { IsEnabled: true } m ? m : null;

            void Content(SKPaint? paint)
            {
                canvas.SaveLayer(bounds, paint);
                DrawBitmap(canvas, bitmap, sampling, null);
                if (mask is not null)
                {
                    using var maskPaint = new SKPaint { BlendMode = SKBlendMode.DstIn, ColorFilter = SKColorFilter.CreateLumaColor() };
                    DrawBitmap(canvas, mask.Bitmap, sampling, maskPaint);
                }
                canvas.Restore();
            }

            canvas.SaveLayer(bounds, groupPaint);

            if (style.DropShadow && style.ShadowOpacity > 0)
            {
                var offset = style.ShadowOffset;
                var sigma = LayerStyleRef.Sigma(style.ShadowSize);
                using var filter = SKImageFilter.CreateDropShadowOnly(offset.X, offset.Y, sigma, sigma,
                    style.ShadowColor.WithAlpha(LayerStyleRef.Alpha(style.ShadowOpacity)));
                using var paint = new SKPaint { ImageFilter = filter };
                Content(paint);
            }

            if (style.OuterGlow && style.GlowOpacity > 0 && style.GlowSize > 0)
            {
                // Spread the shape a little, color it, then blur: a glow that stays
                // visible right at the edge instead of a faint halo.
                var spread = (float)(style.GlowSize / 4);
                using var tint = SKColorFilter.CreateBlendMode(style.GlowColor.WithAlpha(LayerStyleRef.Alpha(style.GlowOpacity)), SKBlendMode.SrcIn);
                using var dilate = SKImageFilter.CreateDilate(spread, spread);
                using var colored = SKImageFilter.CreateColorFilter(tint, dilate);
                var sigma = LayerStyleRef.Sigma(style.GlowSize);
                using var filter = SKImageFilter.CreateBlur(sigma, sigma, colored);
                using var paint = new SKPaint { ImageFilter = filter };
                Content(paint);
            }

            if (style.Stroke && style.StrokeSize > 0 && style.StrokeOpacity > 0)
            {
                var radius = (float)style.StrokeSize;
                using var tint = SKColorFilter.CreateBlendMode(style.StrokeColor.WithAlpha(LayerStyleRef.Alpha(style.StrokeOpacity)), SKBlendMode.SrcIn);
                using var dilate = SKImageFilter.CreateDilate(radius, radius);
                using var filter = SKImageFilter.CreateColorFilter(tint, dilate);
                using var paint = new SKPaint { ImageFilter = filter };
                Content(paint);
            }

            if (!style.HasInnerEffect)
            {
                Content(null);
                canvas.Restore();
                return;
            }

            // An effect painted only on the layer's own pixels: `fill` draws into a layer
            // that is then laid on with SrcATop, which keeps it inside the shape.
            void Inside(Action fill)
            {
                using var atop = new SKPaint { BlendMode = SKBlendMode.SrcATop };
                canvas.SaveLayer(bounds, atop);
                fill();
                canvas.Restore();
            }

            // Fills `color`, then cuts away the layer's shape run through `filter`; what
            // remains is a band along the inside of the edge (inner shadow/glow, bevel).
            void InnerBand(SKColor color, SKImageFilter filter) => Inside(() =>
            {
                canvas.DrawColor(color);
                using var cut = new SKPaint { BlendMode = SKBlendMode.DstOut, ImageFilter = filter };
                Content(cut);
            });

            // The content and what's painted inside it share one layer, so inner effects
            // can't spill onto the shadow/glow/stroke underneath.
            canvas.SaveLayer(bounds, null);
            Content(null);

            if (style.ColorOverlay && style.OverlayOpacity > 0)
                Inside(() => canvas.DrawColor(style.OverlayColor.WithAlpha(LayerStyleRef.Alpha(style.OverlayOpacity))));

            if (style.GradientOverlay && style.GradientOpacity > 0)
            {
                Inside(() =>
                {
                    // Like Photoshop, the gradient spans the layer's content, not the canvas.
                    var area = ContentBounds(bitmap) is { IsEmpty: false } content ? content : bounds;
                    var (start, end) = GradientLine(area, style.GradientAngle);
                    using var shader = SKShader.CreateLinearGradient(start, end, new[] { style.GradientStart, style.GradientEnd }, SKShaderTileMode.Clamp);
                    using var paint = new SKPaint { Shader = shader, Color = SKColors.White.WithAlpha(LayerStyleRef.Alpha(style.GradientOpacity)) };
                    canvas.DrawRect(bounds, paint);
                });
            }

            if (style.PatternOverlay && style.PatternOpacity > 0)
            {
                Inside(() =>
                {
                    using var tile = PatternTilesRef.Create(style.PatternName, style.PatternColor);
                    var scale = (float)Math.Max(0.05, style.PatternScale / 100);
                    using var shader = SKShader.CreateBitmap(tile, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, SKMatrix.CreateScale(scale, scale));
                    using var paint = new SKPaint { Shader = shader, Color = SKColors.White.WithAlpha(LayerStyleRef.Alpha(style.PatternOpacity)) };
                    canvas.DrawRect(bounds, paint);
                });
            }

            if (style.InnerGlow && style.InnerGlowOpacity > 0 && style.InnerGlowSize > 0)
            {
                var shrink = (float)(style.InnerGlowSize / 4);
                var sigma = LayerStyleRef.Sigma(style.InnerGlowSize);
                using var erode = SKImageFilter.CreateErode(shrink, shrink);
                using var blur = SKImageFilter.CreateBlur(sigma, sigma, erode);
                InnerBand(style.InnerGlowColor.WithAlpha(LayerStyleRef.Alpha(style.InnerGlowOpacity)), blur);
            }

            if (style.InnerShadow && style.InnerShadowOpacity > 0)
            {
                var offset = style.InnerShadowOffset;
                var sigma = LayerStyleRef.Sigma(style.InnerShadowSize);
                using var move = SKImageFilter.CreateOffset(offset.X, offset.Y);
                using var blur = SKImageFilter.CreateBlur(sigma, sigma, move);
                InnerBand(style.InnerShadowColor.WithAlpha(LayerStyleRef.Alpha(style.InnerShadowOpacity)), blur);
            }

            if (style.Bevel && style.BevelSize > 0)
            {
                // A lit edge facing the light and a dark edge on the far side.
                var offset = style.BevelOffset;
                var sigma = LayerStyleRef.Sigma(style.BevelSize);
                if (style.BevelHighlightOpacity > 0)
                {
                    using var move = SKImageFilter.CreateOffset(offset.X, offset.Y);
                    using var blur = SKImageFilter.CreateBlur(sigma, sigma, move);
                    InnerBand(style.BevelHighlight.WithAlpha(LayerStyleRef.Alpha(style.BevelHighlightOpacity)), blur);
                }
                if (style.BevelShadowOpacity > 0)
                {
                    using var move = SKImageFilter.CreateOffset(-offset.X, -offset.Y);
                    using var blur = SKImageFilter.CreateBlur(sigma, sigma, move);
                    InnerBand(style.BevelShadow.WithAlpha(LayerStyleRef.Alpha(style.BevelShadowOpacity)), blur);
                }
            }

            canvas.Restore();
            canvas.Restore();
        }

        /// <summary>The smallest rectangle holding every non-transparent pixel of
        /// <paramref name="bitmap"/> (empty when there are none).</summary>
        public static SKRect ContentBounds(SKBitmap bitmap)
        {
            if (bitmap.BytesPerPixel != 4) return new SKRect(0, 0, bitmap.Width, bitmap.Height);
            var bytes = bitmap.GetPixelSpan();
            var w = bitmap.Width;
            var rowBytes = bitmap.RowBytes;
            const int alphaOffset = 3; // RGBA and BGRA both keep alpha last
            int minX = w, minY = -1, maxX = -1, maxY = -1;
            for (var y = 0; y < bitmap.Height; y++)
            {
                var row = bytes.Slice(y * rowBytes, w * 4);
                var first = -1;
                for (var x = 0; x < w; x++)
                    if (row[x * 4 + alphaOffset] != 0) { first = x; break; }
                if (first < 0) continue;
                var last = first;
                for (var x = w - 1; x > first; x--)
                    if (row[x * 4 + alphaOffset] != 0) { last = x; break; }
                if (minY < 0) minY = y;
                maxY = y;
                minX = Math.Min(minX, first);
                maxX = Math.Max(maxX, last);
            }
            return minY < 0 ? SKRect.Empty : new SKRect(minX, minY, maxX + 1, maxY + 1);
        }

        /// <summary>The ends of a gradient across <paramref name="bounds"/> at
        /// <paramref name="angle"/> degrees (90 = from the bottom up).</summary>
        internal static (SKPoint Start, SKPoint End) GradientLine(SKRect bounds, double angle)
        {
            var radians = angle * Math.PI / 180.0;
            var dx = (float)Math.Cos(radians);
            var dy = (float)-Math.Sin(radians);
            // Half the bounds' extent along the direction, so the ends reach the corners.
            var half = (Math.Abs(dx) * bounds.Width + Math.Abs(dy) * bounds.Height) / 2;
            var center = new SKPoint(bounds.MidX, bounds.MidY);
            return (new SKPoint(center.X - dx * half, center.Y - dy * half), new SKPoint(center.X + dx * half, center.Y + dy * half));
        }

        /// <summary>Renders one layer alone - full opacity, Normal blend - with its
        /// layer style baked in. <paramref name="applyMask"/> false leaves the mask
        /// out (PSD export writes the mask separately).</summary>
        public static SKBitmap RenderStyledLayer(Layer layer, bool applyMask)
        {
            ArgumentNullException.ThrowIfNull(layer);
            var result = new SKBitmap(layer.Bitmap.Width, layer.Bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            result.Erase(SKColors.Transparent);
            using var canvas = new SKCanvas(result);
            var style = layer.Style ?? new LayerStyleRef();
            DrawStyled(canvas, layer, layer.Bitmap, style, new SKRect(0, 0, result.Width, result.Height), null, default, applyMask);
            return result;
        }

        /// <summary>
        /// Applies an adjustment layer to what's already on <paramref name="canvas"/>.
        /// With Normal blending the result is an exact blend
        /// below * (1 - k) + adjusted * k, where k is mask * opacity: first the covered
        /// part of the canvas is faded out (DstOut), then the adjusted copy is added
        /// back (Plus) - so semi-transparent areas keep their alpha. Other blend modes
        /// blend the adjusted copy over the original with that mode.
        /// </summary>
        private static void DrawAdjustment(SKCanvas canvas, Layer layer, int documentWidth, int documentHeight,
            SKSamplingOptions sampling, Func<SKImage>? backdrop)
        {
            if (backdrop is null || layer.Adjustment is not { } adjustment || layer.Opacity <= 0) return;

            using var below = backdrop();
            using var filter = adjustment.CreateColorFilter();
            var bounds = new SKRect(0, 0, documentWidth, documentHeight);
            var alpha = (byte)Math.Clamp(layer.Opacity * 255.0, 0, 255);
            var mask = layer.Mask is { IsEnabled: true } m ? m : null;
            var normal = layer.BlendMode == Shared.Enums.BlendMode.Normal;

            canvas.Save();
            canvas.ClipRect(bounds);

            if (normal)
            {
                // Fade out the covered part: k = mask luminance * opacity.
                using (var fade = new SKPaint { BlendMode = SKBlendMode.DstOut, Color = SKColors.White.WithAlpha(alpha) })
                {
                    canvas.SaveLayer(bounds, fade);
                    if (mask is null)
                    {
                        using var solid = new SKPaint { Color = SKColors.White };
                        canvas.DrawRect(bounds, solid);
                    }
                    else
                    {
                        using var lumaAsAlpha = new SKPaint { ColorFilter = SKColorFilter.CreateLumaColor() };
                        DrawBitmap(canvas, mask.Bitmap, sampling, lumaAsAlpha);
                    }

                    canvas.Restore();
                }
            }

            using (var combine = new SKPaint
            {
                BlendMode = normal ? SKBlendMode.Plus : Layer.ToSkiaBlendMode(layer.BlendMode),
                Color = SKColors.White.WithAlpha(alpha),
            })
            {
                canvas.SaveLayer(bounds, combine);

                canvas.Save();
                canvas.ResetMatrix();
                using (var adjusted = new SKPaint { ColorFilter = filter })
                    canvas.DrawImage(below, 0, 0, adjusted);
                canvas.Restore();

                if (mask is not null)
                {
                    using var maskPaint = new SKPaint { BlendMode = SKBlendMode.DstIn, ColorFilter = SKColorFilter.CreateLumaColor() };
                    DrawBitmap(canvas, mask.Bitmap, sampling, maskPaint);
                }

                canvas.Restore();
            }

            canvas.Restore();
        }

        /// <summary>Draws at (0,0) with the given filtering. A non-default sampling
        /// needs an SKImage; it wraps the bitmap's pixels without copying them.</summary>
        private static void DrawBitmap(SKCanvas canvas, SKBitmap bitmap, SKSamplingOptions sampling, SKPaint? paint)
        {
            if (sampling.Equals(default(SKSamplingOptions)))
            {
                canvas.DrawBitmap(bitmap, 0, 0, paint);
                return;
            }

            using var pixmap = bitmap.PeekPixels();
            using var image = pixmap is null ? SKImage.FromBitmap(bitmap) : SKImage.FromPixels(pixmap);
            canvas.DrawImage(image, 0, 0, sampling, paint);
        }

        /// <summary>Flattens every visible layer (mask, opacity, and blend mode all
        /// respected) into one brand new opaque-background-free RGBA bitmap sized exactly
        /// (<paramref name="documentWidth"/>, <paramref name="documentHeight"/>) - no
        /// checkerboard, no selection overlay, no pan/zoom: the same pixels a viewer of
        /// an exported file would see. Used by image export
        /// (<c>Export.ImageExportService</c>).</summary>
        public static SKBitmap Flatten(Scene scene, int documentWidth, int documentHeight)
        {
            ArgumentNullException.ThrowIfNull(scene);

            var result = new SKBitmap(documentWidth, documentHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            result.Erase(SKColors.Transparent);
            DrawLayersInto(result, scene);
            return result;
        }

        /// <summary>Renders a single layer alone - masked if applicable, at full opacity
        /// and Normal blend (neither means anything without other layers beneath it) -
        /// into a brand new bitmap the same size as <paramref name="layer"/>. Used to
        /// export one selected layer rather than the whole flattened composite.</summary>
        public static SKBitmap FlattenLayer(Layer layer)
        {
            ArgumentNullException.ThrowIfNull(layer);

            if (layer.Style is { HasAnyEffect: true })
                return RenderStyledLayer(layer, applyMask: true);

            var result = new SKBitmap(layer.Bitmap.Width, layer.Bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(result);
            canvas.Clear(SKColors.Transparent);

            if (layer.Mask is { IsEnabled: true } mask)
            {
                canvas.DrawBitmap(layer.Bitmap, 0, 0);
                using var maskPaint = new SKPaint { BlendMode = SKBlendMode.DstIn, ColorFilter = SKColorFilter.CreateLumaColor() };
                canvas.DrawBitmap(mask.Bitmap, 0, 0, maskPaint);
            }
            else
            {
                canvas.DrawBitmap(layer.Bitmap, 0, 0);
            }

            return result;
        }
    }
}
