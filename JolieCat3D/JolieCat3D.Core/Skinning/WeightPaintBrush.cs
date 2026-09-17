using System.Numerics;
using JolieCat3D.Core.Geometry;

namespace JolieCat3D.Core.Skinning
{
    /// <summary>
    /// The Weight Paint mode brush's own backend: given a world-space hit point (from a
    /// raycast against the mesh being painted) and a radius, blends every vertex within
    /// that radius toward a target weight for one chosen bone slot, then proportionally
    /// rescales the vertex's OTHER (up to 3) existing bone influences so all 4 slots
    /// still sum to 1 - the same "changing one influence redistributes the rest, it
    /// doesn't just push the total over 1" behavior every real weight-paint tool has.
    /// Deliberately independent of any viewport/mouse/raycast concern (those are
    /// <c>JolieCat3D.Engine</c>'s/<c>JolieCat3D.UI</c>'s own job - turning a mouse
    /// drag into a world hit point and a call to <see cref="Apply"/>) - a caller
    /// supplies the already-resolved hit point, so this is exercisable (and was
    /// exercised) from a standalone script with no viewport at all.
    /// </summary>
    public static class WeightPaintBrush
    {
        /// <summary>Applies one brush "tick" (one mouse-move sample during a paint
        /// drag) to every vertex of <paramref name="mesh"/> within <paramref name="radius"/>
        /// of <paramref name="worldHitPoint"/>. <paramref name="targetWeight"/> (0-1,
        /// clamped) is what the brush pushes <paramref name="activeBoneIndex"/>'s own
        /// weight TOWARD - 1 for the usual "paint this bone's influence in", 0 for
        /// "erase it" (both directions share the exact same blend-then-renormalize
        /// logic, just aiming at a different target). <paramref name="strength"/> (0-1)
        /// is how far a single tick moves toward that target (1 snaps immediately; a
        /// smaller value needs several ticks/drag samples to fully converge, the same
        /// "repeated strokes build up the effect" feel every real brush has).
        /// Vertices outside <paramref name="radius"/> are left completely untouched.</summary>
        public static void Apply(Mesh mesh, Matrix4x4 meshWorldTransform, Vector3 worldHitPoint, float radius, int activeBoneIndex, float targetWeight, float strength)
        {
            ArgumentNullException.ThrowIfNull(mesh);
            if (radius <= 0f || strength <= 0f || activeBoneIndex < 0) return;

            targetWeight = Math.Clamp(targetWeight, 0f, 1f);
            strength = Math.Clamp(strength, 0f, 1f);

            for (var i = 0; i < mesh.Vertices.Count; i++)
            {
                var vertex = mesh.Vertices[i];
                var worldPosition = Vector3.Transform(vertex.Position, meshWorldTransform);
                var distance = Vector3.Distance(worldPosition, worldHitPoint);
                if (distance > radius) continue;

                // Linear falloff: full strength at the brush's own center, fading to
                // none at its edge - a soft-edged brush, not a hard binary cutoff, the
                // same "falloff toward the radius's own edge" idea Proportional Editing
                // already established elsewhere in this project (a simpler, linear
                // curve here - nothing about painting a weight demands that feature's
                // own smoothstep shape).
                var falloff = 1f - distance / radius;
                var blend = Math.Clamp(strength * falloff, 0f, 1f);
                if (blend <= 0f) continue;

                var (indices, weights) = BlendTowardTarget(vertex.BoneIndices, vertex.BoneWeights, activeBoneIndex, targetWeight, blend);
                mesh.SetVertexBoneWeights(i, indices, weights);
            }
        }

        /// <summary>The actual per-vertex blend: finds (or makes room for)
        /// <paramref name="activeBoneIndex"/> among the vertex's own 4 slots, moves
        /// that slot's weight <paramref name="blend"/> of the way toward
        /// <paramref name="targetWeight"/>, and proportionally rescales every OTHER
        /// occupied slot so the 4 weights still sum to 1.</summary>
        private static (BoneIndices Indices, Vector4 Weights) BlendTowardTarget(BoneIndices indices, Vector4 weights, int activeBoneIndex, float targetWeight, float blend)
        {
            Span<int> slotIndices = stackalloc int[4] { indices.X, indices.Y, indices.Z, indices.W };
            Span<float> slotWeights = stackalloc float[4] { weights.X, weights.Y, weights.Z, weights.W };

            var activeSlot = -1;
            for (var slot = 0; slot < 4; slot++)
                if (slotWeights[slot] > 1e-6f && slotIndices[slot] == activeBoneIndex) { activeSlot = slot; break; }

            if (activeSlot < 0)
            {
                // Not currently one of this vertex's influences - claim an empty slot
                // if one exists, otherwise evict whichever slot currently contributes
                // the LEAST (the standard "adding a new influence bumps out the
                // weakest one" behavior once all 4 slots are already spoken for).
                activeSlot = 0;
                for (var slot = 0; slot < 4; slot++)
                    if (slotWeights[slot] < slotWeights[activeSlot]) activeSlot = slot;

                slotIndices[activeSlot] = activeBoneIndex;
                slotWeights[activeSlot] = 0f;
            }

            var otherOldTotal = 0f;
            for (var slot = 0; slot < 4; slot++)
                if (slot != activeSlot) otherOldTotal += slotWeights[slot];

            var newActiveWeight = float.Lerp(slotWeights[activeSlot], targetWeight, blend);
            var remainingForOthers = MathF.Max(0f, 1f - newActiveWeight);

            if (otherOldTotal > 1e-6f)
            {
                var scale = remainingForOthers / otherOldTotal;
                for (var slot = 0; slot < 4; slot++)
                    if (slot != activeSlot) slotWeights[slot] *= scale;
            }
            // otherOldTotal ~0 (no other influences painted yet): nothing to
            // redistribute - the other slots simply stay at 0, and newActiveWeight
            // alone (implicitly renormalized by SkinningEvaluator regardless) carries
            // the vertex.

            slotWeights[activeSlot] = newActiveWeight;

            return (new BoneIndices(slotIndices[0], slotIndices[1], slotIndices[2], slotIndices[3]),
                new Vector4(slotWeights[0], slotWeights[1], slotWeights[2], slotWeights[3]));
        }
    }
}
