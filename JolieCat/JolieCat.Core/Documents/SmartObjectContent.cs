using System;
using JolieCat.Core.Rendering;
using SkiaSharp;

namespace JolieCat.Core.Documents
{
    /// <summary>
    /// The pristine, never-modified source behind a Smart Object layer (see
    /// <see cref="Layer.SmartObject"/>), plus its current placement
    /// (<see cref="Layer.SmartObjectTransform"/>, held on the layer rather than here -
    /// see that property's own remarks for why). <see cref="Layer.Bitmap"/> is always
    /// just a cached render of <see cref="SourceBitmap"/> through that transform,
    /// rebuilt fresh by <see cref="Layer.RenderSmartObject"/> on every transform
    /// change - so scaling or rotating a Smart Object repeatedly always resamples from
    /// this one untouched original, never compounding quality loss the way baking
    /// pixels destructively (as an ordinary Free Transform commit does) would.
    /// </summary>
    public sealed class SmartObjectContent : IDisposable
    {
        private bool _disposed;

        /// <summary>The untouched placed image, or the embedded sub-project's own
        /// flattened composite - resampled from fresh into <see cref="Layer.Bitmap"/>
        /// on every render, never itself overwritten in place except by
        /// <see cref="RefreshFromEmbeddedScene"/>, and even then by replacement, not
        /// in-place mutation.</summary>
        public SKBitmap SourceBitmap { get; private set; }

        /// <summary>The embedded sub-project, when this Smart Object wraps a whole
        /// nested scene (a "Place Embedded" placement, one document composited inside
        /// another) rather than a single placed image - null for a plain placed-image
        /// Smart Object, which has no scene of its own to edit. "Edit Contents" opens
        /// this scene directly in its own document tab; saving that tab calls
        /// <see cref="RefreshFromEmbeddedScene"/> to re-flatten it back into
        /// <see cref="SourceBitmap"/>, so the parent instance's next render picks up
        /// the edit.</summary>
        public Scene? EmbeddedScene { get; }

        public SmartObjectContent(SKBitmap sourceBitmap, Scene? embeddedScene = null)
        {
            ArgumentNullException.ThrowIfNull(sourceBitmap);

            SourceBitmap = sourceBitmap;
            EmbeddedScene = embeddedScene;
        }

        /// <summary>Re-flattens <see cref="EmbeddedScene"/> (sized
        /// <paramref name="documentWidth"/> by <paramref name="documentHeight"/> - its
        /// own document's canvas size, which the caller already knows from having
        /// opened it as a document tab) into a fresh <see cref="SourceBitmap"/>,
        /// disposing the previous one. No-op for a plain placed-image Smart Object
        /// (<see cref="EmbeddedScene"/> null) - there is no nested composition to
        /// re-flatten, only the original placed pixels, which never change.</summary>
        public void RefreshFromEmbeddedScene(int documentWidth, int documentHeight)
        {
            if (EmbeddedScene is null) return;

            var flattened = SceneCompositor.Flatten(EmbeddedScene, documentWidth, documentHeight);
            SourceBitmap.Dispose();
            SourceBitmap = flattened;
        }

        public void Dispose()
        {
            if (_disposed) return;

            SourceBitmap.Dispose();
            EmbeddedScene?.Dispose();
            _disposed = true;
        }
    }

    /// <summary>
    /// The affine placement of a Smart Object layer's <see cref="SmartObjectContent.SourceBitmap"/>
    /// within the layer's own document-space bounds, as a single accumulated
    /// <see cref="SKMatrix"/> - so a later Free Transform commit can fold its own
    /// gesture matrix straight into this one (<c>Matrix = Matrix.PostConcat(gesture)</c>,
    /// see <c>JolieCat.UI.ViewModels.CanvasViewModel.CommitTransform</c>) without ever
    /// needing to decompose an arbitrary combined scale+rotation+translation back into
    /// separate scalar fields - a plain origin/scale/rotation/translate tuple (the shape
    /// <see cref="Transform.LayerTransformer.BuildMatrix"/> takes) can represent any one
    /// gesture, but not the general affine map two gestures with *different* pivots
    /// compose into. Each change re-renders <see cref="Layer.Bitmap"/> by resampling
    /// fresh from <see cref="SmartObjectContent.SourceBitmap"/> through this matrix
    /// (<see cref="Layer.RenderSmartObject"/>) instead of baking a transformed bitmap
    /// over whatever pixels were already there.
    /// </summary>
    public sealed class SmartObjectTransform
    {
        /// <summary>Maps a point in the source bitmap's own local pixel space to its
        /// position within the layer's document-space bounds. Identity means the
        /// source is drawn at its own native size, anchored at the layer's top-left
        /// corner - <see cref="Layer.CreateSmartObject"/> starts every new Smart Object
        /// centered instead, by seeding this with a translation.</summary>
        public SKMatrix Matrix { get; set; } = SKMatrix.Identity;
    }
}
