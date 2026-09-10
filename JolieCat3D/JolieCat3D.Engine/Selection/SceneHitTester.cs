using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using JolieCat3D.Core.Geometry;
using CoreNode = JolieCat3D.Core.Scene.Node;
using CoreRay = JolieCat3D.Core.Geometry.Ray;
using CoreScene = JolieCat3D.Core.Scene.Scene3D;
using Vector3 = System.Numerics.Vector3;

namespace JolieCat3D.Engine.Selection
{
    /// <summary>
    /// Resolves a 2D click position in a <see cref="HelixViewport3D"/> back to the
    /// <see cref="CoreNode"/> whose mesh was clicked - the actual "click to select"
    /// mechanism. WPF's own 3D hit-testing (<see cref="VisualTreeHelper.HitTest(Visual,System.Windows.Media.HitTestFilterCallback,HitTestResultCallback,HitTestParameters)"/>
    /// against the viewport's inner <see cref="System.Windows.Controls.Viewport3D"/>)
    /// reports which <see cref="GeometryModel3D"/> was hit, not which <see cref="CoreNode"/>
    /// it came from - the reverse-lookup map <see cref="Geometry.SceneGraphBuilder.Build(Core.Scene.Scene3D,System.Collections.Generic.IDictionary{GeometryModel3D,CoreNode})"/>
    /// builds is what bridges that gap. This alone only ever finds a node with an actual
    /// <see cref="Core.Geometry.Mesh"/> (only those produce a <see cref="GeometryModel3D"/>
    /// at all) - a camera/light node has none, so <see cref="HitTest"/> ALSO falls back
    /// to a plain ray-vs-bounding-box test (see <see cref="RayIntersection.IntersectAABB"/>)
    /// against a small fixed-size box around every such node's own world position, the
    /// "Detect intersections with object bounding boxes... to select whole SceneObjects"
    /// half of the task's own wording, for exactly the objects a precise per-triangle
    /// mesh hit test structurally cannot ever reach.
    /// </summary>
    public static class SceneHitTester
    {
        /// <summary>Half the side length of the fixed picking box used for a non-meshed
        /// node (see this class's own remarks) - a camera/light node has no geometry of
        /// its own to derive a meaningful size from, so this is simply a small, constant,
        /// easy-to-click box around its world position (1 world unit across in total),
        /// the same role a "gizmo icon" plays in most editors without this project
        /// needing to actually render one.</summary>
        public const float PickingBoxHalfExtent = 0.5f;

        /// <summary>The nearest <see cref="CoreNode"/> whose mesh is hit by a ray through
        /// <paramref name="position"/> (in <paramref name="viewport"/>'s own coordinates,
        /// e.g. straight from a mouse event's <c>GetPosition(viewport)</c>) - null if
        /// nothing was hit, or if the hit <see cref="GeometryModel3D"/> isn't in
        /// <paramref name="modelToNode"/> (only possible if it came from something other
        /// than <see cref="Geometry.SceneGraphBuilder"/>, e.g. a gizmo handle or the
        /// selection outline, which deliberately aren't registered in that map so
        /// clicking them can't be misread as clicking the scene object underneath).
        /// A precise mesh hit always wins over the <paramref name="scene"/>-driven
        /// bounding-box fallback below when both would otherwise match (a camera/light
        /// icon box that happens to overlap a mesh in front of it should never steal a
        /// click meant for that mesh). <paramref name="scene"/> is optional (null skips
        /// the fallback entirely, matching this method's own original, mesh-only
        /// behavior) so any other caller of this overload set is unaffected.</summary>
        public static CoreNode? HitTest(HelixViewport3D viewport, Point position, IReadOnlyDictionary<GeometryModel3D, CoreNode> modelToNode, CoreScene? scene = null)
        {
            ArgumentNullException.ThrowIfNull(viewport);
            ArgumentNullException.ThrowIfNull(modelToNode);

            GeometryModel3D? hitModel = null;
            double nearestDistance = double.MaxValue;

            VisualTreeHelper.HitTest(
                viewport.Viewport,
                null,
                result =>
                {
                    // Prefer the closest hit along the ray - a click can land on more than
                    // one overlapping mesh (e.g. through a gap in the scene), and the
                    // frontmost one is the one a user expects "clicking on it" to mean.
                    if (result is RayMeshGeometry3DHitTestResult meshResult &&
                        meshResult.ModelHit is GeometryModel3D geometryModel &&
                        modelToNode.ContainsKey(geometryModel) &&
                        meshResult.DistanceToRayOrigin < nearestDistance)
                    {
                        nearestDistance = meshResult.DistanceToRayOrigin;
                        hitModel = geometryModel;
                    }

                    return HitTestResultBehavior.Continue;
                },
                new PointHitTestParameters(position));

            if (hitModel is not null && modelToNode.TryGetValue(hitModel, out var meshNode)) return meshNode;

            return scene is null ? null : HitTestNonMeshedNode(viewport, position, scene);
        }

        /// <summary>The ray-vs-bounding-box fallback for camera/light nodes - see this
        /// class's own remarks. Picks whichever candidate box the ray enters NEAREST its
        /// own origin (the camera), the same "closest wins" tie-break the mesh hit test
        /// above already applies via WPF's own <c>DistanceToRayOrigin</c>.</summary>
        private static CoreNode? HitTestNonMeshedNode(HelixViewport3D viewport, Point position, CoreScene scene)
        {
            var ray3D = Viewport3DHelper.Point2DtoRay3D(viewport.Viewport, position);
            var ray = new CoreRay(
                new Vector3((float)ray3D.Origin.X, (float)ray3D.Origin.Y, (float)ray3D.Origin.Z),
                new Vector3((float)ray3D.Direction.X, (float)ray3D.Direction.Y, (float)ray3D.Direction.Z));

            CoreNode? bestNode = null;
            var bestDistance = float.MaxValue;

            foreach (var node in scene.Traverse())
            {
                // A meshed node is always reachable through the precise per-triangle hit
                // test above - this fallback exists only for the nodes that AREN'T.
                if (node.Mesh is not null) continue;
                if (node.Camera is null && node.Light is null) continue;

                var worldPosition = node.GetWorldPosition();
                var half = new Vector3(PickingBoxHalfExtent);
                var min = worldPosition - half;
                var max = worldPosition + half;

                if (RayIntersection.IntersectAABB(ray, min, max) is not { } distance || distance >= bestDistance) continue;

                bestDistance = distance;
                bestNode = node;
            }

            return bestNode;
        }
    }
}
