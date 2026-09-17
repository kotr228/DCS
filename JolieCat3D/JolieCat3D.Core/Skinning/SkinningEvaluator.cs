using System.Numerics;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.Core.Skinning
{
    /// <summary>
    /// Deforms a mesh's own vertex positions according to its <see cref="SkinBinding"/>'s
    /// current bone poses - standard Linear Blend Skinning ("Smooth Skinning"), the same
    /// technique every mainstream 3D engine/tool (Blender, Unity, Unreal, glTF) uses.
    /// Called from <c>JolieCat3D.Engine.Geometry.SceneGraphBuilder.Build</c> immediately
    /// AFTER <see cref="Modifiers.ModifierStack.Evaluate"/> (so a modifier - Mirror,
    /// Solidify, ... - still sees/produces plain, undeformed geometry; skinning is the
    /// LAST step before the mesh becomes GPU geometry), the same "non-destructive,
    /// evaluated fresh every render, never mutates its own input" shape every other
    /// mesh-producing step in this pipeline already has.
    ///
    /// The math, per vertex, per one of its (up to 4) bone influences i:
    /// <code>
    /// skinningMatrix_i = InverseBindMatrix_i * CurrentBoneWorldMatrix_i
    /// deformedWorldPosition = Σ normalizedWeight_i * (restWorldPosition * skinningMatrix_i)
    /// </code>
    /// (row-vector convention throughout, matching this project's own established
    /// <see cref="Matrix4x4"/> composition order - see <see cref="Node.GetWorldTransform"/>'s
    /// own remarks: <c>A * B</c> applies <c>A</c> first). <c>InverseBindMatrix_i</c>
    /// (<see cref="BoneData.GetInverseBindMatrix"/>) converts a world-space position at
    /// BIND time back into that bone's own local space at bind time; multiplying by the
    /// bone's CURRENT world matrix then carries it wherever the bone is NOW - so a bone
    /// still sitting exactly at its own rest pose (an identity skinning matrix)
    /// contributes its rest-position vertices completely unchanged, and rotating a
    /// PARENT bone changes every descendant bone's own <see cref="Node.GetWorldTransform"/>
    /// (ordinary Forward Kinematics - see <see cref="BoneData"/>'s own remarks), which
    /// this method picks up for free with no bone-hierarchy-specific code of its own at
    /// all.
    /// </summary>
    public static class SkinningEvaluator
    {
        /// <summary>Deforms <paramref name="mesh"/> against <paramref name="binding"/>'s
        /// current bone poses - <paramref name="meshWorldTransform"/> is the SKINNED
        /// NODE's own current world transform (<see cref="Node.GetWorldTransform"/>),
        /// needed to round-trip each vertex out to world space and back (bone matrices
        /// live in world space; <see cref="Mesh.Vertices"/> positions are always in the
        /// owning node's own local space, the same convention every other mesh-producing
        /// step in this project already assumes). Returns <paramref name="mesh"/>
        /// completely UNCHANGED (no copy at all) if <paramref name="binding"/> is null or
        /// has no bones, or if <paramref name="meshWorldTransform"/> happens to be
        /// non-invertible (a degenerate/zero-scale transform somewhere in this node's own
        /// parent chain) - the same "nothing to do, don't manufacture a copy" latitude
        /// <see cref="Modifiers.ModifierStack.Evaluate"/>'s own empty-stack case already
        /// has, and the same "bail out rather than corrupt everything into NaN" latitude
        /// <see cref="Node.CompensateMeshForOriginChange"/>'s own non-invertible-transform
        /// guard already has.</summary>
        public static Mesh Deform(Mesh mesh, Matrix4x4 meshWorldTransform, SkinBinding? binding)
        {
            ArgumentNullException.ThrowIfNull(mesh);
            if (binding is null || binding.Bones.Count == 0) return mesh;
            if (!Matrix4x4.Invert(meshWorldTransform, out var worldToMesh)) return mesh;

            var skinningMatrices = new Matrix4x4[binding.Bones.Count];
            for (var i = 0; i < binding.Bones.Count; i++)
            {
                var bone = binding.Bones[i];
                var inverseBind = bone.Bone?.GetInverseBindMatrix() ?? Matrix4x4.Identity;
                skinningMatrices[i] = inverseBind * bone.GetWorldTransform();
            }

            var output = new Mesh(mesh.Name) { Material = mesh.Material };
            foreach (var vertex in mesh.Vertices)
                output.AddVertex(DeformVertex(vertex, meshWorldTransform, worldToMesh, skinningMatrices));

            foreach (var face in mesh.Faces) output.AddFace(face);
            foreach (var polygon in mesh.Polygons) output.AddPolygon(new Polygon(polygon.Indices) { MaterialSlotIndex = polygon.MaterialSlotIndex });

            // Recompute from the DEFORMED geometry, the same "never transform normals
            // directly, always re-derive from the resulting shape" convention every
            // other vertex-repositioning operation in this project already follows
            // (Subdivide/LoopCut/BevelVertex/ExtrudeFace/CompensateMeshForOriginChange).
            output.RecalculateNormals();
            return output;
        }

        private static Vertex DeformVertex(Vertex vertex, Matrix4x4 meshWorldTransform, Matrix4x4 worldToMesh, Matrix4x4[] skinningMatrices)
        {
            var weights = vertex.BoneWeights;
            var totalWeight = weights.X + weights.Y + weights.Z + weights.W;

            // An unpainted/unweighted vertex (the shared default for every vertex
            // authored before it was ever bound to a skeleton) has nothing to blend at
            // all - left at its own rest/authored position, exactly like a vertex a
            // Mirror/Solidify modifier never touches.
            if (totalWeight < 1e-6f) return vertex;

            var restWorldPosition = Vector3.Transform(vertex.Position, meshWorldTransform);
            var blendedWorldPosition = Vector3.Zero;

            blendedWorldPosition += AccumulateInfluence(vertex.BoneIndices.X, weights.X);
            blendedWorldPosition += AccumulateInfluence(vertex.BoneIndices.Y, weights.Y);
            blendedWorldPosition += AccumulateInfluence(vertex.BoneIndices.Z, weights.Z);
            blendedWorldPosition += AccumulateInfluence(vertex.BoneIndices.W, weights.W);

            var deformedLocalPosition = Vector3.Transform(blendedWorldPosition, worldToMesh);
            return vertex.WithPosition(deformedLocalPosition);

            Vector3 AccumulateInfluence(int boneIndex, float weight)
            {
                if (weight <= 0f || boneIndex < 0 || boneIndex >= skinningMatrices.Length) return Vector3.Zero;
                var normalizedWeight = weight / totalWeight;
                return normalizedWeight * Vector3.Transform(restWorldPosition, skinningMatrices[boneIndex]);
            }
        }
    }
}
