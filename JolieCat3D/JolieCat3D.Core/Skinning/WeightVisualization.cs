using System.Numerics;
using JolieCat3D.Core.Geometry;

namespace JolieCat3D.Core.Skinning
{
    /// <summary>
    /// Builds a throwaway COPY of a mesh with every vertex's own U texture coordinate
    /// overwritten by that vertex's own current weight (0-1) for one chosen bone - the
    /// data half of the Weight Paint mode heat-map overlay. Deliberately produces plain
    /// <see cref="Geometry.Vertex.UV"/> data, not any actual color - WPF's fixed-function
    /// 3D pipeline has no native per-vertex color channel at all (see
    /// <c>JolieCat3D.Engine.Geometry.MeshGeometryFactory.Create</c>'s own remarks), so
    /// <c>JolieCat3D.Engine</c> is the one that turns this U coordinate into an actual
    /// blue-to-red heat-map color, by shading with a horizontal gradient brush sampled
    /// through it - the same "bake the visualization into a UV-sampled brush" workaround
    /// that same remark names as the one available option. Never mutates the real mesh
    /// (its own genuine UVs, used for actual texturing, are completely unaffected) - this
    /// is a disposable, render-only copy, rebuilt fresh every time the active bone/paint
    /// state changes.
    /// </summary>
    public static class WeightVisualization
    {
        public static Mesh BuildHeatmapMesh(Mesh mesh, int activeBoneIndex)
        {
            ArgumentNullException.ThrowIfNull(mesh);

            var output = new Mesh(mesh.Name) { Material = mesh.Material };
            foreach (var vertex in mesh.Vertices)
            {
                var weight = WeightForBone(vertex.BoneIndices, vertex.BoneWeights, activeBoneIndex);
                output.AddVertex(vertex.WithUV(new Vector2(weight, 0.5f)));
            }

            foreach (var face in mesh.Faces) output.AddFace(face);
            foreach (var polygon in mesh.Polygons) output.AddPolygon(new Polygon(polygon.Indices) { MaterialSlotIndex = polygon.MaterialSlotIndex });

            return output;
        }

        private static float WeightForBone(BoneIndices indices, Vector4 weights, int boneIndex)
        {
            var total = weights.X + weights.Y + weights.Z + weights.W;
            if (total < 1e-6f) return 0f;

            var sum = 0f;
            if (indices.X == boneIndex) sum += weights.X;
            if (indices.Y == boneIndex) sum += weights.Y;
            if (indices.Z == boneIndex) sum += weights.Z;
            if (indices.W == boneIndex) sum += weights.W;

            return Math.Clamp(sum / total, 0f, 1f);
        }
    }
}
