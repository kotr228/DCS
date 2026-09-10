namespace JolieCat3D.Service.Animation
{
    /// <summary>
    /// A standard cubic Bezier EASING curve - the same shape CSS's own
    /// <c>cubic-bezier(x1,y1,x2,y2)</c> timing function describes: a curve from (0,0)
    /// to (1,1) with two free control points (x1,y1)/(x2,y2), used to remap a raw
    /// [0,1] time fraction into an eased [0,1] progress fraction (NOT a general
    /// path-drawing Bezier curve - only ever this specific "easing function" shape,
    /// where the X axis is time and the Y axis is progress).
    /// </summary>
    public static class CubicBezierEasing
    {
        /// <summary>The CSS default <c>ease</c> timing function's own control points -
        /// what <see cref="InterpolationMode.Bezier"/> uses (a single, fixed, well-known
        /// "ease in and out" curve, rather than exposing 4 free control-point sliders
        /// per keyframe - a deliberate scope cut matching this project's own established
        /// pattern of shipping the common, useful case).</summary>
        public static (float X1, float Y1, float X2, float Y2) Ease => (0.25f, 0.1f, 0.25f, 1.0f);

        /// <summary>
        /// The eased progress fraction for a raw time fraction <paramref name="t"/>
        /// (clamped to [0,1] at the call site's own responsibility - values outside are
        /// clamped here too, for safety) - solves for the curve parameter whose X
        /// (time) component equals <paramref name="t"/> via bisection (the curve's own X
        /// component is monotonic for any valid easing curve, i.e. x1/x2 within [0,1],
        /// so bisection always converges), then returns that parameter's Y (progress)
        /// component.
        /// </summary>
        public static float Evaluate(float t, float x1, float y1, float x2, float y2)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;

            var low = 0f;
            var high = 1f;
            var mid = t;

            for (var i = 0; i < 24; i++)
            {
                mid = (low + high) / 2f;
                var x = CubicComponent(mid, x1, x2);
                if (MathF.Abs(x - t) < 0.0001f) break;
                if (x < t) low = mid; else high = mid;
            }

            return CubicComponent(mid, y1, y2);
        }

        /// <summary>The <see cref="Ease"/> preset's own eased value for <paramref name="t"/> -
        /// the overload <see cref="AnimationTrack.Evaluate"/> actually calls.</summary>
        public static float Evaluate(float t)
        {
            var (x1, y1, x2, y2) = Ease;
            return Evaluate(t, x1, y1, x2, y2);
        }

        /// <summary>One axis of a cubic Bezier with fixed endpoints P0=0, P3=1 -
        /// <c>3(1-t)^2*t*p1 + 3(1-t)t^2*p2 + t^3</c>.</summary>
        private static float CubicComponent(float t, float p1, float p2)
        {
            var oneMinusT = 1f - t;
            return 3f * oneMinusT * oneMinusT * t * p1 + 3f * oneMinusT * t * t * p2 + t * t * t;
        }
    }
}
