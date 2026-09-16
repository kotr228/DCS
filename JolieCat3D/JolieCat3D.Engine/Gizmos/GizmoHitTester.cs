using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using JolieCat3D.Engine.Rendering;

namespace JolieCat3D.Engine.Gizmos
{
    /// <summary>
    /// Resolves a 2D viewport click to whichever <see cref="TranslateManipulator"/> Translate
    /// arrow (see <see cref="TransformGizmo.Handles"/>/<c>Editing.ComponentGizmo.Handles</c>)
    /// it actually landed on, and starts that manipulator's own native drag - an explicit,
    /// occlusion-independent stand-in for WPF's own 3D hit-testing, which only ever routes a
    /// mouse-down to a <see cref="Manipulator"/> (a <see cref="UIElement3D"/>) when it comes
    /// out the NEAREST hit along the ray. An ordinary mesh has no input handling of its own,
    /// but its geometry still occludes the ray in depth - so a manipulator whose own hit-test
    /// geometry sits behind the mesh's silhouette from the camera's viewpoint can fail to
    /// receive the event at all, even though the arrow visually appears to be in front of (or
    /// entirely clear of) the mesh on screen. That bubbles the click unhandled to the
    /// viewport's own mesh-selection handler instead, which is the actual root cause of
    /// "clicking an arrow selects the mesh behind it" - a mesh occluding a manipulator that
    /// was never given a chance to compete for the hit at all, not a z-fighting or
    /// draw-order bug. Testing screen-space distance to each arrow's own segment directly,
    /// BEFORE mesh selection ever runs, can't be fooled by depth occlusion the way native
    /// hit-testing can, because it never asks WPF to resolve the click against the mesh at
    /// all.
    ///
    /// Only <see cref="TranslateManipulator"/> (Translate mode's own handle, and Scale mode's
    /// reused one - see <c>TransformGizmo</c>'s own remarks) gets this treatment: it's what
    /// the bug report is actually about, and its handle is a simple line segment, cheap and
    /// unambiguous to hit-test directly. A <see cref="RotateManipulator"/>'s handle is a ring,
    /// not a segment - precise screen-space ring hit-testing is a materially different (and,
    /// with no way to actually run this on Windows to verify, riskier) problem than this bug
    /// report calls for, so Rotate mode is left on WPF's native hit-testing, unchanged.
    /// </summary>
    public static class GizmoHitTester
    {
        public const double DefaultPixelThreshold = 14.0;

        /// <summary>The <see cref="TranslateManipulator"/> among <paramref name="handles"/>
        /// whose own axis segment - from <see cref="Manipulator.Position"/> out to
        /// <see cref="TranslateManipulator.Direction"/> * <see cref="TranslateManipulator.Length"/>,
        /// the same extent the handle is actually drawn along - passes within
        /// <paramref name="pixelThreshold"/> screen pixels of <paramref name="screenPosition"/>;
        /// the closest one, if more than one is that close, or null if none is.</summary>
        public static TranslateManipulator? HitTest(HelixViewport3D viewport, IEnumerable<Manipulator> handles, Point screenPosition, double pixelThreshold = DefaultPixelThreshold)
        {
            ArgumentNullException.ThrowIfNull(viewport);
            ArgumentNullException.ThrowIfNull(handles);

            TranslateManipulator? nearest = null;
            var nearestDistance = double.MaxValue;

            foreach (var handle in handles)
            {
                if (handle is not TranslateManipulator translate) continue;
                // A handle mid-teardown (see TransformGizmo/ComponentGizmo's own Rebuild
                // remarks) can briefly be detached from the viewport it's still tracked
                // in - skip it rather than let HelixToolkit.Wpf's own Point3DtoPoint2D
                // throw trying to resolve a Viewport3D that isn't there.
                if (translate.GetViewport3DOrNull() is null) continue;

                var origin = translate.Position;
                var tip = origin + translate.Direction * translate.Length;

                var screenOrigin = Project(viewport, origin);
                var screenTip = Project(viewport, tip);

                var distance = ScreenDistanceToSegment(screenPosition, screenOrigin, screenTip);
                if (distance > pixelThreshold || distance >= nearestDistance) continue;

                nearestDistance = distance;
                nearest = translate;
            }

            return nearest;
        }

        /// <summary>Starts <paramref name="manipulator"/>'s own native drag as though WPF's
        /// hit-testing HAD routed <paramref name="sourceArgs"/> to it directly - re-raises a
        /// fresh <see cref="Mouse.MouseDownEvent"/> (the actual routed event
        /// <see cref="Manipulator"/>'s own protected <c>OnMouseDown</c> override is tied to;
        /// confirmed by inspecting the compiled HelixToolkit.Wpf/PresentationCore assemblies,
        /// since this project has no way to run the real control to verify at runtime)
        /// directly at <paramref name="manipulator"/> via <see cref="UIElement3D.RaiseEvent"/>,
        /// so the manipulator's own class handler runs its real <c>OnMouseDown</c> logic -
        /// initializing whatever private drag state it keeps (its own <c>lastPoint</c> field,
        /// confirmed to exist the same way), capturing the mouse itself - exactly as if the
        /// click had hit it natively. Reimplementing that drag-start logic from outside
        /// instead would have no way to reach the manipulator's own private fields, and would
        /// risk a wrong first-drag-frame delta.
        ///
        /// <see cref="UIElement3D.MouseDownEvent"/> and <see cref="Mouse.MouseDownEvent"/> are
        /// the SAME <see cref="RoutedEvent"/> instance (WPF's standard
        /// <c>RoutedEvent.AddOwner</c> pattern - <c>Mouse</c> registers the event once,
        /// <c>UIElement3D</c> just adds itself as an owner of it), so explicitly setting
        /// <see cref="RoutedEventArgs.RoutedEvent"/> to <see cref="Mouse.MouseDownEvent"/>
        /// reaches the exact same class-handler chain a native hit would have.</summary>
        public static void BeginDrag(Manipulator manipulator, MouseButtonEventArgs sourceArgs)
        {
            ArgumentNullException.ThrowIfNull(manipulator);
            ArgumentNullException.ThrowIfNull(sourceArgs);

            var args = new MouseButtonEventArgs(sourceArgs.MouseDevice, sourceArgs.Timestamp, sourceArgs.ChangedButton)
            {
                RoutedEvent = Mouse.MouseDownEvent,
            };
            manipulator.RaiseEvent(args);
        }

        private static Point Project(HelixViewport3D viewport, Point3D worldPosition) =>
            Viewport3DHelper.Point3DtoPoint2D(viewport.Viewport, worldPosition);

        /// <summary>Standard 2D point-to-segment distance, clamped to the segment's own
        /// extent - mirrors <c>Editing.ComponentHitTester</c>'s own private helper of the
        /// same shape exactly (a screen-space click tolerance, not a real 3D measurement).</summary>
        private static double ScreenDistanceToSegment(Point point, Point a, Point b)
        {
            var ab = b - a;
            var lengthSquared = ab.X * ab.X + ab.Y * ab.Y;
            if (lengthSquared < 1e-9) return (point - a).Length;

            var t = ((point.X - a.X) * ab.X + (point.Y - a.Y) * ab.Y) / lengthSquared;
            t = Math.Clamp(t, 0.0, 1.0);

            var closest = new Point(a.X + ab.X * t, a.Y + ab.Y * t);
            return (point - closest).Length;
        }
    }
}
