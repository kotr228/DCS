using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using JolieCat3D.Engine.Rendering;

namespace JolieCat3D.Engine.Gizmos
{
    /// <summary>
    /// Resolves a 2D viewport click to whichever gizmo handle - a <see cref="TranslateManipulator"/>
    /// Translate/Scale arrow, or a <see cref="RotateManipulator"/> Rotate ring (see
    /// <see cref="TransformGizmo.Handles"/>/<c>Editing.ComponentGizmo.Handles</c>) - it actually
    /// landed on, and starts that manipulator's own native drag - an explicit,
    /// occlusion-independent stand-in for WPF's own 3D hit-testing, which only ever routes a
    /// mouse-down to a <see cref="Manipulator"/> (a <see cref="UIElement3D"/>) when it comes
    /// out the NEAREST hit along the ray. An ordinary mesh has no input handling of its own,
    /// but its geometry still occludes the ray in depth - so a manipulator whose own hit-test
    /// geometry sits behind the mesh's silhouette from the camera's viewpoint can fail to
    /// receive the event at all, even though the handle visually appears to be in front of (or
    /// entirely clear of) the mesh on screen. That bubbles the click unhandled to the
    /// viewport's own mesh-selection handler instead, which is the actual root cause of
    /// "clicking a handle selects the mesh behind it" - a mesh occluding a manipulator that
    /// was never given a chance to compete for the hit at all, not a z-fighting or
    /// draw-order bug. Testing screen-space distance to each handle's own geometry directly,
    /// BEFORE mesh selection ever runs, can't be fooled by depth occlusion the way native
    /// hit-testing can, because it never asks WPF to resolve the click against the mesh at
    /// all - the same reasoning this class originally applied only to Translate's arrows now
    /// applies equally to Rotate's rings, which suffered the identical bug.
    /// </summary>
    public static class GizmoHitTester
    {
        public const double DefaultPixelThreshold = 14.0;

        /// <summary>How many straight segments a Rotate ring's own circle is approximated by
        /// for hit-testing - a 3D circle viewed from an oblique angle projects to an ellipse,
        /// not a circle, so unlike <see cref="TranslateManipulator"/>'s single straight-line
        /// segment, there's no simple closed-form screen-space distance to reach for; sampling
        /// the ring's own world-space centerline at this many points and taking the nearest of
        /// the resulting screen-space polyline's segments is the same "screen-space gates
        /// eligibility" approach <c>Editing.ComponentHitTester.HitTestEdge</c> already uses
        /// for a real mesh edge, just applied to a circle's own parametric points instead of
        /// two mesh vertices. High enough that consecutive samples are always well within one
        /// click's own pixel tolerance of each other at any sane zoom level, so the polyline
        /// reads as smoothly circular for hit-testing purposes.</summary>
        private const int RingSampleCount = 48;

        /// <summary>The <see cref="Manipulator"/> among <paramref name="handles"/> whose own
        /// visible geometry - a <see cref="TranslateManipulator"/>'s axis segment (from
        /// <see cref="Manipulator.Position"/> out to <see cref="TranslateManipulator.Direction"/> *
        /// <see cref="TranslateManipulator.Length"/>, the same extent the handle is actually
        /// drawn along), or a <see cref="RotateManipulator"/>'s ring (its centerline circle,
        /// radius (<see cref="RotateManipulator.Diameter"/> + <see cref="RotateManipulator.InnerDiameter"/>) / 4,
        /// in the plane perpendicular to <see cref="RotateManipulator.Axis"/> through
        /// <see cref="Manipulator.Position"/>) - passes within <paramref name="pixelThreshold"/>
        /// screen pixels of <paramref name="screenPosition"/>; the closest one, if more than
        /// one is that close, or null if none is. Any other <see cref="Manipulator"/> subtype
        /// is skipped (none currently exist in this project beyond these two).</summary>
        public static Manipulator? HitTest(HelixViewport3D viewport, IEnumerable<Manipulator> handles, Point screenPosition, double pixelThreshold = DefaultPixelThreshold)
        {
            ArgumentNullException.ThrowIfNull(viewport);
            ArgumentNullException.ThrowIfNull(handles);

            Manipulator? nearest = null;
            var nearestDistance = double.MaxValue;

            foreach (var handle in handles)
            {
                // A handle mid-teardown (see TransformGizmo/ComponentGizmo's own Rebuild
                // remarks) can briefly be detached from the viewport it's still tracked in -
                // skip it rather than let HelixToolkit.Wpf's own Point3DtoPoint2D throw trying
                // to resolve a Viewport3D that isn't there.
                if (handle.GetViewport3DOrNull() is null) continue;

                var distance = handle switch
                {
                    TranslateManipulator translate => DistanceToTranslateHandle(viewport, translate, screenPosition),
                    RotateManipulator rotate => DistanceToRotateHandle(viewport, rotate, screenPosition),
                    _ => (double?)null,
                };

                if (distance is not { } d || d > pixelThreshold || d >= nearestDistance) continue;

                nearestDistance = d;
                nearest = handle;
            }

            return nearest;
        }

        private static double DistanceToTranslateHandle(HelixViewport3D viewport, TranslateManipulator translate, Point screenPosition)
        {
            var origin = translate.Position;
            var tip = origin + translate.Direction * translate.Length;

            return ScreenDistanceToSegment(screenPosition, Project(viewport, origin), Project(viewport, tip));
        }

        private static double? DistanceToRotateHandle(HelixViewport3D viewport, RotateManipulator rotate, Point screenPosition)
        {
            var axis = rotate.Axis;
            if (axis.LengthSquared < 1e-9) return null;
            axis.Normalize();

            // Any world-axis vector not parallel to axis, projected out via cross product,
            // gives one of the ring plane's own two orthonormal basis vectors; crossing THAT
            // with axis gives the other - the standard "build a plane basis from a single
            // known normal" construction, picking whichever seed avoids the near-parallel
            // (numerically unstable) case.
            var seed = Math.Abs(axis.Z) < 0.9 ? new Vector3D(0, 0, 1) : new Vector3D(1, 0, 0);
            var u = Vector3D.CrossProduct(seed, axis);
            u.Normalize();
            var v = Vector3D.CrossProduct(axis, u);

            // The ring's own centerline sits halfway between its outer and inner diameter -
            // the actual drawn torus tube straddles this circle on both sides, but the
            // centerline is what a click aimed at "the ring" is really aimed at.
            var radius = (rotate.Diameter + rotate.InnerDiameter) / 4.0;
            var center = rotate.Position;

            Point? previous = null;
            var best = double.MaxValue;

            for (var i = 0; i <= RingSampleCount; i++)
            {
                var angle = 2 * Math.PI * i / RingSampleCount;
                var offset = u * (radius * Math.Cos(angle)) + v * (radius * Math.Sin(angle));
                var screenPoint = Project(viewport, center + offset);

                if (previous is { } prev)
                {
                    var segmentDistance = ScreenDistanceToSegment(screenPosition, prev, screenPoint);
                    if (segmentDistance < best) best = segmentDistance;
                }

                previous = screenPoint;
            }

            return best;
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
        /// confirmed to exist the same way for both <see cref="TranslateManipulator"/> and
        /// <see cref="RotateManipulator"/>), capturing the mouse itself - exactly as if the
        /// click had hit it natively. Reimplementing that drag-start logic from outside
        /// instead would have no way to reach the manipulator's own private fields, and would
        /// risk a wrong first-drag-frame delta.
        ///
        /// <see cref="UIElement3D.MouseDownEvent"/> and <see cref="Mouse.MouseDownEvent"/> are
        /// the SAME <see cref="RoutedEvent"/> instance (WPF's standard
        /// <c>RoutedEvent.AddOwner</c> pattern - <c>Mouse</c> registers the event once,
        /// <c>UIElement3D</c> just adds itself as an owner of it), so explicitly setting
        /// <see cref="RoutedEventArgs.RoutedEvent"/> to <see cref="Mouse.MouseDownEvent"/>
        /// reaches the exact same class-handler chain a native hit would have. The caller
        /// (<c>MainWindow.TryBeginGizmoDrag</c>) wraps this call in its own reentrancy guard -
        /// see its own remarks for why: this same RaiseEvent, left unguarded, is what produced
        /// an observed <see cref="StackOverflowException"/> (WPF's own class handling promotes
        /// the resulting unhandled <c>Mouse.MouseDownEvent</c> into a fresh
        /// <c>MouseLeftButtonDownEvent</c> regardless of whether the manipulator's own
        /// <c>OnMouseDown</c> already marked THIS event handled, and that promoted event
        /// bubbles right back out to the viewport's own click handler with the mouse still on
        /// the same handle) - nothing about that recursion is specific to Translate vs. Rotate,
        /// so the very same guard already covers a Rotate ring's drag too.</summary>
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
