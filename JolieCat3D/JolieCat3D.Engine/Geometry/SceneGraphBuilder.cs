using System.Windows.Media;
using System.Windows.Media.Media3D;
using JolieCat3D.Core.Modifiers;
using JolieCat3D.Core.Skinning;
using JolieCat3D.Engine.Rendering;
using CoreNode = JolieCat3D.Core.Scene.Node;
using CoreScene = JolieCat3D.Core.Scene.Scene3D;
using MediaQuaternion = System.Windows.Media.Media3D.Quaternion;

namespace JolieCat3D.Engine.Geometry
{
    /// <summary>Weight Paint mode's own heat-map override - see <see cref="SceneGraphBuilder"/>'s
    /// own remarks. When <see cref="Target"/> matches the node currently being built,
    /// that ONE node renders with a blue-to-red gradient sampled through
    /// <see cref="Core.Skinning.WeightVisualization.BuildHeatmapMesh"/>'s own UV remap
    /// (<see cref="ActiveBoneIndex"/>'s own current weight per vertex) instead of its
    /// normal material - every other node (including every other skinned one) still
    /// renders completely normally.</summary>
    public sealed record WeightPaintOverlay(CoreNode Target, int ActiveBoneIndex);

    /// <summary>
    /// Converts a <c>JolieCat3D.Core</c> scene graph (<see cref="CoreNode"/>/<see cref="CoreScene"/>)
    /// into a WPF <see cref="Model3DGroup"/> tree, combining <see cref="MeshGeometryFactory"/>
    /// and <see cref="MaterialFactory"/> for each meshed node and recreating each node's
    /// own local Scale/Rotate/Translate transform (see <see cref="CoreNode.GetLocalTransform"/>)
    /// as a WPF <see cref="Transform3DGroup"/> - nesting a child's <see cref="Model3DGroup"/>
    /// inside its parent's is what makes WPF's own model tree compose parent/child
    /// transforms the same way <see cref="CoreNode.GetWorldTransform"/> does, so this never
    /// needs to flatten a node's world matrix by hand. Before actually building GPU
    /// geometry for a node's mesh, its own <see cref="CoreNode.Modifiers"/> stack is
    /// evaluated first (see <see cref="ModifierStack.Evaluate"/>) - so <em>every</em>
    /// consumer of this class (the main viewport, but also anything else that ever calls
    /// it) always sees a node's mesh WITH its modifier stack already applied, never the
    /// raw <see cref="CoreNode.Mesh"/> a Mirror/Subdivision Surface modifier is meant to
    /// hide/replace for display purposes. <see cref="CoreNode.Mesh"/> itself is never
    /// touched by this - Edit Mode still edits exactly the base geometry.
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
        public static Model3DGroup Build(CoreScene scene, IDictionary<GeometryModel3D, CoreNode>? modelToNode = null, ShadingMode shadingMode = ShadingMode.Rendered, WeightPaintOverlay? weightPaintOverlay = null)
        {
            ArgumentNullException.ThrowIfNull(scene);

            var group = new Model3DGroup();
            foreach (var root in scene.RootNodes)
                group.Children.Add(Build(root, modelToNode, shadingMode, weightPaintOverlay));

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
        public static Model3DGroup Build(CoreNode node, IDictionary<GeometryModel3D, CoreNode>? modelToNode = null, ShadingMode shadingMode = ShadingMode.Rendered, WeightPaintOverlay? weightPaintOverlay = null)
        {
            ArgumentNullException.ThrowIfNull(node);

            var group = new Model3DGroup { Transform = ToTransform3D(node) };

            if (node.Mesh is { } mesh)
            {
                // The evaluated (post-modifier-stack) mesh is what actually gets built
                // into GPU geometry and shaded - node.Mesh itself, with an empty or
                // all-disabled Modifiers list, IS this value unchanged (see
                // ModifierStack.Evaluate's own remarks), so a node with no modifiers at
                // all costs nothing beyond the empty loop.
                var evaluatedMesh = ModifierStack.Evaluate(mesh, node.Modifiers, node);

                // Skeletal skinning is the LAST step before this mesh becomes GPU
                // geometry - after Modifiers, never before, so a Mirror/Solidify still
                // sees/produces plain rest-pose geometry (see SkinningEvaluator's own
                // remarks). A node with no SkinBinding at all - every mesh authored
                // before skinning existed - skips this entirely and renders exactly as
                // it always did.
                var skinnedMesh = node.SkinBinding is { } binding
                    ? SkinningEvaluator.Deform(evaluatedMesh, node.GetWorldTransform(), binding)
                    : evaluatedMesh;

                if (weightPaintOverlay is { } overlay && ReferenceEquals(overlay.Target, node))
                {
                    // Weight Paint mode's own heat-map, in place of this ONE node's
                    // normal material - see WeightPaintOverlay's own remarks. Reuses
                    // MeshGeometryFactory.Create exactly as-is (it already turns
                    // Vertex.UV into TextureCoordinates for ordinary texturing; a
                    // gradient Brush sampled through those same coordinates is no
                    // different from any other textured material as far as WPF's own
                    // renderer is concerned).
                    var heatmapMesh = WeightVisualization.BuildHeatmapMesh(skinnedMesh, overlay.ActiveBoneIndex);
                    var geometry = MeshGeometryFactory.Create(heatmapMesh);
                    var heatmapMaterial = new DiffuseMaterial(WeightPaintGradientBrush);
                    var heatmapModel = new GeometryModel3D(geometry, heatmapMaterial) { BackMaterial = heatmapMaterial };
                    group.Children.Add(heatmapModel);
                    if (modelToNode is not null) modelToNode[heatmapModel] = node;
                }
                else
                {
                    // Multi-Material Support: one GeometryModel3D per DISTINCT material the
                    // mesh's own polygons actually use (see MeshGeometryFactory.GroupTrianglesByMaterial's
                    // own remarks) - a plain single-material mesh (every mesh authored before
                    // this existed) always produces exactly one group here, so this is a
                    // strict generalization of the old "always exactly one model" behavior,
                    // not a change to it.
                    foreach (var (coreMaterial, triangles) in MeshGeometryFactory.GroupTrianglesByMaterial(skinnedMesh))
                    {
                        GeometryModel3D model;

                        if (VertexColorBakery.HasPaintedVertexColor(skinnedMesh, triangles))
                        {
                            // Vertex Paint: this group's own diffuse layer is a baked
                            // atlas multiplying its material's own texture (or flat
                            // white, if none) by every touched vertex's own Color - see
                            // VertexColorBakery/Materials.VertexColorBaker's own
                            // remarks. Every OTHER aspect of the material (specular,
                            // shading mode) still comes from the SAME MaterialFactory
                            // every untouched mesh already uses.
                            var (bakedTexture, remappedMesh) = VertexColorBakery.Bake(skinnedMesh, coreMaterial, triangles);
                            var bakedBrush = new ImageBrush(bakedTexture) { TileMode = TileMode.None, Stretch = Stretch.Fill };
                            bakedBrush.Freeze();

                            var bakedMaterial = MaterialFactory.Create(coreMaterial, shadingMode, bakedBrush);
                            var bakedGeometry = MeshGeometryFactory.Create(remappedMesh);
                            model = new GeometryModel3D(bakedGeometry, bakedMaterial) { BackMaterial = bakedMaterial };
                        }
                        else
                        {
                            var geometry = MeshGeometryFactory.CreateForTriangles(skinnedMesh, triangles);
                            var material = MaterialFactory.Create(coreMaterial, shadingMode);
                            model = new GeometryModel3D(geometry, material)
                            {
                                // Lets the same material shade the mesh from either side - a mesh
                                // authored with outward-only normals (Primitives.CreateCube included)
                                // would otherwise render invisible/black from behind its own faces,
                                // which reads as a bug to anyone orbiting the camera around it.
                                BackMaterial = material,
                            };
                        }

                        group.Children.Add(model);
                        if (modelToNode is not null) modelToNode[model] = node;
                    }
                }
            }

            foreach (var child in node.Children)
                group.Children.Add(Build(child, modelToNode, shadingMode, weightPaintOverlay));

            return group;
        }

        /// <summary>Blue (weight 0) to red (weight 1), sampled horizontally through
        /// <see cref="Core.Skinning.WeightVisualization.BuildHeatmapMesh"/>'s own
        /// UV.X remap - the task's own "Blue for 0 weight, transitioning to Red for
        /// 1.0 weight" ask. A single shared, frozen (immutable, thread-safe-to-reuse)
        /// brush instance - nothing about it ever changes, so there's no reason to
        /// rebuild it per node/per frame.</summary>
        private static readonly Brush WeightPaintGradientBrush = CreateWeightPaintGradientBrush();

        private static Brush CreateWeightPaintGradientBrush()
        {
            var brush = new LinearGradientBrush(Colors.Blue, Colors.Red, new System.Windows.Point(0, 0.5), new System.Windows.Point(1, 0.5));
            brush.Freeze();
            return brush;
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
