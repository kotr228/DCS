using System.Numerics;
using JolieCat3D.Core.Geometry;

namespace JolieCat3D.Core.Sculpting
{
    /// <summary>
    /// Sculpt Mode's own 3 brush algorithms - Draw, Smooth, Grab. The same "world hit
    /// point + radius, one tick per mouse-move sample, mutate the RAW <see cref="Scene.Node.Mesh"/>
    /// directly" shape <see cref="Materials.VertexPaintBrush"/>/<see cref="Skinning.WeightPaintBrush"/>
    /// already established for their own paint modes, just displacing POSITION instead
    /// of a paint value. Every method here recalculates normals once at its own end
    /// (never leaving stale ones behind after moving vertices), and never adds/removes
    /// a single vertex, face, or polygon - only <see cref="Mesh.SetVertexPosition"/>
    /// ever runs, so a mesh's own index buffers (<see cref="Mesh.Faces"/>/
    /// <see cref="Mesh.Polygons"/>) are bit-for-bit unchanged after any stroke, however
    /// long - the task's own "without destroying the index buffer topology" ask.
    /// </summary>
    public static class SculptBrush
    {
        /// <summary>Displaces every vertex within <paramref name="radius"/> of
        /// <paramref name="worldHitPoint"/> along the SHARED average normal of the
        /// whole affected area (not each vertex's own individual normal - the task's
        /// own wording) - the standard "Draw" brush, pushing a bump (or, with
        /// <paramref name="invert"/>, a dent) into the surface. <paramref name="strength"/>
        /// is a real world-unit displacement magnitude (not a 0-1 blend factor the way
        /// <see cref="Materials.VertexPaintBrush.Apply"/>'s own Strength is) - Draw
        /// genuinely moves geometry, it doesn't blend toward some existing target
        /// value.</summary>
        public static void ApplyDraw(Mesh mesh, Matrix4x4 meshWorldTransform, Vector3 worldHitPoint, float radius, float strength, bool invert)
        {
            ArgumentNullException.ThrowIfNull(mesh);
            var affected = FindAffected(mesh, meshWorldTransform, radius, worldHitPoint);
            if (affected.Count == 0) return;
            if (!Matrix4x4.Invert(meshWorldTransform, out var worldToMesh)) return;

            var averageWorldNormal = Vector3.Zero;
            foreach (var (index, _) in affected)
                averageWorldNormal += Vector3.TransformNormal(mesh.Vertices[index].Normal, meshWorldTransform);

            // A perfectly canceling set of opposing normals (a razor-thin double-sided
            // sliver, or a brush spanning a mesh's own two opposite sides) has no single
            // sane "outward" direction to draw along at all - left completely
            // untouched rather than picking an arbitrary one.
            if (averageWorldNormal.LengthSquared() < 1e-12f) return;
            averageWorldNormal = Vector3.Normalize(averageWorldNormal);

            var signedStrength = invert ? -strength : strength;
            foreach (var (index, falloff) in affected)
            {
                var worldDisplacement = averageWorldNormal * (signedStrength * falloff);
                var localDisplacement = Vector3.TransformNormal(worldDisplacement, worldToMesh);
                mesh.SetVertexPosition(index, mesh.Vertices[index].Position + localDisplacement);
            }

            mesh.RecalculateNormals();
        }

        /// <summary>Relaxes every vertex within <paramref name="radius"/> of
        /// <paramref name="worldHitPoint"/> toward the average position of its own
        /// TOPOLOGICAL neighbors (every other vertex directly connected to it by an
        /// edge - see <see cref="Mesh.GetEdges"/>) - the standard "Smooth" brush,
        /// removing sharp/noisy detail without changing the mesh's own overall shape.
        ///
        /// Adjacency is computed by POSITION, not raw vertex index (see
        /// <see cref="Mesh.WeldVertices"/>'s own remarks on why - this engine
        /// deliberately duplicates vertices per face for flat shading, e.g.
        /// <see cref="Geometry.Primitives.CreateCube"/>'s own hard edges, so a real
        /// mesh corner is usually represented by SEVERAL distinct vertex entries all
        /// sitting at the same position). Averaging by raw index alone would smooth
        /// each of those duplicate copies toward a DIFFERENT target (whatever's
        /// reachable through ITS OWN single face), pulling a shared corner apart into
        /// several different positions and visibly tearing the mesh open at every
        /// edge - averaging PER POSITION GROUP instead, then writing the SAME new
        /// position back to every vertex in that group, keeps every such group of
        /// duplicates coincident before and after, exactly the "round the corners
        /// without destroying the topology" behavior a cube needs to still read as one
        /// solid, closed shape afterward.</summary>
        public static void ApplySmooth(Mesh mesh, Matrix4x4 meshWorldTransform, Vector3 worldHitPoint, float radius, float strength)
        {
            ArgumentNullException.ThrowIfNull(mesh);

            var (groupOf, groupMembers, groupPosition) = BuildPositionGroups(mesh);
            var groupNeighbors = BuildGroupAdjacency(mesh, groupOf);

            var affectedGroups = new List<(int Group, float Falloff)>();
            for (var group = 0; group < groupPosition.Length; group++)
            {
                var worldPosition = Vector3.Transform(groupPosition[group], meshWorldTransform);
                var distance = Vector3.Distance(worldPosition, worldHitPoint);
                if (distance > radius) continue;
                affectedGroups.Add((group, 1f - distance / radius));
            }
            if (affectedGroups.Count == 0) return;

            // Every affected group's own new position is computed from the ORIGINAL
            // (pre-stroke) positions of every group, all at once - never from a
            // neighbor's ALREADY-updated position, so the result doesn't depend on
            // which order groups happen to be processed in.
            var newGroupPositions = new Dictionary<int, Vector3>(affectedGroups.Count);
            foreach (var (group, falloff) in affectedGroups)
            {
                if (!groupNeighbors.TryGetValue(group, out var neighbors) || neighbors.Count == 0) continue;

                var average = Vector3.Zero;
                foreach (var neighbor in neighbors) average += groupPosition[neighbor];
                average /= neighbors.Count;

                newGroupPositions[group] = Vector3.Lerp(groupPosition[group], average, Math.Clamp(strength * falloff, 0f, 1f));
            }

            foreach (var (group, newPosition) in newGroupPositions)
                foreach (var vertexIndex in groupMembers[group])
                    mesh.SetVertexPosition(vertexIndex, newPosition);

            mesh.RecalculateNormals();
        }

        /// <summary>Translates every vertex within <paramref name="radius"/> of
        /// <paramref name="strokeOriginWorld"/> by <paramref name="worldDelta"/>
        /// (falling off with distance from that SAME origin, captured once at the
        /// start of the drag) - the standard "Grab" brush. Unlike Draw/Smooth (which
        /// re-evaluate "what's within the brush" fresh every tick, since the brush
        /// itself doesn't move), Grab's own affected set and each vertex's own falloff
        /// are meant to be fixed for the WHOLE drag from the moment it starts - see
        /// <c>Engine.Editing.SculptSession</c>'s own remarks on why it captures
        /// <paramref name="originalLocalPositions"/> once, up front, and calls this
        /// method repeatedly (once per mouse-move sample) with the SAME captured
        /// positions and a fresh CUMULATIVE <paramref name="worldDelta"/> each time,
        /// rather than this method re-deriving anything from the mesh's own
        /// (already-moved) current state.</summary>
        public static void ApplyGrab(Mesh mesh, Matrix4x4 meshWorldTransform, IReadOnlyDictionary<int, (Vector3 LocalPosition, float Falloff)> originalLocalPositions, Vector3 worldDelta, float strength)
        {
            ArgumentNullException.ThrowIfNull(mesh);
            if (originalLocalPositions.Count == 0) return;
            if (!Matrix4x4.Invert(meshWorldTransform, out var worldToMesh)) return;

            var localDelta = Vector3.TransformNormal(worldDelta, worldToMesh);

            foreach (var (index, (originalPosition, falloff)) in originalLocalPositions)
                mesh.SetVertexPosition(index, originalPosition + localDelta * (falloff * strength));

            mesh.RecalculateNormals();
        }

        /// <summary>Every vertex within <paramref name="radius"/> of
        /// <paramref name="worldHitPoint"/>, paired with its own LINEAR falloff (1 at
        /// the brush's own center, fading to 0 at its edge) - the same falloff shape
        /// <see cref="Materials.VertexPaintBrush.Apply"/>/<see cref="Skinning.WeightPaintBrush.Apply"/>
        /// already use.</summary>
        public static List<(int Index, float Falloff)> FindAffected(Mesh mesh, Matrix4x4 meshWorldTransform, float radius, Vector3 worldHitPoint)
        {
            ArgumentNullException.ThrowIfNull(mesh);

            var result = new List<(int, float)>();
            for (var i = 0; i < mesh.Vertices.Count; i++)
            {
                var worldPosition = Vector3.Transform(mesh.Vertices[i].Position, meshWorldTransform);
                var distance = Vector3.Distance(worldPosition, worldHitPoint);
                if (distance > radius) continue;
                result.Add((i, 1f - distance / radius));
            }

            return result;
        }

        /// <summary>Groups every vertex index by its own (quantized) local position -
        /// the same tolerance/quantization <see cref="Mesh.WeldVertices"/> itself uses,
        /// duplicated here (rather than shared) since it's small, self-contained, and
        /// this class has no other reason to depend on <see cref="Mesh"/>'s own private
        /// implementation details. Returns: a per-vertex group id, each group's own
        /// member vertex indices, and each group's own representative position (any one
        /// member's - they're all identical by construction).</summary>
        private static (int[] GroupOf, List<int>[] Members, Vector3[] Position) BuildPositionGroups(Mesh mesh)
        {
            const float tolerance = 1e-4f;
            var scale = 1f / tolerance;

            var groupOf = new int[mesh.Vertices.Count];
            var members = new List<List<int>>();
            var positions = new List<Vector3>();
            var keyToGroup = new Dictionary<(long, long, long), int>();

            for (var i = 0; i < mesh.Vertices.Count; i++)
            {
                var position = mesh.Vertices[i].Position;
                var key = ((long)MathF.Round(position.X * scale), (long)MathF.Round(position.Y * scale), (long)MathF.Round(position.Z * scale));

                if (!keyToGroup.TryGetValue(key, out var group))
                {
                    group = members.Count;
                    keyToGroup[key] = group;
                    members.Add(new List<int>());
                    positions.Add(position);
                }

                groupOf[i] = group;
                members[group].Add(i);
            }

            return (groupOf, members.ToArray(), positions.ToArray());
        }

        /// <summary>Which OTHER position groups each group shares a mesh edge with -
        /// <see cref="Mesh.GetEdges"/>'s own raw index pairs, remapped through
        /// <paramref name="groupOf"/> so an edge between two duplicated-per-face
        /// corners that happen to sit at the same real-world position is recognized as
        /// the SAME group, not two unrelated, unconnected ones.</summary>
        private static Dictionary<int, List<int>> BuildGroupAdjacency(Mesh mesh, int[] groupOf)
        {
            var adjacency = new Dictionary<int, List<int>>();

            void AddEdge(int a, int b)
            {
                if (a == b) return;
                if (!adjacency.TryGetValue(a, out var list)) adjacency[a] = list = new List<int>();
                if (!list.Contains(b)) list.Add(b);
            }

            foreach (var (a, b) in mesh.GetEdges())
            {
                var groupA = groupOf[a];
                var groupB = groupOf[b];
                AddEdge(groupA, groupB);
                AddEdge(groupB, groupA);
            }

            return adjacency;
        }
    }
}
