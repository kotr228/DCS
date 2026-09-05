using System;

namespace JolieCat.Core.Geometry
{
    /// <summary>
    /// A 2D displacement/direction, distinct from <see cref="Point2D"/> (a position).
    /// </summary>
    public readonly struct Vector2D : IEquatable<Vector2D>
    {
        public static readonly Vector2D Zero = new(0, 0);

        public double X { get; }

        public double Y { get; }

        public Vector2D(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double Length => Math.Sqrt(X * X + Y * Y);

        public static Vector2D operator +(Vector2D a, Vector2D b) => new(a.X + b.X, a.Y + b.Y);

        public static Vector2D operator -(Vector2D a, Vector2D b) => new(a.X - b.X, a.Y - b.Y);

        public static Vector2D operator *(Vector2D v, double scalar) => new(v.X * scalar, v.Y * scalar);

        public bool Equals(Vector2D other) => X.Equals(other.X) && Y.Equals(other.Y);

        public override bool Equals(object? obj) => obj is Vector2D other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Y);

        public override string ToString() => $"<{X}, {Y}>";
    }
}
