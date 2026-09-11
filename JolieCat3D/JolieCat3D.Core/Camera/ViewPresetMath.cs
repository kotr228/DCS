using System.Numerics;

namespace JolieCat3D.Core.Camera
{
    /// <summary>
    /// The world-space (look) direction and up vector for each <see cref="ViewPreset"/> -
    /// pure, WPF-free math (<c>JolieCat3D.Engine.Camera.CameraFraming</c> is the only
    /// consumer, converting these straight into a <c>ProjectionCamera</c>'s own
    /// <c>LookDirection</c>/<c>UpDirection</c>), so this project's own view-alignment
    /// CONVENTION is testable without a WPF runtime at all.
    ///
    /// This project's own convention (not a universal standard - every modeling tool
    /// picks its own signs/axes here, and this codebase already defines its own for
    /// "forward"/"up" elsewhere - see <c>Core.Scene.Node.GetWorldForward</c>'s own
    /// remarks): <see cref="Direction"/> always points FROM the named side TOWARD the
    /// scene's own center - a "Front" view camera sits in FRONT of the object (at +Z)
    /// looking back at it (along -Z), the same "which side are you standing on" reading
    /// every one of the 6 names has in ordinary modeling-tool usage. World Y is up for
    /// every preset except Top/Bottom (looking straight up/down, Y can't ALSO be the
    /// screen-space up vector without being parallel to the look direction itself - a
    /// degenerate, undefined orientation) - those two use world Z instead, chosen so Top
    /// and Bottom are consistent mirror images of one another (see this class's own
    /// scratch-verified property: <c>GetOrientation(Bottom).Up == -GetOrientation(Top).Up</c>).
    /// </summary>
    public static class ViewPresetMath
    {
        public static (Vector3 Direction, Vector3 Up) GetOrientation(ViewPreset preset) => preset switch
        {
            ViewPreset.Front => (new Vector3(0, 0, -1), Vector3.UnitY),
            ViewPreset.Back => (new Vector3(0, 0, 1), Vector3.UnitY),
            ViewPreset.Right => (new Vector3(-1, 0, 0), Vector3.UnitY),
            ViewPreset.Left => (new Vector3(1, 0, 0), Vector3.UnitY),
            ViewPreset.Top => (new Vector3(0, -1, 0), new Vector3(0, 0, 1)),
            ViewPreset.Bottom => (new Vector3(0, 1, 0), new Vector3(0, 0, -1)),
            _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null),
        };
    }
}
