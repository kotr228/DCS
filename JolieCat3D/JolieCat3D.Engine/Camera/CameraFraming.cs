using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using CoreScene = JolieCat3D.Core.Scene.Scene3D;

namespace JolieCat3D.Engine.Camera
{
    /// <summary>
    /// Orbit/pan/zoom camera setup for a <see cref="HelixViewport3D"/> - the viewport
    /// control <c>HelixToolkit.Wpf</c> itself provides specifically to give a WPF 3D
    /// scene mouse-driven orbit/pan/zoom "out of the box" (its own <see cref="HelixViewport3D.CameraController"/>,
    /// wired up to <see cref="HelixViewport3D.IsRotationEnabled"/>/<see cref="HelixViewport3D.IsPanEnabled"/>/
    /// <see cref="HelixViewport3D.IsZoomEnabled"/> and default mouse gestures) rather than
    /// something this project hand-rolls on top of a plain <c>Viewport3D</c> - reimplementing
    /// that interaction model would only reproduce, less robustly, exactly what the
    /// library this task asked to integrate already does. What this class actually adds
    /// is the two things <see cref="HelixViewport3D"/> doesn't decide on its own:
    /// explicitly turning all three gestures on (its defaults already are, but a caller
    /// shouldn't have to know that), and framing a <c>JolieCat3D.Core</c> scene's own
    /// bounds in it.
    /// </summary>
    public static class CameraFraming
    {
        /// <summary>Turns on orbit (rotate), pan, and zoom, and sets a sensible default
        /// distance-based zoom sensitivity - the explicit "yes, all three gestures are
        /// on" this class exists to make visible rather than left to
        /// <see cref="HelixViewport3D"/>'s own defaults.</summary>
        public static void ConfigureOrbitPanZoom(HelixViewport3D viewport)
        {
            ArgumentNullException.ThrowIfNull(viewport);

            viewport.IsRotationEnabled = true;
            viewport.IsPanEnabled = true;
            viewport.IsZoomEnabled = true;
            viewport.ZoomAroundMouseDownPoint = true;

            // This project's own Core conventions (Primitives.CreatePlane's "ground" in
            // the XZ plane, LightingSettings' default light coming mostly from above) are
            // all Y-up - matching that here is what makes "orbit" read as rotating around
            // an upright object instead of a sideways-tumbling one.
            viewport.ModelUpDirection = new Vector3D(0, 1, 0);
        }

        /// <summary>Moves <paramref name="viewport"/>'s camera to frame
        /// <paramref name="scene"/>'s whole world-space bounding box (see
        /// <see cref="CoreScene.GetBounds"/>), via <see cref="CameraHelper.ZoomExtents(ProjectionCamera,System.Windows.Controls.Viewport3D,Rect3D,double)"/> -
        /// the same "zoom to fit" <c>HelixToolkit.Wpf</c> already ships, not a hand-rolled
        /// bounding-sphere/field-of-view calculation. A no-op if the scene has no
        /// geometry to frame, or if the viewport's current camera isn't a
        /// <see cref="ProjectionCamera"/> (every camera <see cref="ConfigureOrbitPanZoom"/>
        /// and <see cref="HelixViewport3D"/>'s own <see cref="HelixViewport3D.DefaultCamera"/>
        /// produce is, but a caller could in principle have assigned something else).</summary>
        public static void ZoomToFit(HelixViewport3D viewport, CoreScene scene, double animationTimeMs = 0)
        {
            ArgumentNullException.ThrowIfNull(viewport);
            ArgumentNullException.ThrowIfNull(scene);

            if (viewport.Camera is not ProjectionCamera camera) return;

            var (min, max) = scene.GetBounds();
            if (min == max) return;

            var bounds = new Rect3D(
                min.X, min.Y, min.Z,
                System.Math.Max(max.X - min.X, 0.001),
                System.Math.Max(max.Y - min.Y, 0.001),
                System.Math.Max(max.Z - min.Z, 0.001));

            // HelixViewport3D wraps a real System.Windows.Controls.Viewport3D rather than
            // being one itself (see its own .Viewport property) - CameraHelper.ZoomExtents
            // is an extension over that inner control, not the wrapper.
            CameraHelper.ZoomExtents(camera, viewport.Viewport, bounds, animationTimeMs);
        }
    }
}
