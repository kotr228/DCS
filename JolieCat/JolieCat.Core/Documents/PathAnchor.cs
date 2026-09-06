using SkiaSharp;

namespace JolieCat.Core.Documents
{
    /// <summary>Whether an anchor's two handles are kept symmetric as they're
    /// dragged (a smooth curve passes through) or move independently (a sharp
    /// corner, including a straight line when a handle is absent entirely) -
    /// mirrors every vector-path tool's own Pen tool convention.</summary>
    public enum AnchorPointType
    {
        Corner,
        Smooth,
    }

    /// <summary>
    /// One point on a <see cref="VectorPath"/>: its position, and the (optional)
    /// Bezier control handles for the curve segments entering and leaving it. A null
    /// handle on either side means that side's segment is a straight line rather
    /// than a curve - the common case for a Pen tool's plain corner-point click.
    /// </summary>
    public sealed class PathAnchor
    {
        public SKPoint Position { get; set; }

        /// <summary>Absolute document-space control point pulling the curve segment
        /// arriving at this anchor - null for a straight line in from the previous
        /// anchor.</summary>
        public SKPoint? InHandle { get; set; }

        /// <summary>Absolute document-space control point pulling the curve segment
        /// leaving this anchor - null for a straight line out to the next anchor.</summary>
        public SKPoint? OutHandle { get; set; }

        public AnchorPointType Type { get; set; } = AnchorPointType.Corner;

        public PathAnchor(SKPoint position)
        {
            Position = position;
        }

        /// <summary>Sets <see cref="OutHandle"/> to <paramref name="handle"/> and, if
        /// this anchor is <see cref="AnchorPointType.Smooth"/>, mirrors it into
        /// <see cref="InHandle"/> on the opposite side of <see cref="Position"/> - the
        /// click-and-drag gesture that defines a smooth point's own two handles as a
        /// single straight line through the anchor.</summary>
        public void SetOutHandleMirrored(SKPoint handle)
        {
            OutHandle = handle;
            if (Type == AnchorPointType.Smooth)
                InHandle = new SKPoint(2 * Position.X - handle.X, 2 * Position.Y - handle.Y);
        }

        /// <summary>The <see cref="InHandle"/> counterpart of <see cref="SetOutHandleMirrored"/> -
        /// sets <see cref="InHandle"/> to <paramref name="handle"/> and, if this anchor
        /// is <see cref="AnchorPointType.Smooth"/>, mirrors it into <see cref="OutHandle"/>.
        /// Needed because Direct Selection node-editing lets either handle be the one
        /// actually dragged, not just the out-handle a fresh Pen-tool click-drag always
        /// sets first.</summary>
        public void SetInHandleMirrored(SKPoint handle)
        {
            InHandle = handle;
            if (Type == AnchorPointType.Smooth)
                OutHandle = new SKPoint(2 * Position.X - handle.X, 2 * Position.Y - handle.Y);
        }
    }
}
