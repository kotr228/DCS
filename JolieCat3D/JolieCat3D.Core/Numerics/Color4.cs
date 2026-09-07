using System.Numerics;

namespace JolieCat3D.Core.Numerics
{
    /// <summary>
    /// A linear RGBA color, stored as a <see cref="System.Numerics.Vector4"/> (R, G, B, A
    /// in X, Y, Z, W) rather than four separate floats or packed bytes - the whole point
    /// of leaning on <see cref="System.Numerics"/> throughout this namespace (named
    /// "Numerics", not "Math", specifically to avoid colliding with <see cref="System.Math"/>
    /// for every file that ends up wanting both) is that these types are
    /// hardware-accelerated (SIMD, JIT intrinsics) in the runtime itself; hand-rolling an
    /// equivalent struct would only be slower, not "lightweight". Components are 0-1
    /// float, not 0-255 byte, so blending/multiplying colors (as material tinting and
    /// lighting both need) never needs an intermediate int-to-float conversion.
    /// </summary>
    public readonly struct Color4 : IEquatable<Color4>
    {
        public readonly Vector4 Value;

        public Color4(float r, float g, float b, float a = 1f) : this(new Vector4(r, g, b, a))
        {
        }

        public Color4(Vector4 value) => Value = value;

        public float R => Value.X;
        public float G => Value.Y;
        public float B => Value.Z;
        public float A => Value.W;

        public static readonly Color4 White = new(1f, 1f, 1f);
        public static readonly Color4 Black = new(0f, 0f, 0f);
        public static readonly Color4 Red = new(1f, 0f, 0f);
        public static readonly Color4 Green = new(0f, 1f, 0f);
        public static readonly Color4 Blue = new(0f, 0f, 1f);
        public static readonly Color4 Transparent = new(0f, 0f, 0f, 0f);

        /// <summary>The common 0-255 per-channel byte form, for interop with anything
        /// (a file format, a UI color picker) that speaks that convention instead.</summary>
        public static Color4 FromBytes(byte r, byte g, byte b, byte a = 255) =>
            new(r / 255f, g / 255f, b / 255f, a / 255f);

        public static Color4 Lerp(Color4 a, Color4 b, float t) => new(Vector4.Lerp(a.Value, b.Value, t));

        public static Color4 operator *(Color4 color, float scale) => new(color.Value * scale);

        /// <summary>Per-channel multiply - the standard way to tint one color by another
        /// (e.g. a vertex color tinting a material's own diffuse color).</summary>
        public static Color4 operator *(Color4 a, Color4 b) => new(a.Value * b.Value);

        public bool Equals(Color4 other) => Value.Equals(other.Value);
        public override bool Equals(object? obj) => obj is Color4 other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => $"({R:0.###}, {G:0.###}, {B:0.###}, {A:0.###})";
    }
}
