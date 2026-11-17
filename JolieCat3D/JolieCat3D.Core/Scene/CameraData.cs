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

        /// <summary>A complete, independent copy - every property is a plain value type,
        /// so there is nothing here two clones could ever share a reference to (unlike
        /// <see cref="Geometry.Mesh.Material"/>'s own deliberately-shared convention).
        /// Used by <see cref="Node.Clone"/>.</summary>
        /// <summary>Depth of field in the final render (Blender's Camera > Depth of Field).</summary>
        public bool DepthOfField { get; set; }

        /// <summary>Distance from the camera that is perfectly sharp (world units).</summary>
        public float FocusDistance
        {
            get => _focusDistance;
            set => _focusDistance = MathF.Max(0.01f, value);
        }
        private float _focusDistance = 5f;

        /// <summary>Lens f-number: smaller = shallower focus (more blur).</summary>
        public float FStop
        {
            get => _fStop;
            set => _fStop = Math.Clamp(value, 0.1f, 128f);
        }
        private float _fStop = 2.8f;

        /// <summary>Lens aperture radius in world units (metres), from a 24 mm-tall sensor
        /// and this camera's vertical field of view.</summary>
        public float ApertureRadius()
        {
            var focalMm = 12f / MathF.Tan(Math.Clamp(FieldOfView, 1f, 179f) * MathF.PI / 360f);
            return focalMm / FStop * 0.5f / 1000f;
        }

        public CameraData Clone() => new()
        {
            DepthOfField = DepthOfField,
            FocusDistance = FocusDistance,
            FStop = FStop,
            ProjectionMode = ProjectionMode,
            FieldOfView = FieldOfView,
            OrthographicWidth = OrthographicWidth,
            NearPlaneDistance = NearPlaneDistance,
            FarPlaneDistance = FarPlaneDistance,
        };
    }
}
