using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    public partial class CanvasViewModel
    {
        /// <summary>
        /// The selection's outline scaled (<paramref name="widthPercent"/>, <paramref name="heightPercent"/>)
        /// and turned (<paramref name="degrees"/>) around its middle, then moved by
        /// (<paramref name="dx"/>, <paramref name="dy"/>) pixels. Null without a selection.
        /// </summary>
        public SKPath? TransformedSelection(double widthPercent, double heightPercent, double degrees, double dx, double dy)
        {
            var selection = Layers.Scene.Selection;
            if (!selection.HasSelection || selection.Path is not { } path) return null;
            var bounds = path.Bounds;
            var matrix = SKMatrix.CreateScale((float)(widthPercent / 100), (float)(heightPercent / 100), bounds.MidX, bounds.MidY)
                .PostConcat(SKMatrix.CreateRotationDegrees((float)degrees, bounds.MidX, bounds.MidY))
                .PostConcat(SKMatrix.CreateTranslation((float)dx, (float)dy));
            var result = new SKPath(path);
            result.Transform(matrix);
            return result;
        }

        /// <summary>Select &gt; Transform Selection: shows the transformed outline live.</summary>
        public void PreviewTransformSelection(double widthPercent, double heightPercent, double degrees, double dx, double dy)
        {
            if (TransformedSelection(widthPercent, heightPercent, degrees, dx, dy) is not { } path) return;
            LiveSelectionPath = path;
            RaiseInvalidate();
        }

        /// <summary>Applies the transform to the selection's outline (the pixels stay put).</summary>
        public void TransformSelection(double widthPercent, double heightPercent, double degrees, double dx, double dy)
        {
            LiveSelectionPath = null;
            var selection = Layers.Scene.Selection;
            if (TransformedSelection(widthPercent, heightPercent, degrees, dx, dy) is not { } path) return;
            var feather = selection.Feather;
            selection.SetPath(path, Layers.DocumentWidth, Layers.DocumentHeight);
            selection.Feather = feather;
            RaiseInvalidate();
        }
    }
}
