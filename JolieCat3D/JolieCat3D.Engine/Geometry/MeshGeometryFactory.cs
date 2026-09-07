using System.Windows.Media.Media3D;
using CoreMesh = JolieCat3D.Core.Geometry.Mesh;

namespace JolieCat3D.Engine.Geometry
{
    /// <summary>
    /// Converts a platform-agnostic <see cref="CoreMesh"/> into a GPU-renderable WPF
    /// <see cref="MeshGeometry3D"/> - the actual "adapter" half of
    /// <c>JolieCat3D.Core</c> -&gt; <c>JolieCat3D.Engine</c> mapping for geometry (see
    /// <see cref="MaterialFactory"/> for the material half, and <see cref="SceneGraphBuilder"/>
    /// for the scene-graph/transform half that puts both together).
    /// </summary>
    public static class MeshGeometryFactory
    {
        /// <summary>
        /// Builds Positions/Normals/TextureCoordinates/TriangleIndices from
        /// <paramref name="mesh"/>'s own <see cref="CoreMesh.Vertices"/> and
        /// <see cref="CoreMesh.GetRenderFaces"/> (which already triangulates any
        /// authoring-time <see cref="Core.Geometry.Polygon"/>, so this never needs to
        /// know about that distinction). One <see cref="Core.Geometry.Vertex"/> in the
        /// source mesh becomes exactly one WPF vertex at the same index, so
        /// <see cref="Core.Geometry.Face"/>'s indices carry across unchanged.
        /// </summary>
        /// <remarks>
        /// WPF's fixed-function 3D pipeline has no native per-vertex color channel -
        /// <see cref="MeshGeometry3D"/> only carries position/normal/texture-coordinate
        /// per vertex, not an RGBA color one. <see cref="Core.Geometry.Vertex.Color"/> is
        /// therefore not represented in the geometry this method returns; it currently
        /// has no effect on rendering at all (a mesh's overall appearance instead comes
        /// from its <see cref="MaterialFactory"/>-built material). This is a real
        /// limitation of the target rendering API, not an oversight - documented rather
        /// than silently dropped or faked via an unrequested workaround (e.g. baking
        /// vertex colors into a generated texture).
        /// </remarks>
        public static MeshGeometry3D Create(CoreMesh mesh)
        {
            ArgumentNullException.ThrowIfNull(mesh);

            var geometry = new MeshGeometry3D();

            foreach (var vertex in mesh.Vertices)
            {
                geometry.Positions.Add(new Point3D(vertex.Position.X, vertex.Position.Y, vertex.Position.Z));
                geometry.Normals.Add(new Vector3D(vertex.Normal.X, vertex.Normal.Y, vertex.Normal.Z));
                geometry.TextureCoordinates.Add(new System.Windows.Point(vertex.UV.X, vertex.UV.Y));
            }

            foreach (var face in mesh.GetRenderFaces())
            {
                geometry.TriangleIndices.Add(face.A);
                geometry.TriangleIndices.Add(face.B);
                geometry.TriangleIndices.Add(face.C);
            }

            // MeshGeometry3D's Freeze() lets WPF's render thread use it without any
            // further cross-thread access checks - worthwhile here since a converted
            // mesh's geometry is never mutated in place (an edit always rebuilds a new
            // one from the updated Core.Geometry.Mesh instead).
            geometry.Freeze();
            return geometry;
        }
    }
}
