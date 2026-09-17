using System.Windows.Media;
using System.Windows.Media.Media3D;
using CoreMaterial = JolieCat3D.Core.Materials.Material;
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
        /// <summary>The single shared "no material" dictionary-key sentinel
        /// <see cref="CreateGroups"/>'s own grouping uses in place of a literal null key -
        /// see that method's own remarks.</summary>
        private static readonly CoreMaterial NoMaterialSentinel = CoreMaterial.CreateDefault();

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

        /// <summary>
        /// Multi-Material Support's own rendering split: groups <paramref name="mesh"/>'s
        /// own triangles by whichever <see cref="CoreMaterial"/> each one should
        /// actually render with (a raw <see cref="Core.Geometry.Face"/> triangle, or a
        /// <see cref="Core.Geometry.Polygon"/> with no <see cref="Core.Geometry.Polygon.MaterialSlotIndex"/>
        /// override, always uses <paramref name="mesh"/>'s own plain
        /// <see cref="CoreMesh.Material"/> - see <see cref="CoreMesh.GetEffectiveMaterial"/>),
        /// returning one <see cref="MeshGeometry3D"/> per DISTINCT material actually
        /// used - <see cref="SceneGraphBuilder"/> builds one <see cref="GeometryModel3D"/>
        /// per entry, since WPF's fixed-function <see cref="System.Windows.Media.Media3D.Model3D"/>
        /// has no way to vary material WITHIN a single <see cref="MeshGeometry3D"/> at
        /// all. Every group's own geometry shares the exact SAME frozen Positions/
        /// Normals/TextureCoordinates collections (a vertex's own position doesn't
        /// depend on which material renders the triangles touching it) - only
        /// TriangleIndices differs per group, so this costs one pass over
        /// <see cref="CoreMesh.Vertices"/> total, not one per group. For a mesh with no
        /// per-polygon material overrides at all (every mesh authored before Multi-
        /// Material Support existed), this always returns exactly one group - pixel-
        /// identical to what <see cref="Create"/> alone already produced, so nothing
        /// about how an ordinary single-material mesh renders changes at all.
        /// </summary>
        public static IReadOnlyList<(CoreMaterial? Material, MeshGeometry3D Geometry)> CreateGroups(CoreMesh mesh)
        {
            ArgumentNullException.ThrowIfNull(mesh);

            var positions = new Point3DCollection(mesh.Vertices.Count);
            var normals = new Vector3DCollection(mesh.Vertices.Count);
            var textureCoordinates = new PointCollection(mesh.Vertices.Count);

            foreach (var vertex in mesh.Vertices)
            {
                positions.Add(new Point3D(vertex.Position.X, vertex.Position.Y, vertex.Position.Z));
                normals.Add(new Vector3D(vertex.Normal.X, vertex.Normal.Y, vertex.Normal.Z));
                textureCoordinates.Add(new System.Windows.Point(vertex.UV.X, vertex.UV.Y));
            }

            positions.Freeze();
            normals.Freeze();
            textureCoordinates.Freeze();

            // Dictionary<TKey,...> requires a non-null key - a mesh's own plain Material
            // can legitimately be null (no material at all), so a single shared sentinel
            // instance stands in for "no material" as the dictionary key; passing it
            // straight through to MaterialFactory.Create afterward is exactly as correct
            // as passing null would have been - that method's own first line already
            // substitutes CreateDefault() for a null material anyway.
            var trianglesByMaterial = new Dictionary<CoreMaterial, List<int>>();

            List<int> GetGroup(CoreMaterial? material)
            {
                var key = material ?? NoMaterialSentinel;
                if (!trianglesByMaterial.TryGetValue(key, out var indices))
                    trianglesByMaterial[key] = indices = new List<int>();
                return indices;
            }

            foreach (var face in mesh.Faces)
            {
                var group = GetGroup(mesh.Material);
                group.Add(face.A);
                group.Add(face.B);
                group.Add(face.C);
            }

            foreach (var polygon in mesh.Polygons)
            {
                var group = GetGroup(mesh.GetEffectiveMaterial(polygon));
                foreach (var triangle in polygon.Triangulate())
                {
                    group.Add(triangle.A);
                    group.Add(triangle.B);
                    group.Add(triangle.C);
                }
            }

            var result = new List<(CoreMaterial?, MeshGeometry3D)>(trianglesByMaterial.Count);
            foreach (var (material, indices) in trianglesByMaterial)
            {
                var geometry = new MeshGeometry3D
                {
                    Positions = positions,
                    Normals = normals,
                    TextureCoordinates = textureCoordinates,
                };
                foreach (var index in indices) geometry.TriangleIndices.Add(index);

                geometry.Freeze();
                result.Add((material, geometry));
            }

            return result;
        }
    }
}
