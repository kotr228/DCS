using System.Windows.Media.Media3D;
using JolieCat3D.Engine.Rendering;
using CoreNode = JolieCat3D.Core.Scene.Node;
using CoreScene = JolieCat3D.Core.Scene.Scene3D;
using MediaQuaternion = System.Windows.Media.Media3D.Quaternion;

namespace JolieCat3D.Engine.Geometry
{
    /// <summary>
    /// Converts a <c>JolieCat3D.Core</c> scene graph (<see cref="CoreNode"/>/<see cref="CoreScene"/>)
    /// into a WPF <see cref="Model3DGroup"/> tree, combining <see cref="MeshGeometryFactory"/>
    /// and <see cref="MaterialFactory"/> for each meshed node and recreating each node's
    /// own local Scale/Rotate/Translate transform (see <see cref="CoreNode.GetLocalTransform"/>)
    /// as a WPF <see cref="Transform3DGroup"/> - nesting a child's <see cref="Model3DGroup"/>
    /// inside its parent's is what makes WPF's own model tree compose parent/child
    /// transforms the same way <see cref="CoreNode.GetWorldTransform"/> does, so this never
    /// needs to flatten a node's world matrix by hand.
    /// </summary>
    public static class SceneGraphBuilder
    {
        /// <summary>Builds one <see cref="Model3DGroup"/> per root node in
        /// <paramref name="scene"/>, combined into a single top-level group.
        /// <paramref name="modelToNode"/>, if given, gets one entry added per
        /// <see cref="GeometryModel3D"/> created, mapping it back to the <see cref="CoreNode"/>
        /// it came from - what lets a viewport click (which WPF reports as a hit
        /// <see cref="GeometryModel3D"/>, not a <see cref="CoreNode"/>) resolve back to a
        /// selectable scene object at all; see <c>JolieCat3D.Engine.Selection.SceneHitTester</c>.</summary>
        public static Model3DGroup Build(CoreScene scene, IDictionary<GeometryModel3D, CoreNode>? modelToNode = null, ShadingMode shadingMode = ShadingMode.Rendered)
        {
            ArgumentNullException.ThrowIfNull(scene);

            var group = new Model3DGroup();
            foreach (var root in scene.RootNodes)
                group.Children.Add(Build(root, modelToNode, shadingMode));

            return group;
        }

        /// <summary>Builds <paramref name="node"/>'s own <see cref="GeometryModel3D"/>
        /// (if it has a <see cref="CoreNode.Mesh"/>) plus every child's, all inside one
        /// <see cref="Model3DGroup"/> carrying this node's own local transform - so the
        /// result, placed anywhere in a visual tree, renders this node and its whole
        /// subtree exactly where <see cref="CoreNode.GetWorldTransform"/> would put them
        /// relative to whatever <paramref name="node"/> is itself nested under.
        /// <paramref name="shadingMode"/> only changes which <see cref="Material"/>
        /// <see cref="MaterialFactory.Create(Core.Materials.Material?,ShadingMode)"/>
        /// builds - the geometry and transform are identical either way (a caller
        /// wanting <see cref="ShadingMode.Wireframe"/>'s own "no filled geometry at all"
        /// behavior skips calling this in the first place - see <see cref="Scene3DRenderer.Render"/>).</summary>
        public static Model3DGroup Build(CoreNode node, IDictionary<GeometryModel3D, CoreNode>? modelToNode = null, ShadingMode shadingMode = ShadingMode.Rendered)
        {
            ArgumentNullException.ThrowIfNull(node);

            var group = new Model3DGroup { Transform = ToTransform3D(node) };

            if (node.Mesh is { } mesh)
            {
                var material = MaterialFactory.Create(mesh.Material, shadingMode);
                var model = new GeometryModel3D(MeshGeometryFactory.Create(mesh), material)
                {
                    // Lets the same material shade the mesh from either side - a mesh
                    // authored with outward-only normals (Primitives.CreateCube included)
                    // would otherwise render invisible/black from behind its own faces,
                    // which reads as a bug to anyone orbiting the camera around it.
                    BackMaterial = material,
                };
                group.Children.Add(model);
                if (modelToNode is not null) modelToNode[model] = node;
            }

            foreach (var child in node.Children)
                group.Children.Add(Build(child, modelToNode, shadingMode));

            return group;
        }

        /// <summary>Scale, then Rotate, then Translate - matching <see cref="CoreNode.GetLocalTransform"/>'s
        /// own SRT order exactly, via <see cref="Transform3DGroup"/>'s documented
        /// behavior of applying its <see cref="Transform3DGroup.Children"/> in list order.</summary>
        private static Transform3D ToTransform3D(CoreNode node)
        {
            var transform = new Transform3DGroup();
            transform.Children.Add(new ScaleTransform3D(node.LocalScale.X, node.LocalScale.Y, node.LocalScale.Z));
            transform.Children.Add(new RotateTransform3D(new QuaternionRotation3D(ToMediaQuaternion(node.LocalRotation))));
            transform.Children.Add(new TranslateTransform3D(node.LocalPosition.X, node.LocalPosition.Y, node.LocalPosition.Z));

            transform.Freeze();
            return transform;
        }

        private static MediaQuaternion ToMediaQuaternion(System.Numerics.Quaternion q) => new(q.X, q.Y, q.Z, q.W);
    }
}
