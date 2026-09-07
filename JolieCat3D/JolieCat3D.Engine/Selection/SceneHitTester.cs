using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using CoreNode = JolieCat3D.Core.Scene.Node;

namespace JolieCat3D.Engine.Selection
{
    /// <summary>
    /// Resolves a 2D click position in a <see cref="HelixViewport3D"/> back to the
    /// <see cref="CoreNode"/> whose mesh was clicked - the actual "click to select"
    /// mechanism. WPF's own 3D hit-testing (<see cref="VisualTreeHelper.HitTest(Visual,System.Windows.Media.HitTestFilterCallback,HitTestResultCallback,HitTestParameters)"/>
    /// against the viewport's inner <see cref="System.Windows.Controls.Viewport3D"/>)
    /// reports which <see cref="GeometryModel3D"/> was hit, not which <see cref="CoreNode"/>
    /// it came from - the reverse-lookup map <see cref="Geometry.SceneGraphBuilder.Build(Core.Scene.Scene3D,System.Collections.Generic.IDictionary{GeometryModel3D,CoreNode})"/>
    /// builds is what bridges that gap.
    /// </summary>
    public static class SceneHitTester
    {
        /// <summary>The nearest <see cref="CoreNode"/> whose mesh is hit by a ray through
        /// <paramref name="position"/> (in <paramref name="viewport"/>'s own coordinates,
        /// e.g. straight from a mouse event's <c>GetPosition(viewport)</c>) - null if
        /// nothing was hit, or if the hit <see cref="GeometryModel3D"/> isn't in
        /// <paramref name="modelToNode"/> (only possible if it came from something other
        /// than <see cref="Geometry.SceneGraphBuilder"/>, e.g. a gizmo handle or the
        /// selection outline, which deliberately aren't registered in that map so
        /// clicking them can't be misread as clicking the scene object underneath).</summary>
        public static CoreNode? HitTest(HelixViewport3D viewport, Point position, IReadOnlyDictionary<GeometryModel3D, CoreNode> modelToNode)
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

            return hitModel is not null && modelToNode.TryGetValue(hitModel, out var node) ? node : null;
        }
    }
}
