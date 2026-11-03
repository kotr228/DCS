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

        /// <summary>Stable identity. Kept when a layer is rebuilt by structural undo/redo,
        /// a canvas resize or a crop, so pixel history entries can find "the same layer"
        /// again (see <see cref="History.LayerPixelsCommand"/>).</summary>
        public Guid Id { get; }

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
        public SKBitmap PaintBitmap => PaintRedirect?.Bitmap ?? (IsMaskActive && Mask is not null ? Mask.Bitmap : Bitmap);

        /// <summary>The canvas counterpart of <see cref="PaintBitmap"/>.</summary>
        public SKCanvas PaintCanvas => PaintRedirect?.Canvas ?? (IsMaskActive && Mask is not null ? Mask.Canvas : Canvas);

        /// <summary>Quick Mask: while set, painting tools draw into this document-wide mask
        /// instead of the layer (see <see cref="PaintBitmap"/>).</summary>
        public LayerMask? PaintRedirect { get; set; }

        /// <summary>True when painting goes to a mask (the layer's own, or Quick Mask)
        /// rather than the layer's pixels.</summary>
        public bool PaintsMask => PaintRedirect is not null || (IsMaskActive && Mask is not null);

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

        /// <summary>Set for a non-destructive adjustment layer: instead of drawing its
        /// own pixels, the layer applies this adjustment to everything beneath it
        /// (limited by its mask, scaled by its opacity). Null for every other layer.</summary>
        public Adjustments.AdjustmentSettings? Adjustment { get; set; }

        public bool IsAdjustment => Adjustment is not null;

        /// <summary>Live effects (drop shadow, glow, stroke, color overlay) drawn around
        /// this layer's pixels by the compositor. Null when the layer has none.</summary>
        public Effects.LayerStyle? Style { get; set; }

        /// <summary>The group this layer belongs to (a <see cref="LayerType.Group"/>
        /// layer's Id), or null at the top level. A group's members always sit directly
        /// below it in <see cref="Scene.Layers"/>, as one unbroken block.</summary>
        public Guid? ParentId { get; set; }

        /// <summary>A layer group (folder): has no pixels of its own; it composites its
        /// members together, with its own visibility, opacity and blend mode.</summary>
        public bool IsGroup => Type == LayerType.Group;

        /// <summary>Groups only: whether the Layers panel shows its members.</summary>
        public bool IsExpanded { get; set; } = true;

        /// <summary>Clipping mask: this layer shows only where the layer below it (its
        /// "base", the nearest unclipped layer under it in the same group) has pixels.</summary>
        public bool IsClipped { get; set; }

        /// <summary>Set for an editable text layer: its pixels are this text, drawn by
        /// <see cref="RenderText"/>. Null for every other layer (including a text layer
        /// that was rasterized).</summary>
        public TextContent? Text { get; set; }

        public bool IsText => Text is not null;

        /// <summary>Set for an editable shape layer: its pixels are this shape, drawn by
        /// <see cref="RenderShape"/>. Null for every other layer.</summary>
        public ShapeContent? Shape { get; set; }

        public bool IsShape => Shape is not null;

        /// <summary>Set for a fill layer (solid color / gradient / pattern): its pixels
        /// are this, drawn by <see cref="RenderFill"/>.</summary>
        public FillContent? Fill { get; set; }

        public bool IsFill => Fill is not null;

        /// <summary>Redraws a fill layer's pixels from <see cref="Fill"/>.</summary>
        public void RenderFill()
        {
            if (Fill is null) return;
            Fill.Render(Canvas, Bitmap.Width, Bitmap.Height);
        }

        /// <summary>Redraws a shape layer's pixels from <see cref="Shape"/>.</summary>
        public void RenderShape()
        {
            if (Shape is null) return;
            Canvas.Clear(SKColors.Transparent);
            Shape.Render(Canvas);
        }

        /// <summary>Smart Filters: the layer's untouched pixels. Set once the layer is
        /// converted for Smart Filters; <see cref="Bitmap"/> then always shows this run
        /// through <see cref="SmartFilters"/> (see <see cref="RenderSmartFilters"/>).</summary>
        public SKBitmap? SmartFilterSource { get; private set; }

        public IReadOnlyList<Effects.SmartFilter> SmartFilters { get; private set; } = Array.Empty<Effects.SmartFilter>();

        public bool HasSmartFilters => SmartFilterSource is not null;

        /// <summary>Makes this a Smart Filter layer (or replaces its original and list),
        /// then redraws it. Takes ownership of <paramref name="source"/>.</summary>
        public void SetSmartFilters(SKBitmap source, IReadOnlyList<Effects.SmartFilter> filters)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (!ReferenceEquals(source, SmartFilterSource)) SmartFilterSource?.Dispose();
            SmartFilterSource = source;
            SmartFilters = filters ?? Array.Empty<Effects.SmartFilter>();
            RenderSmartFilters();
        }

        /// <summary>Changes the filter list (keeping the original) and redraws.</summary>
        public void ReplaceSmartFilters(IReadOnlyList<Effects.SmartFilter> filters)
        {
            if (SmartFilterSource is null) return;
            SmartFilters = filters ?? Array.Empty<Effects.SmartFilter>();
            RenderSmartFilters();
        }

        /// <summary>Back to an ordinary layer, keeping what it shows now ("Rasterize").</summary>
        public void ClearSmartFilters()
        {
            SmartFilterSource?.Dispose();
            SmartFilterSource = null;
            SmartFilters = Array.Empty<Effects.SmartFilter>();
        }

        /// <summary>Redraws <see cref="Bitmap"/> from the original through the enabled filters.</summary>
        public void RenderSmartFilters()
        {
            if (SmartFilterSource is null) return;
            using var rendered = Effects.SmartFilterEngine.Render(SmartFilterSource, SmartFilters);
            Bitmap.Pixels = rendered.Pixels;
        }

        /// <summary>Redraws a text layer's pixels from <see cref="Text"/>.</summary>
        public void RenderText()
        {
            if (Text is null) return;
            Canvas.Clear(SKColors.Transparent);
            Text.Render(Canvas);
        }

        public Layer(string name, int width, int height, LayerType type = LayerType.Raster, Guid? id = null)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Layer name cannot be empty.", nameof(name));
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

            Id = id ?? Guid.NewGuid();
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
            BlendMode.Hue => SKBlendMode.Hue,
            BlendMode.Saturation => SKBlendMode.Saturation,
            BlendMode.Color => SKBlendMode.Color,
            BlendMode.Luminosity => SKBlendMode.Luminosity,
            _ => SKBlendMode.SrcOver,
        };

        /// <summary>Re-attaches Smart Object content to a layer rebuilt by structural
        /// undo/redo, so undoing e.g. "delete layer" brings back a real Smart Object
        /// rather than its flattened pixels.</summary>
        internal void AttachSmartObject(SmartObjectContent content, SKMatrix matrix)
        {
            SmartObject = content;
            SmartObjectTransform = new SmartObjectTransform { Matrix = matrix };
        }

        public void Dispose()
        {
            if (_disposed) return;

            // SmartObject content is deliberately not disposed here: undo history
            // snapshots may still share it (see AttachSmartObject). Its bitmaps are
            // released by their finalizers once nothing references them.
            Mask?.Dispose();
            SmartFilterSource?.Dispose();
            Canvas.Dispose();
            Bitmap.Dispose();
            _disposed = true;
        }
    }
}
