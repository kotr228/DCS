using System;

namespace JolieCat.Core.Geometry
{
    /// <summary>
    /// A non-negative width/height pair, e.g. the pixel dimensions of a canvas or layer.
    /// </summary>
    public readonly struct Size2D : IEquatable<Size2D>
    {
        public static readonly Size2D Empty = new(0, 0);

        public double Width { get; }

        public double Height { get; }

        public Size2D(double width, double height)
        {
            if (width < 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height < 0) throw new ArgumentOutOfRangeException(nameof(height));

            Width = width;
            Height = height;
        }

        public bool Equals(Size2D other) => Width.Equals(other.Width) && Height.Equals(other.Height);

        public override bool Equals(object? obj) => obj is Size2D other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Width, Height);

        public override string ToString() => $"{Width}x{Height}";
    }
}
