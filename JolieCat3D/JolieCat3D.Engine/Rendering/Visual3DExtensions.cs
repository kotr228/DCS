using System.Windows.Controls;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace JolieCat3D.Engine.Rendering
{
    /// <summary>
    /// A null-safe equivalent of <c>HelixToolkit.Wpf.Visual3DHelper.GetViewport3D(Visual3D)</c> -
    /// that extension method ships as part of the compiled <c>HelixToolkit.Wpf</c> NuGet
    /// package (not this project's own code, so its source can't be edited directly) and
    /// throws <see cref="InvalidOperationException"/> ("The visual is not added to a
    /// Viewport3D.") for any <see cref="Visual3D"/> that isn't CURRENTLY attached to one,
    /// rather than returning null - a real, observed crash: HelixToolkit.Wpf's own
    /// <see cref="Manipulator"/>-derived controls (<see cref="TranslateManipulator"/>/
    /// <see cref="RotateManipulator"/>, the actual gizmo handles
    /// <c>Gizmos.TransformGizmo</c>/<c>Editing.ComponentGizmo</c> both use) resolve their
    /// own Viewport3D internally whenever a mouse event reaches them or one of their own
    /// dependency properties changes - and a manipulator CAN receive one of those a
    /// moment after this project's own code has removed it from
    /// <see cref="HelixViewport3D.Children"/> (see both gizmo classes' own <c>Rebuild</c>/
    /// <c>Refresh</c> remarks on the exact "mid-drag Rebuild" scenario this guards
    /// against).
    ///
    /// HelixToolkit.Wpf does already ship its own non-throwing check,
    /// <see cref="Visual3DHelper.IsAttachedToViewport3D"/> - this simply wraps
    /// <see cref="Visual3DHelper.GetViewport3D"/> behind it, rather than this project
    /// re-implementing the same parent-visual-tree walk a second time just to get a
    /// null instead of an exception out of it.
    /// </summary>
    public static class Visual3DExtensions
    {
        /// <summary>The <see cref="Viewport3D"/> <paramref name="visual"/> is currently
        /// attached to, or null (never throws) if it isn't attached to one at all right
        /// now.</summary>
        public static Viewport3D? GetViewport3DOrNull(this Visual3D visual)
        {
            ArgumentNullException.ThrowIfNull(visual);
            return Visual3DHelper.IsAttachedToViewport3D(visual) ? Visual3DHelper.GetViewport3D(visual) : null;
        }
    }
}
