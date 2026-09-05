using System;

namespace JolieCat.Core.Geometry
{
    /// <summary>
    /// An axis-aligned rectangle in document space, defined by its top-left corner and size.
    /// </summary>
    public readonly struct Rect2D : IEquatable<Rect2D>
    {
        public static readonly Rect2D Empty = new(Point2D.Zero, Size2D.Empty);

        public Point2D Location { get; }

        public Size2D Size { get; }

        public double X => Location.X;

        public double Y => Location.Y;

        public double Width => Size.Width;

        public double Height => Size.Height;

        public double Left => X;

        public double Top => Y;

        public double Right => X + Width;

        public double Bottom => Y + Height;

        public Rect2D(Point2D location, Size2D size)
        {
            Location = location;
            Size = size;
        }

        public Rect2D(double x, double y, double width, double height)
            : this(new Point2D(x, y), new Size2D(width, height))
        {
        }

        public bool Contains(Point2D point) =>
            point.X >= Left && point.X <= Right && point.Y >= Top && point.Y <= Bottom;

        public bool IntersectsWith(Rect2D other) =>
            Left < other.Right && Right > other.Left && Top < other.Bottom && Bottom > other.Top;

        public bool Equals(Rect2D other) => Location.Equals(other.Location) && Size.Equals(other.Size);

        public override bool Equals(object? obj) => obj is Rect2D other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Location, Size);

        public override string ToString() => $"[{X}, {Y}, {Width}x{Height}]";
    }
}
