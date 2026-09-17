using System.Numerics;
using JolieCat3D.Core.Numerics;

namespace JolieCat3D.Core.Geometry
{
    /// <summary>
    /// One point in a <see cref="Mesh"/>'s vertex buffer: position, surface normal, a
    /// single UV texture-coordinate set, a per-vertex color, and up to 4 skeletal-
    /// skinning bone influences (<see cref="BoneIndices"/>/<see cref="BoneWeights"/> -
    /// see <see cref="Skinning.SkinningEvaluator"/>'s own remarks). A <c>readonly
    /// struct</c> (value type), not a class - meshes can hold thousands of these, and a
    /// value type avoids one heap allocation and one pointer-chase per vertex that a
    /// class would cost, both in <c>JolieCat3D.Core</c> itself and when
    /// <c>JolieCat3D.Engine</c> walks a mesh's <see cref="Mesh.Vertices"/> to build a
    /// WPF <c>MeshGeometry3D</c>. "With"-style constructors instead of settable
    /// properties, matching struct-value semantics: mutating a copy, not a shared
    /// instance, is what you get either way with a struct, so the API makes that
    /// explicit rather than silently doing nothing to the original the way an
    /// accidental struct-property "mutation" would.
    /// </summary>
    public readonly struct Vertex : IEquatable<Vertex>
    {
        public readonly Vector3 Position;
        public readonly Vector3 Normal;
        public readonly Vector2 UV;
        public readonly Color4 Color;

        /// <summary>Which up-to-4 entries of a <see cref="Scene.SkinBinding.Bones"/>
        /// list influence this vertex - a LOCAL index into that mesh's own binding, the
        /// same "a skin's own joints array, not the whole scene" convention glTF's own
        /// skinning format already uses (see <see cref="Skinning.SkinningEvaluator"/>'s
        /// own remarks). Meaningless on its own with an all-zero <see cref="BoneWeights"/>
        /// (the shared default for every vertex authored before skinning existed) -
        /// index 0 with weight 0 contributes nothing, so an unbound/unpainted vertex
        /// never needs a separate "no bone" sentinel value here.</summary>
        public readonly BoneIndices BoneIndices;

        /// <summary>How much each of <see cref="BoneIndices"/>' own 4 slots (X/Y/Z/W
        /// pairing positionally with <see cref="BoneIndices"/>' own X/Y/Z/W) influences
        /// this vertex - need not already sum to 1 (<see cref="Skinning.SkinningEvaluator"/>
        /// normalizes defensively at deform time); all-zero (the default) means "no
        /// skinning influence at all", left in its rest/authored position untouched by
        /// deformation.</summary>
        public readonly Vector4 BoneWeights;

        public Vertex(Vector3 position, Vector3 normal = default, Vector2 uv = default, Color4 color = default,
            BoneIndices boneIndices = default, Vector4 boneWeights = default)
        {
            Position = position;
            Normal = normal;
            UV = uv;
            // default(Color4) is (0,0,0,0) - transparent black, not a sane vertex color -
            // so an omitted color argument gets White instead, matching "no color data
            // supplied" reading as "don't tint this vertex" rather than "hide it".
            Color = color.Equals(default(Color4)) ? Color4.White : color;
            BoneIndices = boneIndices;
            BoneWeights = boneWeights;
        }

        public Vertex WithPosition(Vector3 position) => new(position, Normal, UV, Color, BoneIndices, BoneWeights);
        public Vertex WithNormal(Vector3 normal) => new(Position, normal, UV, Color, BoneIndices, BoneWeights);
        public Vertex WithUV(Vector2 uv) => new(Position, Normal, uv, Color, BoneIndices, BoneWeights);
        public Vertex WithColor(Color4 color) => new(Position, Normal, UV, color, BoneIndices, BoneWeights);
        public Vertex WithBoneIndices(BoneIndices boneIndices) => new(Position, Normal, UV, Color, boneIndices, BoneWeights);
        public Vertex WithBoneWeights(Vector4 boneWeights) => new(Position, Normal, UV, Color, BoneIndices, boneWeights);

        public bool Equals(Vertex other) =>
            Position.Equals(other.Position) && Normal.Equals(other.Normal) &&
            UV.Equals(other.UV) && Color.Equals(other.Color) &&
            BoneIndices.Equals(other.BoneIndices) && BoneWeights.Equals(other.BoneWeights);

        public override bool Equals(object? obj) => obj is Vertex other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Position, Normal, UV, Color, BoneIndices, BoneWeights);
    }
}
