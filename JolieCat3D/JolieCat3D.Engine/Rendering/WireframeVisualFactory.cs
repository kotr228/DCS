using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using JolieCat3D.Core.Modifiers;
using CoreNode = JolieCat3D.Core.Scene.Node;
using CoreScene = JolieCat3D.Core.Scene.Scene3D;

namespace JolieCat3D.Engine.Rendering
{
    /// <summary>
    /// Builds <see cref="ShadingMode.Wireframe"/>'s own scene-wide edge overlay - every
    /// meshed node's own EVALUATED (its <see cref="CoreNode.Modifiers"/> stack applied -
    /// see <see cref="ModifierStack.Evaluate"/>, and <c>Geometry.SceneGraphBuilder</c>'s
    /// own remarks on why every consumer of a node's mesh does this) edges (via
    /// <see cref="Core.Geometry.Mesh.GetEdges"/>, transformed by that node's
    /// <see cref="CoreNode.GetWorldTransform"/>), all as one <see cref="LinesVisual3D"/>.
    /// The same building block <c>Editing.ComponentMarkerVisualFactory</c>'s own edge
    /// markers already use, just walking the WHOLE scene tree instead of one Edit Mode
    /// target - Wireframe shading shows a Mirror/Subdivision Surface modifier's actual
    /// effect too, the same as every other shading mode does.
    /// </summary>
    public static class WireframeVisualFactory
    {
        private static readonly Color WireframeColor = Color.FromRgb(0xC2, 0x9B, 0x58); // JolieCat AccentBrush (gold)

        /// <summary>Null for a scene with no meshed nodes at all (nothing to draw) -
        /// the caller (<see cref="Scene3DRenderer"/>) treats that as "no wireframe
        /// visual", the same convention <c>Selection.SelectionHighlightFactory.CreateHighlight</c>
        /// already uses for "nothing to highlight".</summary>
        public static Visual3D? CreateSceneWireframe(CoreScene scene)
        {
            ArgumentNullException.ThrowIfNull(scene);

            var points = new Point3DCollection();
            foreach (var root in scene.RootNodes)
                foreach (var node in root.Traverse())
                    CollectEdges(node, points);

            if (points.Count == 0) return null;

            return new LinesVisual3D { Points = points, Color = WireframeColor, Thickness = 1.0 };
        }

        private static void CollectEdges(CoreNode node, Point3DCollection points)
        {
            if (node.Mesh is not { } mesh) return;
            var evaluatedMesh = ModifierStack.Evaluate(mesh, node.Modifiers);
            var world = node.GetWorldTransform();

            foreach (var (a, b) in evaluatedMesh.GetEdges())
            {
                var worldA = Vector3.Transform(evaluatedMesh.Vertices[a].Position, world);
                var worldB = Vector3.Transform(evaluatedMesh.Vertices[b].Position, world);
                points.Add(new Point3D(worldA.X, worldA.Y, worldA.Z));
                points.Add(new Point3D(worldB.X, worldB.Y, worldB.Z));
            }
        }
    }
}
