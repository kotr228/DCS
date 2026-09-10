namespace JolieCat3D.Core.Scene
{
    /// <summary>
    /// Makes a <see cref="Node"/> a camera - present (non-null, on <see cref="Node.Camera"/>)
    /// only for a node meant to actually be a viewable/renderable camera, the same
    /// "optional data, not a subclass" shape <see cref="Node.Mesh"/> already uses for
    /// "this node is a mesh". A camera node's own position/orientation come from the
    /// SAME <see cref="Node.LocalPosition"/>/<see cref="Node.LocalRotation"/>
    /// (composed through its own parent chain via <see cref="Node.GetWorldTransform"/>,
    /// exactly like every other node) rather than a separate set of camera-specific
    /// transform fields - a camera node can be parented, transformed, and keyframed on
    /// an <c>Service.Animation.AnimationTimeline</c> exactly like a meshed node, with no
    /// special-casing anywhere in that pipeline at all.
    /// </summary>
    public sealed class CameraData
    {
        public CameraProjectionMode ProjectionMode { get; set; } = CameraProjectionMode.Perspective;

        /// <summary>Vertical field of view, in degrees - only meaningful when
        /// <see cref="ProjectionMode"/> is <see cref="CameraProjectionMode.Perspective"/>.
        /// 45 is a standard, unremarkable default (WPF's own <c>PerspectiveCamera.FieldOfView</c>
        /// likewise defaults to 45).</summary>
        public float FieldOfView { get; set; } = 45f;

        /// <summary>The width (in world units) of the view volume - only meaningful when
        /// <see cref="ProjectionMode"/> is <see cref="CameraProjectionMode.Orthographic"/>
        /// (WPF's own <c>OrthographicCamera.Width</c> - height follows from the
        /// viewport's own aspect ratio, not a separate field here).</summary>
        public float OrthographicWidth { get; set; } = 10f;

        public float NearPlaneDistance { get; set; } = 0.1f;

        public float FarPlaneDistance { get; set; } = 1000f;
    }
}
