using System.Numerics;
using JolieCat3D.Core.Geometry;

namespace JolieCat3D.Core.Scene
{
    /// <summary>
    /// Makes a <see cref="Node"/> a curve/spline entity - present (non-null, on
    /// <see cref="Node.Curve"/>) only for a node authored as one, the same "optional
    /// data, not a subclass" shape <see cref="Node.Camera"/>/<see cref="Node.Light"/>
    /// already use. A cubic Bezier spline through <see cref="Points"/> (each with its
    /// own in/out tangent handle - see <see cref="CurvePoint"/>'s own remarks), the
    /// classic curve-authoring representation (Blender's own "Bezier" curve type).
    ///
    /// <see cref="Points"/>/<see cref="Closed"/>/<see cref="BevelDepth"/> are this
    /// node's own SOURCE OF TRUTH for what the curve looks like - <see cref="Node.Mesh"/>
    /// is just a CACHED tube mesh <see cref="GenerateMesh"/> produces from them, regenerated
    /// (by whichever caller just changed one of these - the Properties Inspector's own
    /// curve panel, currently) and reassigned to <see cref="Node.Mesh"/> exactly the way any
    /// other property edit already re-renders the viewport, going through the SAME
    /// Mesh-to-viewport pipeline every other node already uses (including
    /// <see cref="Node.Modifiers"/>, which apply on top of this generated tube exactly as
    /// they would any other mesh) - no separate rendering path needed for a curve at all.
    /// </summary>
    public sealed class CurveData
    {
        /// <summary>At least 2 for a meaningful curve - <see cref="GenerateMesh"/>
        /// tolerates fewer (an empty tube) rather than throwing, the same
        /// "degenerate input produces a degenerate, not broken, result" latitude
        /// <see cref="ArrayModifier"/>/<see cref="SolidifyModifier"/> already give.</summary>
        public List<CurvePoint> Points { get; } = new();

        /// <summary>Whether the spline loops back from the last point to the first
        /// (an added, implicit segment) rather than ending open.</summary>
        public bool Closed { get; set; }

        /// <summary>The generated tube's own cross-section radius - the task's own
        /// "Geometry: Bevel Depth" property. Clamped to a small positive minimum by
        /// <see cref="CurveMesher.GenerateTube"/> itself, not here, so a momentarily-zero
        /// value while typing in the Inspector never itself throws.</summary>
        public float BevelDepth { get; set; } = 0.05f;

        /// <summary>How many samples to take along EACH Bezier span between two
        /// consecutive <see cref="Points"/> - higher looks smoother at the cost of more
        /// geometry, the same "quality vs. density" tradeoff
        /// <see cref="SubdivisionSurfaceModifier.Iterations"/> already exposes for its
        /// own modifier. Clamped to [2, 64] - below 2 a span wouldn't even reach its own
        /// endpoint, above 64 a modest curve already exceeds what this project's
        /// fixed-function WPF viewport should reasonably rebuild every
        /// <c>Scene3DRenderer.Refresh</c> (the same ceiling reasoning
        /// <see cref="SubdivisionSurfaceModifier.Iterations"/>'s own remarks give).</summary>
        public int SegmentsPerSpan
        {
            get => _segmentsPerSpan;
            set => _segmentsPerSpan = Math.Clamp(value, 2, 64);
        }
        private int _segmentsPerSpan = 12;

        /// <summary>How many sides the generated tube's own circular cross-section has -
        /// clamped to [3, 64] for the same reasons as <see cref="SegmentsPerSpan"/>.</summary>
        public int RadialSegments
        {
            get => _radialSegments;
            set => _radialSegments = Math.Clamp(value, 3, 64);
        }
        private int _radialSegments = 8;

        /// <summary>Samples this curve's own cubic Bezier spline - each consecutive pair
        /// of <see cref="Points"/> forms one cubic Bezier span (P0 = the first point's own
        /// <see cref="CurvePoint.Position"/>, C1 = that SAME point's <see cref="CurvePoint.HandleOut"/>,
        /// C2 = the NEXT point's own <see cref="CurvePoint.HandleIn"/>, P1 = the next
        /// point's own <see cref="CurvePoint.Position"/> - the standard "each point's
        /// out-handle and the next point's in-handle are the span's own two control
        /// handles" Bezier-curve convention) - into a flat polyline <see cref="CurveMesher.GenerateTube"/>
        /// can sweep a cross-section along. A single point (or none) produces an empty
        /// list rather than throwing.</summary>
        public List<Vector3> SampleCenterline()
        {
            var result = new List<Vector3>();
            if (Points.Count == 0) return result;
            if (Points.Count == 1) { result.Add(Points[0].Position); return result; }

            var spanCount = Closed ? Points.Count : Points.Count - 1;
            for (var spanIndex = 0; spanIndex < spanCount; spanIndex++)
            {
                var start = Points[spanIndex];
                var end = Points[(spanIndex + 1) % Points.Count];

                // The FIRST sample of every span after the first is identical to the
                // previous span's own LAST sample (t=1 there is the same point as t=0
                // here) - skip it everywhere except this curve's very first sample, so
                // shared points aren't duplicated in the output polyline.
                var startT = spanIndex == 0 ? 0 : 1;
                for (var i = startT; i <= SegmentsPerSpan; i++)
                {
                    var t = (float)i / SegmentsPerSpan;
                    result.Add(EvaluateCubicBezier(start.Position, start.HandleOut, end.HandleIn, end.Position, t));
                }
            }

            return result;
        }

        private static Vector3 EvaluateCubicBezier(Vector3 p0, Vector3 c1, Vector3 c2, Vector3 p1, float t)
        {
            var u = 1f - t;
            return u * u * u * p0 + 3f * u * u * t * c1 + 3f * u * t * t * c2 + t * t * t * p1;
        }

        /// <summary>The actual tube/cable mesh this curve renders as - see this class's
        /// own remarks on how the result gets back onto <see cref="Node.Mesh"/>.</summary>
        public Mesh GenerateMesh() => CurveMesher.GenerateTube(SampleCenterline(), BevelDepth, RadialSegments, Closed);

        /// <summary>A complete, independent copy - every <see cref="CurvePoint"/> is
        /// itself cloned (see <see cref="CurvePoint.Clone"/>), so editing the clone's
        /// own points can never reach back into the original's. Used by
        /// <see cref="Node.Clone"/>.</summary>
        public CurveData Clone()
        {
            var clone = new CurveData
            {
                Closed = Closed,
                BevelDepth = BevelDepth,
                SegmentsPerSpan = SegmentsPerSpan,
                RadialSegments = RadialSegments,
            };
            foreach (var point in Points) clone.Points.Add(point.Clone());
            return clone;
        }
    }
}
