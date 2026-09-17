namespace JolieCat3D.Core.Scene
{
    /// <summary>
    /// Makes a <see cref="Node"/>'s own <see cref="Node.Mesh"/> deform under a
    /// skeleton - present (non-null, on <see cref="Node.SkinBinding"/>) only for a mesh
    /// node that has actually been bound to one, the same "optional data, not a
    /// subclass" shape <see cref="Node.Camera"/>/<see cref="Node.Bone"/>/etc. already
    /// use. <see cref="Bones"/> is this binding's own LOCAL joints list - a
    /// <see cref="Geometry.Vertex.BoneIndices"/> value of, say, 2 means "the THIRD
    /// entry of THIS binding's own <see cref="Bones"/>", never a raw index into the
    /// whole scene - the same "a skin's own joints array, not the whole scene" shape
    /// glTF's own skinning format already uses, letting the SAME small vertex index
    /// range (0-3 slots, each indexing a typically-small per-mesh bone list) work
    /// regardless of how many other nodes/bones happen to exist elsewhere in the scene.
    /// </summary>
    public sealed class SkinBinding
    {
        /// <summary>The armature this binding was created from - kept purely as a
        /// convenience back-reference (e.g. so the Properties Inspector can re-offer
        /// "every bone under this same armature" if <see cref="Bones"/> is edited
        /// later), not read by <see cref="Skinning.SkinningEvaluator"/> itself (which
        /// only ever needs <see cref="Bones"/>).</summary>
        public Node? Armature { get; set; }

        /// <summary>This binding's own ordered joints list - see this class's own
        /// remarks on why a vertex's own <see cref="Geometry.Vertex.BoneIndices"/>
        /// indexes INTO this list, not the whole scene.</summary>
        public List<Node> Bones { get; } = new();

        /// <summary>A complete, independent copy - <see cref="Armature"/> and every
        /// entry of <see cref="Bones"/> are copied BY REFERENCE, not re-pointed at any
        /// duplicated node, the exact same precedent
        /// <see cref="Modifiers.BooleanModifier.Clone"/> already set for its own
        /// Target reference (duplicating a skinned mesh without also duplicating -
        /// and correctly re-mapping - its entire source armature is not something this
        /// method can meaningfully do on its own). Used by <see cref="Node.Clone"/>.</summary>
        public SkinBinding Clone()
        {
            var clone = new SkinBinding { Armature = Armature };
            clone.Bones.AddRange(Bones);
            return clone;
        }
    }
}
