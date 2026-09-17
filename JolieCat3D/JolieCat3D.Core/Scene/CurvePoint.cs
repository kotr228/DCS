using System.Numerics;

namespace JolieCat3D.Core.Scene
{
    /// <summary>
    /// One control point of a <see cref="CurveData"/>'s Bezier spline: the point itself
    /// (<see cref="Position"/>) plus its two tangent handles - the standard Bezier
    /// "anchor + in-handle + out-handle" authoring representation (Blender's own Bezier
    /// curve control point shape), letting a curve be edited by dragging either the
    /// point or either handle independently. Both handles are stored as ABSOLUTE
    /// LOCAL-SPACE positions (not offsets relative to <see cref="Position"/>) - the same
    /// "whatever a caller/gizmo would directly drag to" convention <see cref="Node.LocalPosition"/>
    /// itself already uses, so translating a handle in Edit Mode is just setting its own
    /// field to the gizmo's new world-to-local-converted position, no extra
    /// offset-from-anchor bookkeeping needed at the call site.
    /// </summary>
    public sealed class CurvePoint
    {
        public Vector3 Position { get; set; }

        /// <summary>The tangent handle on the "incoming" side (toward the previous
        /// point) - defaults to <see cref="Position"/> itself (a sharp corner/no curve
        /// influence) until moved.</summary>
        public Vector3 HandleIn { get; set; }

        /// <summary>The tangent handle on the "outgoing" side (toward the next point) -
        /// same default-at-<see cref="Position"/> convention as <see cref="HandleIn"/>.</summary>
        public Vector3 HandleOut { get; set; }

        public CurvePoint(Vector3 position)
        {
            Position = position;
            HandleIn = position;
            HandleOut = position;
        }

        public CurvePoint(Vector3 position, Vector3 handleIn, Vector3 handleOut)
        {
            Position = position;
            HandleIn = handleIn;
            HandleOut = handleOut;
        }

        public CurvePoint Clone() => new(Position, HandleIn, HandleOut);
    }
}
