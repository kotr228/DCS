using System.Numerics;
using System.Windows;
using JolieCat3D.Core.Camera;
using JolieCat3D.Core.Scene;
using CoreRay = JolieCat3D.Core.Geometry.Ray;

namespace JolieCat3D.Engine.Viewport
{
    /// <summary>
    /// Converts between screen pixels and the 3D world for one viewport. Everything that
    /// needs to pick or project (hit testers, paint/sculpt sessions, gizmos) talks to this
    /// interface instead of a concrete renderer.
    /// </summary>
    public interface IViewportProjector
    {
        /// <summary>World-space ray through a point given in viewport pixels (DIPs).</summary>
        CoreRay ScreenToRay(Point screenPosition);

        /// <summary>Viewport pixel position of a world point, or null when it is behind the camera.</summary>
        Point? WorldToScreen(Vector3 worldPosition);

        /// <summary>World-space length of one screen pixel at <paramref name="worldPosition"/>'s depth.</summary>
        float WorldUnitsPerPixel(Vector3 worldPosition);

        /// <summary>Current mouse position in viewport pixels.</summary>
        Point MousePosition { get; }
    }

    /// <summary>
    /// The editor's orbit camera (Blender-style turntable). Conventions match
    /// <see cref="Node"/>: the camera looks along its local +Z and its up is local +Y, so a
    /// scene camera node's world rotation can be used directly ("look through camera").
    /// Right-handed, Y-up, D3D depth range 0..1.
    /// </summary>
    public sealed class ViewportCamera
    {
        private const float MinDistance = 0.001f;

        public Vector3 Target { get; set; } = Vector3.Zero;

        public float Distance
        {
            get => _distance;
            set => _distance = MathF.Max(MinDistance, value);
        }
        private float _distance = 8f;

        public Quaternion Rotation { get; set; } = FromYawPitch(MathF.PI * 0.25f, -MathF.PI * 0.18f);

        public bool IsOrthographic { get; set; }

        /// <summary>Vertical field of view in degrees.</summary>
        public float FieldOfView { get; set; } = 45f;

        public float NearPlane { get; set; } = 0.01f;

        public float FarPlane { get; set; } = 10000f;

        /// <summary>When set (e.g. looking through a scene camera), the orthographic view
        /// height is fixed instead of being derived from <see cref="Distance"/>.</summary>
        public float? FixedOrthographicHeight { get; set; }

        public Vector3 Forward => Vector3.Normalize(Vector3.Transform(Vector3.UnitZ, Rotation));
        public Vector3 Up => Vector3.Normalize(Vector3.Transform(Vector3.UnitY, Rotation));
        public Vector3 Right => Vector3.Normalize(Vector3.Cross(Forward, Up));

        public Vector3 Position => Target - Forward * Distance;

        /// <summary>Height of the visible area at the target distance (used for ortho and panning).</summary>
        public float ViewHeightAtTarget =>
            IsOrthographic && FixedOrthographicHeight is { } fixedHeight
                ? fixedHeight
                : 2f * Distance * MathF.Tan(FieldOfView * MathF.PI / 360f);

        public Matrix4x4 GetView() => Matrix4x4.CreateLookAt(Position, Position + Forward, Up);

        public Matrix4x4 GetProjection(float aspect)
        {
            aspect = MathF.Max(aspect, 1e-4f);
            if (IsOrthographic)
            {
                var height = ViewHeightAtTarget;
                // Ortho near plane can sit behind the eye so nothing close gets clipped.
                return Matrix4x4.CreateOrthographic(height * aspect, height, -FarPlane * 0.5f, FarPlane);
            }

            return Matrix4x4.CreatePerspectiveFieldOfView(FieldOfView * MathF.PI / 180f, aspect, NearPlane, FarPlane);
        }

        public static Quaternion FromYawPitch(float yaw, float pitch)
        {
            // yaw around world Y, then pitch around the resulting local X. At yaw=0,pitch=0
            // the camera looks down -Z ("front" view, matching Blender's numpad 1 on a Y-up scene).
            var baseRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);
            var pitchQ = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -pitch);
            var yawQ = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
            return Quaternion.Normalize(Quaternion.Concatenate(Quaternion.Concatenate(pitchQ, baseRotation), yawQ));
        }

        /// <summary>Turntable orbit around <see cref="Target"/>. Angles in radians.</summary>
        public void Orbit(float yawDelta, float pitchDelta)
        {
            var pitchQ = Quaternion.CreateFromAxisAngle(Right, pitchDelta);
            var yawQ = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yawDelta);
            Rotation = Quaternion.Normalize(Quaternion.Concatenate(Quaternion.Concatenate(Rotation, pitchQ), yawQ));
        }

        /// <summary>Moves the view by a screen-space offset in pixels.</summary>
        public void Pan(float dxPixels, float dyPixels, float viewportHeightPixels)
        {
            var unitsPerPixel = ViewHeightAtTarget / MathF.Max(1f, viewportHeightPixels);
            Target += (-Right * dxPixels + Up * dyPixels) * unitsPerPixel;
        }

        /// <summary>Dolly towards the target (factor &lt; 1 zooms in). With a ray, zooms
        /// towards the point under the cursor like Blender's "zoom to mouse position".</summary>
        public void Zoom(float factor, CoreRay? towards = null)
        {
            factor = Math.Clamp(factor, 0.05f, 20f);
            if (IsOrthographic && FixedOrthographicHeight is { } fixedHeight)
            {
                FixedOrthographicHeight = fixedHeight * factor;
                return;
            }

            if (towards is { } ray)
            {
                // Point under the cursor on the plane through the target facing the camera.
                var denom = Vector3.Dot(ray.Direction, Forward);
                if (MathF.Abs(denom) > 1e-5f)
                {
                    var t = Vector3.Dot(Target - ray.Origin, Forward) / denom;
                    var focus = ray.Origin + ray.Direction * t;
                    Target = focus + (Target - focus) * factor;
                }
            }

            Distance *= factor;
        }

        /// <summary>Frames an axis-aligned box so it fills the view.</summary>
        public void Frame(Vector3 min, Vector3 max, float aspect = 1.5f)
        {
            if (min.X > max.X) return; // empty bounds
            var center = (min + max) * 0.5f;
            var radius = MathF.Max(0.05f, (max - min).Length() * 0.5f);
            var halfFov = FieldOfView * MathF.PI / 360f;
            var fitFov = aspect < 1f ? MathF.Atan(MathF.Tan(halfFov) * aspect) : halfFov;
            Target = center;
            Distance = radius / MathF.Sin(fitFov) * 1.05f;
            if (FixedOrthographicHeight is not null) FixedOrthographicHeight = radius * 2.1f;
        }

        /// <summary>Aligns to one of the standard views (Front/Top/...) keeping the target.</summary>
        public void AlignToPreset(ViewPreset preset)
        {
            var (direction, up) = ViewPresetMath.GetOrientation(preset);
            Rotation = LookRotation(direction, up);
        }

        /// <summary>Rotation whose +Z is <paramref name="forward"/> and +Y is as close to <paramref name="up"/> as possible.</summary>
        public static Quaternion LookRotation(Vector3 forward, Vector3 up)
        {
            forward = Vector3.Normalize(forward);
            var u = up - forward * Vector3.Dot(forward, up);
            if (u.LengthSquared() < 1e-10f)
            {
                u = Vector3.UnitY - forward * Vector3.Dot(forward, Vector3.UnitY);
                if (u.LengthSquared() < 1e-10f) u = Vector3.UnitZ - forward * Vector3.Dot(forward, Vector3.UnitZ);
            }
            u = Vector3.Normalize(u);
            var right = Vector3.Normalize(Vector3.Cross(u, forward));
            u = Vector3.Cross(forward, right);
            var basis = new Matrix4x4(
                right.X, right.Y, right.Z, 0,
                u.X, u.Y, u.Z, 0,
                forward.X, forward.Y, forward.Z, 0,
                0, 0, 0, 1);
            return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(basis));
        }

        /// <summary>Copies a scene camera node's world pose and lens into this camera.</summary>
        public void MatchSceneCamera(Node cameraNode)
        {
            if (cameraNode.Camera is not { } data) return;
            var position = cameraNode.GetWorldPosition();
            Rotation = Quaternion.Normalize(cameraNode.GetWorldRotation());
            Distance = MathF.Max(Distance, 1f);
            Target = position + Forward * Distance;
            FieldOfView = data.FieldOfView;
            NearPlane = MathF.Max(1e-4f, data.NearPlaneDistance);
            FarPlane = MathF.Max(NearPlane * 2f, data.FarPlaneDistance);
            IsOrthographic = data.ProjectionMode == CameraProjectionMode.Orthographic;
            FixedOrthographicHeight = IsOrthographic ? data.OrthographicWidth : null;
        }

        /// <summary>Writes this camera's pose back onto a scene camera node (camera piloting).</summary>
        public void WriteToSceneCamera(Node cameraNode)
        {
            var worldRotation = Rotation;
            var worldPosition = Position;
            if (cameraNode.Parent is { } parent)
            {
                if (!Matrix4x4.Invert(parent.GetWorldTransform(), out var parentWorldToLocal)) return;
                cameraNode.LocalPosition = Vector3.Transform(worldPosition, parentWorldToLocal);
                cameraNode.LocalRotation = Quaternion.Normalize(Quaternion.Inverse(parent.GetWorldRotation()) * worldRotation);
            }
            else
            {
                cameraNode.LocalPosition = worldPosition;
                cameraNode.LocalRotation = worldRotation;
            }

            if (IsOrthographic && FixedOrthographicHeight is { } height && cameraNode.Camera is { } data)
                data.OrthographicWidth = height;
        }

        public ViewportCamera Clone() => new()
        {
            Target = Target,
            Distance = Distance,
            Rotation = Rotation,
            IsOrthographic = IsOrthographic,
            FieldOfView = FieldOfView,
            NearPlane = NearPlane,
            FarPlane = FarPlane,
            FixedOrthographicHeight = FixedOrthographicHeight,
        };

        // ---------------- projection helpers ----------------

        public CoreRay ScreenToRay(Point screen, Size viewportSize)
        {
            var w = MathF.Max(1f, (float)viewportSize.Width);
            var h = MathF.Max(1f, (float)viewportSize.Height);
            var ndcX = (float)(2.0 * screen.X / w - 1.0);
            var ndcY = (float)(1.0 - 2.0 * screen.Y / h);
            var viewProj = GetView() * GetProjection(w / h);
            if (!Matrix4x4.Invert(viewProj, out var inverse)) return new CoreRay(Position, Forward);

            var near = Unproject(new Vector4(ndcX, ndcY, 0f, 1f), inverse);
            var far = Unproject(new Vector4(ndcX, ndcY, 1f, 1f), inverse);
            var direction = far - near;
            if (direction.LengthSquared() < 1e-20f) direction = Forward;
            return new CoreRay(near, Vector3.Normalize(direction));

            static Vector3 Unproject(Vector4 ndc, Matrix4x4 inv)
            {
                var v = Vector4.Transform(ndc, inv);
                return new Vector3(v.X, v.Y, v.Z) / v.W;
            }
        }

        public Point? WorldToScreen(Vector3 world, Size viewportSize)
        {
            var w = MathF.Max(1f, (float)viewportSize.Width);
            var h = MathF.Max(1f, (float)viewportSize.Height);
            var clip = Vector4.Transform(new Vector4(world, 1f), GetView() * GetProjection(w / h));
            if (clip.W <= 1e-6f) return null;
            var ndc = new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
            return new Point((ndc.X + 1) * 0.5 * w, (1 - ndc.Y) * 0.5 * h);
        }

        public float WorldUnitsPerPixel(Vector3 world, Size viewportSize)
        {
            var h = MathF.Max(1f, (float)viewportSize.Height);
            if (IsOrthographic) return ViewHeightAtTarget / h;
            var depth = MathF.Max(NearPlane, Vector3.Dot(world - Position, Forward));
            return 2f * depth * MathF.Tan(FieldOfView * MathF.PI / 360f) / h;
        }
    }
}
