using System.Numerics;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using CoreCameraProjectionMode = JolieCat3D.Core.Scene.CameraProjectionMode;
using CoreNode = JolieCat3D.Core.Scene.Node;

namespace JolieCat3D.Engine.Camera
{
    /// <summary>
    /// Pushes a <see cref="Core.Scene.CameraData"/>-carrying <see cref="CoreNode"/>'s own
    /// world position/orientation onto a <see cref="HelixViewport3D"/>'s actual rendering
    /// camera - the "Connect the active CameraNode to the actual Helix Toolkit viewport
    /// rendering camera" half of the task, called once per <c>Rendering.Scene3DRenderer.Render</c>
    /// (so moving, rotating, or KEYFRAMING the node - <c>Service.Animation.AnimationTrack</c>
    /// works on any <see cref="CoreNode"/>'s <see cref="CoreNode.LocalPosition"/>/
    /// <see cref="CoreNode.LocalRotation"/> regardless of whether it carries a
    /// <see cref="Core.Scene.CameraData"/>, a <see cref="Core.Geometry.Mesh"/>, both, or
    /// neither - updates the actual rendered view in real time, the same "re-render
    /// every frame from the current Core state" mechanism everything else in this
    /// project already uses).
    /// </summary>
    public static class SceneCameraSync
    {
        /// <summary>Replaces <paramref name="viewport"/>'s own <c>Camera</c> with a fresh
        /// <see cref="PerspectiveCamera"/>/<see cref="OrthographicCamera"/> (per
        /// <paramref name="cameraNode"/>'s own <see cref="Core.Scene.CameraData.ProjectionMode"/>)
        /// positioned/oriented at <paramref name="cameraNode"/>'s current world transform
        /// - <see cref="CoreNode.GetWorldPosition"/> for <c>Position</c>,
        /// <see cref="CoreNode.GetWorldForward"/> for <c>LookDirection</c>, and the
        /// node's own world-rotated local +Y axis for <c>UpDirection</c> (the standard
        /// "up" a camera banks around as it rotates, rather than a fixed world-up that
        /// would leave a rolled/banked camera's own horizon looking wrong). A no-op if
        /// <paramref name="cameraNode"/> has no <see cref="Core.Scene.CameraData"/> at
        /// all (nothing to sync).</summary>
        public static void Apply(HelixViewport3D viewport, CoreNode cameraNode)
        {
            ArgumentNullException.ThrowIfNull(viewport);
            ArgumentNullException.ThrowIfNull(cameraNode);

            if (cameraNode.Camera is not { } data) return;

            var worldPosition = cameraNode.GetWorldPosition();
            var forward = cameraNode.GetWorldForward();
            var worldRotation = cameraNode.GetWorldRotation();
            var up = Vector3.Transform(Vector3.UnitY, Matrix4x4.CreateFromQuaternion(worldRotation));

            var position = new Point3D(worldPosition.X, worldPosition.Y, worldPosition.Z);
            var lookDirection = new Vector3D(forward.X, forward.Y, forward.Z);
            var upDirection = new Vector3D(up.X, up.Y, up.Z);

            ProjectionCamera camera = data.ProjectionMode == CoreCameraProjectionMode.Orthographic
                ? new OrthographicCamera(position, lookDirection, upDirection, data.OrthographicWidth)
                : new PerspectiveCamera(position, lookDirection, upDirection, data.FieldOfView);

            camera.NearPlaneDistance = data.NearPlaneDistance;
            camera.FarPlaneDistance = data.FarPlaneDistance;

            viewport.Camera = camera;
        }
    }
}
