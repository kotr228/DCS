using System.Numerics;

namespace JolieCat3D.Core.Geometry
{
    /// <summary>
    /// A 3D ray: an <see cref="Origin"/> and a unit-length <see cref="Direction"/> - the
    /// primitive every method in <see cref="RayIntersection"/> operates on, and (in
    /// <c>JolieCat3D.Engine</c>) what a viewport click becomes via HelixToolkit.Wpf's own
    /// <c>Viewport3DHelper.Point2DtoRay3D</c> before this project's own, WPF-free
    /// intersection math ever runs.
    ///
    /// The constructor always normalizes <paramref name="direction"/> (falling back to
    /// <see cref="Vector3.Zero"/> for a degenerate, zero-length input rather than
    /// producing NaN) - every distance <see cref="RayIntersection"/> returns is a real,
    /// physically meaningful world-space length only because <see cref="Direction"/> is
    /// guaranteed unit length; a caller can never accidentally pass in an unnormalized
    /// direction and get silently-wrong distances back.
    /// </summary>
    public readonly struct Ray
    {
        public readonly Vector3 Origin;
        public readonly Vector3 Direction;

        public Ray(Vector3 origin, Vector3 direction)
        {
            Origin = origin;
            Direction = direction.LengthSquared() > float.Epsilon ? Vector3.Normalize(direction) : Vector3.Zero;
        }

        /// <summary>The point <paramref name="distance"/> world-space units along this
        /// ray from <see cref="Origin"/> - meaningful only because <see cref="Direction"/>
        /// is always unit length (see this struct's own remarks).</summary>
        public Vector3 GetPoint(float distance) => Origin + Direction * distance;
    }
}
