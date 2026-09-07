using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using CoreNode = JolieCat3D.Core.Scene.Node;

namespace JolieCat3D.Engine.Selection
{
    /// <summary>
    /// Builds the visual that marks the currently-selected object in the viewport: a
    /// wireframe box around its own world-space bounds (see <see cref="CoreNode.GetWorldBounds"/>),
    /// via <c>HelixToolkit.Wpf</c>'s own <see cref="BoundingBoxWireFrameVisual3D"/> rather
    /// than a hand-drawn set of line segments. Deliberately a separate <see cref="Visual3D"/>
    /// from the selected node's own <see cref="GeometryModel3D"/> (not, say, an emissive
    /// tint swapped onto its material) so selecting an object never has any risk of
    /// altering how it actually renders.
    /// </summary>
    public static class SelectionHighlightFactory
    {
        /// <summary>Null for a null <paramref name="node"/> (nothing selected) - the
        /// caller (<c>Rendering.Scene3DRenderer</c>) treats that as "remove the highlight
        /// visual, don't add a new one".</summary>
        public static Visual3D? CreateHighlight(CoreNode? node, Color color)
        {
            if (node is null) return null;

            var (min, max) = node.GetWorldBounds();

            // A degenerate (zero-volume) box - a node with no mesh, or a single-point one -
            // still gets a small visible box rather than nothing at all, so an empty pivot
            // node can still be seen as selected.
            const double minimumSize = 0.05;
            var size = new Vector3D(
                System.Math.Max(max.X - min.X, minimumSize),
                System.Math.Max(max.Y - min.Y, minimumSize),
                System.Math.Max(max.Z - min.Z, minimumSize));

            var center = new Point3D(
                (min.X + max.X) / 2.0,
                (min.Y + max.Y) / 2.0,
                (min.Z + max.Z) / 2.0);

            var bounds = new Rect3D(center - size / 2.0, new Size3D(size.X, size.Y, size.Z));

            return new BoundingBoxWireFrameVisual3D
            {
                BoundingBox = bounds,
                Color = color,
            };
        }
    }
}
