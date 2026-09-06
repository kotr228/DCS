using System;
using JolieCat.Core.Transform;
using JolieCat.Shared.Documents;
using JolieCat.Shared.Enums;
using SkiaSharp;

namespace JolieCat.Core.Documents
{
    /// <summary>
    /// Default <see cref="ILayer"/> implementation backing the in-memory scene graph.
    /// Unlike the pure-metadata <see cref="ILayer"/> contract, a concrete Layer also owns
    /// the pixel buffer itself: an <see cref="SKBitmap"/> (and a persistent <see cref="SKCanvas"/>
    /// wrapping it, ready to draw on) sized to the document. Painting tools draw directly
    /// onto <see cref="Canvas"/>; <see cref="Core.Documents.Scene"/>'s compositor and
    /// <c>JolieCat.UI</c>'s renderer read <see cref="Bitmap"/> back out.
    /// </summary>
    public sealed class Layer : ILayer, IDisposable
    {
        private double _opacity = 1.0;
        private bool _disposed;

        public Guid Id { get; } = Guid.NewGuid();

        public string Name { get; set; }

        public LayerType Type { get; }

        public bool IsVisible { get; set; } = true;

        public bool IsLocked { get; set; }

        public double Opacity
        {
            get => _opacity;
            set => _opacity = Math.Clamp(value, 0.0, 1.0);
        }

        public BlendMode BlendMode { get; set; } = BlendMode.Normal;

        /// <summary>The layer's pixel buffer, sized to the document and initially fully transparent.</summary>
        public SKBitmap Bitmap { get; }

        /// <summary>A persistent canvas wrapping <see cref="Bitmap"/> - kept alive for the
        /// layer's lifetime rather than recreated per stroke, since painting tools call
        /// into it on every mouse-move sample.</summary>
        public SKCanvas Canvas { get; }

        /// <summary>This layer's visibility mask, if one has been added via
        /// <see cref="AddMask"/> - null until then. See <see cref="LayerMask"/>'s own
        /// remarks for what it does and how it's stored.</summary>
        public LayerMask? Mask { get; private set; }

        /// <summary>True while <see cref="Mask"/> - not this layer's own color content -
        /// is what painting tools should draw onto. Toggled from the Layers panel when
        /// the mask thumbnail is selected; every painting tool already draws through
        /// <see cref="PaintBitmap"/>/<see cref="PaintCanvas"/> rather than
        /// <see cref="Bitmap"/>/<see cref="Canvas"/> directly, so this one flag redirects
        /// all of them with no changes to any tool's own logic. Has no effect while
        /// <see cref="Mask"/> is null - <see cref="PaintBitmap"/>/<see cref="PaintCanvas"/>
        /// fall back to the layer's own content in that case.</summary>
        public bool IsMaskActive { get; set; }

        /// <summary>Whichever buffer painting tools should currently draw into: the
        /// mask's, if <see cref="IsMaskActive"/> and <see cref="Mask"/> exists, else
        /// this layer's own <see cref="Bitmap"/>.</summary>
        public SKBitmap PaintBitmap => IsMaskActive && Mask is not null ? Mask.Bitmap : Bitmap;

        /// <summary>The canvas counterpart of <see cref="PaintBitmap"/>.</summary>
        public SKCanvas PaintCanvas => IsMaskActive && Mask is not null ? Mask.Canvas : Canvas;

        /// <summary>This layer's pristine source (and, if it wraps a nested
        /// composition, that embedded scene) when <see cref="Type"/> is
        /// <see cref="LayerType.SmartObject"/> - null for every other layer type.
        /// <see cref="Bitmap"/> is always just this rendered through
        /// <see cref="SmartObjectTransform"/>, kept in sync by
        /// <see cref="RenderSmartObject"/>; nothing else ever paints on
        /// <see cref="Bitmap"/> directly for a Smart Object layer.</summary>
        public SmartObjectContent? SmartObject { get; private set; }

        /// <summary>The affine placement currently applied to <see cref="SmartObject"/>'s
        /// source within this layer - null exactly when <see cref="SmartObject"/> is.
        /// Changed by the same Free Transform handles as an ordinary layer (see
        /// <c>JolieCat.UI.ViewModels.CanvasViewModel.CommitTransform</c>), but every
        /// change here is followed by <see cref="RenderSmartObject"/> instead of a
        /// destructive pixel bake.</summary>
        public SmartObjectTransform? SmartObjectTransform { get; private set; }

        public Layer(string name, int width, int height, LayerType type = LayerType.Raster)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Layer name cannot be empty.", nameof(name));
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

            Name = name;
            Type = type;

            Bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            Canvas = new SKCanvas(Bitmap);
            Canvas.Clear(SKColors.Transparent);
        }

        /// <summary>Attaches a fully-opaque-white mask sized to this layer, if one isn't
        /// already present, and returns it (the existing one, if it already had one -
        /// idempotent rather than replacing it, so calling this speculatively is always
        /// safe). Starting fully white/opaque means adding a mask never itself changes
        /// how the layer looks - it only starts affecting compositing once something is
        /// actually painted onto it.</summary>
        public LayerMask AddMask()
        {
            Mask ??= new LayerMask(Bitmap.Width, Bitmap.Height);
            return Mask;
        }

        /// <summary>Removes and disposes this layer's mask, if it has one - a no-op
        /// otherwise. Also turns off <see cref="IsMaskActive"/>, since there's nothing
        /// left for it to redirect painting to.</summary>
        public void RemoveMask()
        {
            if (Mask is null) return;

            IsMaskActive = false;
            Mask.Dispose();
            Mask = null;
        }

        /// <summary>Creates a new Smart Object layer sized to the document, placing
        /// <paramref name="sourceBitmap"/> (never modified again after this - see
        /// <see cref="SmartObjectContent.SourceBitmap"/>) centered at the document's
        /// own center with no scale or rotation, then rendering it immediately so
        /// <see cref="Bitmap"/> is ready to composite like any other layer. Pass
        /// <paramref name="embeddedScene"/> only for a "Place Embedded" Smart Object
        /// wrapping a whole nested composition; leave it null for a plain placed
        /// image.</summary>
        public static Layer CreateSmartObject(string name, int width, int height, SKBitmap sourceBitmap, Scene? embeddedScene = null)
        {
            ArgumentNullException.ThrowIfNull(sourceBitmap);

            var layer = new Layer(name, width, height, LayerType.SmartObject)
            {
                SmartObject = new SmartObjectContent(sourceBitmap, embeddedScene),
                // Centers the source within the layer at 1:1 scale to start - the same
                // "just placed" position every other paint tool's initial content lands
                // at - as a plain translation (no separate origin/pivot needed for an
                // identity scale/rotation).
                SmartObjectTransform = new SmartObjectTransform
                {
                    Matrix = SKMatrix.CreateTranslation(width / 2f - sourceBitmap.Width / 2f, height / 2f - sourceBitmap.Height / 2f),
                },
            };
            layer.RenderSmartObject();
            return layer;
        }

        /// <summary>Re-renders <see cref="Bitmap"/> by resampling
        /// <see cref="SmartObject"/>'s <see cref="SmartObjectContent.SourceBitmap"/>
        /// fresh through <see cref="SmartObjectTransform"/>'s current
        /// <see cref="Documents.SmartObjectTransform.Matrix"/> - the non-destructive
        /// counterpart of baking a Free Transform (see <see cref="LayerTransformer.Bake"/>,
        /// which this calls directly). No-op if this isn't a Smart Object layer.
        /// Call after any change to <see cref="SmartObjectTransform"/>.</summary>
        public void RenderSmartObject()
        {
            if (SmartObject is null || SmartObjectTransform is null) return;

            using var rendered = LayerTransformer.Bake(SmartObject.SourceBitmap, SmartObjectTransform.Matrix, Bitmap.Width, Bitmap.Height);
            Canvas.Clear(SKColors.Transparent);
            Canvas.DrawBitmap(rendered, 0, 0);
        }

        /// <summary>Re-flattens this layer's embedded sub-project (sized
        /// <paramref name="documentWidth"/> by <paramref name="documentHeight"/> - that
        /// sub-project's own document canvas size) into a fresh source bitmap, then
        /// re-renders <see cref="Bitmap"/> from it through the existing
        /// <see cref="SmartObjectTransform"/> unchanged - called when an "Edit
        /// Contents" tab for this layer is closed. No-op if this isn't a Smart Object
        /// layer, or has no embedded scene (a plain placed-image Smart Object has
        /// nothing to re-flatten).</summary>
        public void RefreshSmartObjectContent(int documentWidth, int documentHeight)
        {
            if (SmartObject is null) return;

            SmartObject.RefreshFromEmbeddedScene(documentWidth, documentHeight);
            RenderSmartObject();
        }

        /// <summary>Maps the Shared, framework-agnostic <see cref="BlendMode"/> to the
        /// SkiaSharp blend mode used to composite this layer - every name matches Skia's
        /// own enum, so this is a direct lookup rather than an approximation.</summary>
        public static SKBlendMode ToSkiaBlendMode(BlendMode mode) => mode switch
        {
            BlendMode.Normal => SKBlendMode.SrcOver,
            BlendMode.Multiply => SKBlendMode.Multiply,
            BlendMode.Screen => SKBlendMode.Screen,
            BlendMode.Overlay => SKBlendMode.Overlay,
            BlendMode.Darken => SKBlendMode.Darken,
            BlendMode.Lighten => SKBlendMode.Lighten,
            BlendMode.ColorDodge => SKBlendMode.ColorDodge,
            BlendMode.ColorBurn => SKBlendMode.ColorBurn,
            BlendMode.HardLight => SKBlendMode.HardLight,
            BlendMode.SoftLight => SKBlendMode.SoftLight,
            BlendMode.Difference => SKBlendMode.Difference,
            BlendMode.Exclusion => SKBlendMode.Exclusion,
            _ => SKBlendMode.SrcOver,
        };

        public void Dispose()
        {
            if (_disposed) return;

            Mask?.Dispose();
            SmartObject?.Dispose();
            Canvas.Dispose();
            Bitmap.Dispose();
            _disposed = true;
        }
    }
}
