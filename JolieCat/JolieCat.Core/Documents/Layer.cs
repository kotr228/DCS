using System;
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

            Canvas.Dispose();
            Bitmap.Dispose();
            _disposed = true;
        }
    }
}
