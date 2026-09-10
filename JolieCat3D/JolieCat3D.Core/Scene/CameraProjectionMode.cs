namespace JolieCat3D.Core.Scene
{
    /// <summary>Which projection a <see cref="CameraData"/> renders with - see
    /// <see cref="CameraData.ProjectionMode"/>'s own remarks.</summary>
    public enum CameraProjectionMode
    {
        /// <summary>Objects farther from the camera appear smaller (the standard,
        /// realistic projection every game/3D viewport defaults to) - governed by
        /// <see cref="CameraData.FieldOfView"/>.</summary>
        Perspective,

        /// <summary>Parallel projection - object size on screen never changes with
        /// distance from the camera, the standard "blueprint"/CAD/isometric view -
        /// governed by <see cref="CameraData.OrthographicWidth"/> instead of a field of
        /// view (an orthographic camera has no meaningful FOV at all).</summary>
        Orthographic,
    }
}
