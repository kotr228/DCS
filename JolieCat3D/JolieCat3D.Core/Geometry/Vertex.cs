using System.Numerics;
using JolieCat3D.Core.Numerics;

namespace JolieCat3D.Core.Geometry
{
    /// <summary>
    /// One point in a <see cref="Mesh"/>'s vertex buffer: position, surface normal, a
    /// single UV texture-coordinate set, and a per-vertex color. A <c>readonly struct</c>
    /// (value type), not a class - meshes can hold thousands of these, and a value type
    /// avoids one heap allocation and one pointer-chase per vertex that a class would
    /// cost, both in <c>JolieCat3D.Core</c> itself and when <c>JolieCat3D.Engine</c>
    /// walks a mesh's <see cref="Mesh.Vertices"/> to build a WPF <c>MeshGeometry3D</c>.
    /// "With"-style constructors instead of settable properties, matching struct-value
    /// semantics: mutating a copy, not a shared instance, is what you get either way with
    /// a struct, so the API makes that explicit rather than silently doing nothing to the
    /// original the way an accidental struct-property "mutation" would.
    /// </summary>
    public readonly struct Vertex : IEquatable<Vertex>
    {
        public readonly Vector3 Position;
        public readonly Vector3 Normal;
        public readonly Vector2 UV;
        public readonly Color4 Color;

        public Vertex(Vector3 position, Vector3 normal = default, Vector2 uv = default, Color4 color = default)
        {
            Position = position;
            Normal = normal;
            UV = uv;
            // default(Color4) is (0,0,0,0) - transparent black, not a sane vertex color -
            // so an omitted color argument gets White instead, matching "no color data
            // supplied" reading as "don't tint this vertex" rather than "hide it".
            Color = color.Equals(default(Color4)) ? Color4.White : color;
        }

        public Vertex WithPosition(Vector3 position) => new(position, Normal, UV, Color);
        public Vertex WithNormal(Vector3 normal) => new(Position, normal, UV, Color);
        public Vertex WithUV(Vector2 uv) => new(Position, Normal, uv, Color);
        public Vertex WithColor(Color4 color) => new(Position, Normal, UV, color);

        public bool Equals(Vertex other) =>
            Position.Equals(other.Position) && Normal.Equals(other.Normal) &&
            UV.Equals(other.UV) && Color.Equals(other.Color);

        public override bool Equals(object? obj) => obj is Vertex other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Position, Normal, UV, Color);
    }
}
