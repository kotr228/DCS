using System;

namespace JolieCat.Core.Geometry
{
    /// <summary>
    /// A 2D point in document space. Kept independent of any UI framework so that
    /// <c>JolieCat.Core</c> stays free of a WPF/Skia dependency.
    /// </summary>
    public readonly struct Point2D : IEquatable<Point2D>
    {
        public static readonly Point2D Zero = new(0, 0);

        public double X { get; }

        public double Y { get; }

        public Point2D(double x, double y)
        {
            X = x;
            Y = y;
        }

        public Point2D Offset(double dx, double dy) => new(X + dx, Y + dy);

        public double DistanceTo(Point2D other)
        {
            var dx = other.X - X;
            var dy = other.Y - Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public static Point2D operator +(Point2D point, Vector2D vector) => new(point.X + vector.X, point.Y + vector.Y);

        public static Point2D operator -(Point2D point, Vector2D vector) => new(point.X - vector.X, point.Y - vector.Y);

        public static Vector2D operator -(Point2D a, Point2D b) => new(a.X - b.X, a.Y - b.Y);

        public bool Equals(Point2D other) => X.Equals(other.X) && Y.Equals(other.Y);

        public override bool Equals(object? obj) => obj is Point2D other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Y);

        public override string ToString() => $"({X}, {Y})";
    }
}
