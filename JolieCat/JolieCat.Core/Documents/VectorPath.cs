using System.Collections.Generic;
using SkiaSharp;

namespace JolieCat.Core.Documents
{
    /// <summary>
    /// An ordered chain of <see cref="PathAnchor"/> points forming one vector path -
    /// the Pen tool's own editable document model. Built up interactively (one anchor
    /// added per click, its handles set by a click-drag) and converted to a concrete
    /// <see cref="SKPath"/> - for on-canvas rendering, or as the source geometry for a
    /// pixel selection, a brush stroke along the outline, or a fill - via
    /// <see cref="ToSKPath"/>.
    /// </summary>
    public sealed class VectorPath
    {
        /// <summary>The path's anchors in drawing order - back-to-front along the path,
        /// not any spatial ordering.</summary>
        public List<PathAnchor> Anchors { get; } = new();

        /// <summary>Whether the last anchor connects back to the first, closing the
        /// path into a loop (needed for a meaningful fill, and for a selection that
        /// should enclose an area) rather than leaving it open (a stroke-only path,
        /// e.g. a squiggle with no interior).</summary>
        public bool IsClosed { get; set; }

        /// <summary>
        /// Builds the concrete Bezier geometry from <see cref="Anchors"/>: a straight
        /// <see cref="SKPath.LineTo(SKPoint)"/> between two anchors when neither offers
        /// a handle for that segment, or a full cubic <see cref="SKPath.CubicTo(SKPoint, SKPoint, SKPoint)"/>
        /// otherwise - using the outgoing anchor's <see cref="PathAnchor.OutHandle"/>
        /// and the incoming anchor's <see cref="PathAnchor.InHandle"/>, each falling
        /// back to its own anchor's <see cref="PathAnchor.Position"/> when absent. That
        /// fallback is what lets a curve degrade smoothly into a line from just one
        /// flat side of a segment (a corner point next to a smooth one) rather than
        /// requiring both ends to agree - matching every mainstream vector tool's own
        /// Pen tool behavior. Returns an empty, unopened path for zero anchors, and a
        /// bare <c>MoveTo</c> with no visible geometry for exactly one.
        /// </summary>
        public SKPath ToSKPath()
        {
            var path = new SKPath();
            if (Anchors.Count == 0)
                return path;

            path.MoveTo(Anchors[0].Position);

            for (var i = 1; i < Anchors.Count; i++)
                AppendSegment(path, Anchors[i - 1], Anchors[i]);

            if (IsClosed && Anchors.Count > 1)
                AppendSegment(path, Anchors[^1], Anchors[0]);

            if (IsClosed)
                path.Close();

            return path;
        }

        private static void AppendSegment(SKPath path, PathAnchor from, PathAnchor to)
        {
            if (from.OutHandle is null && to.InHandle is null)
            {
                path.LineTo(to.Position);
            }
            else
            {
                var control1 = from.OutHandle ?? from.Position;
                var control2 = to.InHandle ?? to.Position;
                path.CubicTo(control1, control2, to.Position);
            }
        }
    }
}
