using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using CorePreset = JolieCat3D.Core.Camera.ViewPreset;
using CorePresetMath = JolieCat3D.Core.Camera.ViewPresetMath;
using CoreNode = JolieCat3D.Core.Scene.Node;
using CoreScene = JolieCat3D.Core.Scene.Scene3D;
using CoreVector3 = System.Numerics.Vector3;
using MediaCamera = System.Windows.Media.Media3D.Camera;

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
        /// <see cref="CoreScene.GetBounds"/>) - see <see cref="ZoomToBounds"/>'s own
        /// remarks for the shared framing logic; this and the <see cref="CoreNode"/>
        /// overload just supply the two different sources of bounds
        /// <c>JolieCat3D.UI</c> ever wants to frame (the WHOLE scene, or one selected
        /// object).</summary>
        public static void ZoomToFit(HelixViewport3D viewport, CoreScene scene, double animationTimeMs = 0)
        {
            ArgumentNullException.ThrowIfNull(viewport);
            ArgumentNullException.ThrowIfNull(scene);

            var (min, max) = scene.GetBounds();
            ZoomToBounds(viewport, min, max, animationTimeMs);
        }

        /// <summary>Moves <paramref name="viewport"/>'s camera to frame just
        /// <paramref name="node"/>'s own world-space bounds (<see cref="CoreNode.GetWorldBounds"/> -
        /// its own mesh only, not its children's) - what a "View > Frame Selected"
        /// command, or <see cref="AlignToPreset"/>'s own <c>focusNode</c> argument, uses
        /// instead of framing the entire scene. A no-op (same as the whole-scene
        /// overload) for a node with no mesh at all, whose bounds collapse to a single
        /// point with nothing to meaningfully frame a distance around.</summary>
        public static void ZoomToFit(HelixViewport3D viewport, CoreNode node, double animationTimeMs = 0)
        {
            ArgumentNullException.ThrowIfNull(viewport);
            ArgumentNullException.ThrowIfNull(node);

            var (min, max) = node.GetWorldBounds();
            ZoomToBounds(viewport, min, max, animationTimeMs);
        }

        /// <summary>The actual "zoom to fit" shared by both <see cref="ZoomToFit(HelixViewport3D,CoreScene,double)"/>
        /// overloads, via <see cref="CameraHelper.ZoomExtents(ProjectionCamera,System.Windows.Controls.Viewport3D,Rect3D,double)"/> -
        /// the same "zoom to fit" <c>HelixToolkit.Wpf</c> already ships, not a hand-rolled
        /// bounding-sphere/field-of-view calculation, so it correctly frames EITHER a
        /// <see cref="PerspectiveCamera"/> or an <see cref="OrthographicCamera"/> (each
        /// needs a different distance/width calculation to fit the same bounds, which
        /// this delegates to entirely rather than reimplementing). A no-op if
        /// <paramref name="min"/> equals <paramref name="max"/> (nothing to frame at all -
        /// an empty scene, or a node with no mesh), or if the viewport's current camera
        /// isn't a <see cref="ProjectionCamera"/> (every camera <see cref="ConfigureOrbitPanZoom"/>,
        /// <see cref="SetPerspective"/>, and <see cref="SetOrthographic"/> all produce is,
        /// but a caller could in principle have assigned something else).</summary>
        private static void ZoomToBounds(HelixViewport3D viewport, CoreVector3 min, CoreVector3 max, double animationTimeMs = 0)
        {
            if (viewport.Camera is not ProjectionCamera camera) return;
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

        /// <summary>A reasonable, fixed starting <see cref="OrthographicCamera.Width"/> -
        /// <see cref="SetOrthographic"/>'s own remarks cover why this doesn't try to
        /// derive one from the camera's previous Perspective framing. Matches
        /// <c>Core.Scene.CameraData.OrthographicWidth</c>'s own default (10 world units)
        /// for the same "an unremarkable, sane starting value" reason that default
        /// exists.</summary>
        private const double DefaultOrthographicWidth = 10.0;

        /// <summary>Switches <paramref name="viewport"/>'s camera to
        /// <see cref="PerspectiveCamera"/>, preserving its current position/look
        /// direction/up vector exactly (only the PROJECTION changes - the vantage point
        /// does not) - a no-op if it's already a <see cref="PerspectiveCamera"/>. Default
        /// <see cref="PerspectiveCamera.FieldOfView"/>/near/far planes, matching this
        /// project's own established WPF camera defaults elsewhere (see
        /// <c>Core.Scene.CameraData.FieldOfView</c>'s own remarks on why 45 is
        /// unremarkable).</summary>
        public static void SetPerspective(HelixViewport3D viewport)
        {
            ArgumentNullException.ThrowIfNull(viewport);
            if (viewport.Camera is PerspectiveCamera) return;

            var (position, lookDirection, upDirection) = ReadOrientation(viewport.Camera);
            viewport.Camera = new PerspectiveCamera
            {
                Position = position,
                LookDirection = lookDirection,
                UpDirection = upDirection,
                FieldOfView = 45,
                NearPlaneDistance = 0.01,
                FarPlaneDistance = 10000,
            };
        }

        /// <summary>Switches <paramref name="viewport"/>'s camera to
        /// <see cref="OrthographicCamera"/>, preserving its current position/look
        /// direction/up vector exactly - a no-op if it's already an
        /// <see cref="OrthographicCamera"/>. <see cref="OrthographicCamera.Width"/> starts
        /// at a fixed, unremarkable <see cref="DefaultOrthographicWidth"/> rather than
        /// trying to derive one that exactly preserves the previous Perspective camera's
        /// own apparent framing - there is no single correct answer for "same apparent
        /// size" without knowing what the camera was actually looking AT (a distance to
        /// the nearest object, the scene's own bounds, ...), which this method has no way
        /// to know from the camera alone; a caller that also wants the result properly
        /// FRAMED should follow this with <see cref="ZoomToFit(HelixViewport3D,CoreScene,double)"/>
        /// (<see cref="AlignToPreset"/> already does exactly that).</summary>
        public static void SetOrthographic(HelixViewport3D viewport)
        {
            ArgumentNullException.ThrowIfNull(viewport);
            if (viewport.Camera is OrthographicCamera) return;

            var (position, lookDirection, upDirection) = ReadOrientation(viewport.Camera);
            viewport.Camera = new OrthographicCamera
            {
                Position = position,
                LookDirection = lookDirection,
                UpDirection = upDirection,
                Width = DefaultOrthographicWidth,
                NearPlaneDistance = 0.01,
                FarPlaneDistance = 10000,
            };
        }

        private static (Point3D Position, Vector3D LookDirection, Vector3D UpDirection) ReadOrientation(MediaCamera? camera) => camera switch
        {
            PerspectiveCamera perspective => (perspective.Position, perspective.LookDirection, perspective.UpDirection),
            OrthographicCamera orthographic => (orthographic.Position, orthographic.LookDirection, orthographic.UpDirection),
            // No ProjectionCamera at all yet (shouldn't normally happen - ConfigureOrbitPanZoom
            // never removes HelixViewport3D's own default camera) - a plain, sane starting
            // orientation rather than throwing.
            _ => (new Point3D(0, 0, 10), new Vector3D(0, 0, -1), new Vector3D(0, 1, 0)),
        };

        /// <summary>Rotates <paramref name="viewport"/>'s camera to look along
        /// <paramref name="preset"/>'s own world-axis-aligned direction (see
        /// <see cref="CorePresetMath.GetOrientation"/>'s own remarks on the exact
        /// convention), then re-frames it (<see cref="ZoomToFit(HelixViewport3D,CoreNode,double)"/>/
        /// <see cref="ZoomToFit(HelixViewport3D,CoreScene,double)"/>) around either
        /// <paramref name="focusNode"/>'s own bounds (when given - a mesh-bearing node
        /// currently selected, say) or <paramref name="scene"/>'s whole bounds otherwise -
        /// the standard "Top/Front/Side view" toolbar/menu command every modeling tool
        /// offers, landing the camera at the CORRECT preset-aligned vantage point (not
        /// merely rotated in place from wherever it happened to be) since re-framing
        /// reads the just-assigned look direction back out to decide where along it to
        /// actually position the camera. A no-op if the viewport's current camera isn't a
        /// <see cref="ProjectionCamera"/> at all (see <see cref="ZoomToBounds"/>'s own
        /// remarks).</summary>
        public static void AlignToPreset(HelixViewport3D viewport, CorePreset preset, CoreScene scene, CoreNode? focusNode = null)
        {
            ArgumentNullException.ThrowIfNull(viewport);
            ArgumentNullException.ThrowIfNull(scene);
            if (viewport.Camera is not ProjectionCamera camera) return;

            var (direction, up) = CorePresetMath.GetOrientation(preset);
            camera.LookDirection = new Vector3D(direction.X, direction.Y, direction.Z);
            camera.UpDirection = new Vector3D(up.X, up.Y, up.Z);

            if (focusNode is not null) ZoomToFit(viewport, focusNode);
            else ZoomToFit(viewport, scene);
        }
    }
}
