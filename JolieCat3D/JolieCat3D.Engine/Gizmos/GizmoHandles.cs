using System.Numerics;
using System.Windows;
using JolieCat3D.Core.Numerics;
using JolieCat3D.Engine.Viewport;
using CoreRay = JolieCat3D.Core.Geometry.Ray;

namespace JolieCat3D.Engine.Gizmos
{
    /// <summary>Which part of a gizmo the mouse is over / dragging.</summary>
    public enum GizmoPart
    {
        None,
        AxisX, AxisY, AxisZ,
        PlaneYZ, PlaneXZ, PlaneXY,   // plane handles, named by the plane they move in
        View,                        // centre: move in view plane / uniform scale / view-axis rotate
    }

    /// <summary>
    /// Shared geometry, drawing and screen-space hit testing for the transform and
    /// component gizmos. Sizes are in screen pixels so the gizmo looks the same at any zoom.
    /// </summary>
    public static class GizmoHandles
    {
        public const float AxisLengthPx = 95f;
        public const float PlaneOffsetPx = 26f;
        public const float PlaneSizePx = 14f;
        public const float RingRadiusPx = 80f;
        public const float ViewRingRadiusPx = 98f;
        public const float CenterRadiusPx = 11f;
        public const float HitTolerancePx = 8f;

        public static readonly Color4 XColor = new(0.88f, 0.31f, 0.35f);
        public static readonly Color4 YColor = new(0.45f, 0.78f, 0.30f);
        public static readonly Color4 ZColor = new(0.30f, 0.52f, 0.92f);
        public static readonly Color4 HoverColor = new(1f, 0.86f, 0.42f);
        public static readonly Color4 ViewColor = new(0.92f, 0.92f, 0.92f);

        public static Color4 ColorOf(int axis) => axis switch { 0 => XColor, 1 => YColor, _ => ZColor };

        public static GizmoPart AxisPart(int axis) => axis switch { 0 => GizmoPart.AxisX, 1 => GizmoPart.AxisY, _ => GizmoPart.AxisZ };

        public static GizmoPart PlanePart(int normalAxis) => normalAxis switch { 0 => GizmoPart.PlaneYZ, 1 => GizmoPart.PlaneXZ, _ => GizmoPart.PlaneXY };

        public static int AxisIndex(GizmoPart part) => part switch
        {
            GizmoPart.AxisX or GizmoPart.PlaneYZ => 0,
            GizmoPart.AxisY or GizmoPart.PlaneXZ => 1,
            GizmoPart.AxisZ or GizmoPart.PlaneXY => 2,
            _ => -1,
        };

        public static bool IsPlane(GizmoPart part) => part is GizmoPart.PlaneXY or GizmoPart.PlaneXZ or GizmoPart.PlaneYZ;

        public static bool IsAxis(GizmoPart part) => part is GizmoPart.AxisX or GizmoPart.AxisY or GizmoPart.AxisZ;

        /// <summary>Axis direction flipped so it points towards the viewer's side (keeps handles visible).</summary>
        public static Vector3 FacingAxis(Vector3 axis, Vector3 cameraForward) =>
            Vector3.Dot(axis, cameraForward) > 0.2f ? axis : axis;

        // ---------------- drawing ----------------

        public static void DrawTranslate(OverlayDrawList list, IViewportProjector p, Vector3 center, Vector3[] axes, GizmoPart hover, GizmoPart active, bool showPlanes = true)
        {
            var upp = p.WorldUnitsPerPixel(center);
            var length = AxisLengthPx * upp;
            for (var i = 0; i < 3; i++)
            {
                var part = AxisPart(i);
                if (active != GizmoPart.None && active != part && !(IsPlane(active) && AxisIndex(active) != i) && active != GizmoPart.View) continue;
                var color = hover == part || active == part ? HoverColor : ColorOf(i);
                var end = center + axes[i] * length;
                list.AddLine(center + axes[i] * (CenterRadiusPx * 1.3f * upp), end, color, 2.6f, OverlayLayer.OnTop);
                list.AddCone(end, end + axes[i] * (16f * upp), 5.5f * upp, color);
            }

            if (showPlanes && active == GizmoPart.None || IsPlane(active))
            {
                for (var n = 0; n < 3; n++)
                {
                    var part = PlanePart(n);
                    if (active != GizmoPart.None && active != part) continue;
                    var (u, v) = PlaneAxes(axes, n);
                    var c = center + (u + v) * (PlaneOffsetPx * upp);
                    var h = PlaneSizePx * 0.5f * upp;
                    var color = hover == part || active == part ? HoverColor : ColorOf(n);
                    var fill = new Color4(color.R, color.G, color.B, 0.55f);
                    list.AddTriangle(c - u * h - v * h, c + u * h - v * h, c + u * h + v * h, fill, OverlayLayer.OnTop);
                    list.AddTriangle(c - u * h - v * h, c + u * h + v * h, c - u * h + v * h, fill, OverlayLayer.OnTop);
                }
            }

            DrawCenter(list, p, center, hover == GizmoPart.View || active == GizmoPart.View);
        }

        public static void DrawScale(OverlayDrawList list, IViewportProjector p, Vector3 center, Vector3[] axes, GizmoPart hover, GizmoPart active)
        {
            var upp = p.WorldUnitsPerPixel(center);
            var length = AxisLengthPx * upp;
            for (var i = 0; i < 3; i++)
            {
                var part = AxisPart(i);
                if (active != GizmoPart.None && active != part && active != GizmoPart.View) continue;
                var color = hover == part || active == part ? HoverColor : ColorOf(i);
                var end = center + axes[i] * length;
                list.AddLine(center + axes[i] * (CenterRadiusPx * 1.3f * upp), end, color, 2.6f, OverlayLayer.OnTop);
                list.AddBox(end, axes[0], axes[1], axes[2], 5f * upp, color);
            }
            DrawCenter(list, p, center, hover == GizmoPart.View || active == GizmoPart.View);
        }

        public static void DrawRotate(OverlayDrawList list, IViewportProjector p, Vector3 center, Vector3[] axes, Vector3 cameraForward, GizmoPart hover, GizmoPart active)
        {
            var upp = p.WorldUnitsPerPixel(center);
            for (var i = 0; i < 3; i++)
            {
                var part = AxisPart(i);
                if (active != GizmoPart.None && active != part) continue;
                var color = hover == part || active == part ? HoverColor : ColorOf(i);
                list.AddCircle(center, axes[i], RingRadiusPx * upp, color, 2.6f, OverlayLayer.OnTop, 72);
            }
            if (active == GizmoPart.None || active == GizmoPart.View)
            {
                var color = hover == GizmoPart.View || active == GizmoPart.View ? HoverColor : ViewColor;
                list.AddCircle(center, cameraForward, ViewRingRadiusPx * upp, color, 1.8f, OverlayLayer.OnTop, 72);
            }
            list.AddPoint(center, ViewColor, 5f, OverlayLayer.OnTop);
        }

        private static void DrawCenter(OverlayDrawList list, IViewportProjector p, Vector3 center, bool highlighted)
        {
            var upp = p.WorldUnitsPerPixel(center);
            var forward = Vector3.Normalize(p.ScreenToRay(p.WorldToScreen(center) ?? new Point()).Direction);
            list.AddCircle(center, forward, CenterRadiusPx * upp, highlighted ? HoverColor : ViewColor, 1.6f, OverlayLayer.OnTop, 32);
        }

        public static (Vector3 U, Vector3 V) PlaneAxes(Vector3[] axes, int normalAxis) => normalAxis switch
        {
            0 => (axes[1], axes[2]),
            1 => (axes[0], axes[2]),
            _ => (axes[0], axes[1]),
        };

        // ---------------- hit testing (screen space) ----------------

        public static GizmoPart HitTranslateOrScale(IViewportProjector p, Vector3 center, Vector3[] axes, Point mouse, bool planes)
        {
            if (p.WorldToScreen(center) is not { } c) return GizmoPart.None;
            if ((mouse - c).Length <= CenterRadiusPx + 3) return GizmoPart.View;

            var upp = p.WorldUnitsPerPixel(center);
            var best = GizmoPart.None;
            double bestDistance = HitTolerancePx;

            if (planes)
            {
                for (var n = 0; n < 3; n++)
                {
                    var (u, v) = PlaneAxes(axes, n);
                    var pc = center + (u + v) * (PlaneOffsetPx * upp);
                    var h = PlaneSizePx * 0.5f * upp + 2 * upp;
                    var quad = new[] { pc - u * h - v * h, pc + u * h - v * h, pc + u * h + v * h, pc - u * h + v * h }
                        .Select(q => p.WorldToScreen(q)).ToArray();
                    if (quad.Any(q => q is null)) continue;
                    if (PointInQuad(mouse, quad.Select(q => q!.Value).ToArray())) return PlanePart(n);
                }
            }

            for (var i = 0; i < 3; i++)
            {
                var endWorld = center + axes[i] * ((AxisLengthPx + 16) * upp);
                if (p.WorldToScreen(endWorld) is not { } e) continue;
                var d = DistanceToSegment(mouse, c, e);
                if (d < bestDistance) { bestDistance = d; best = AxisPart(i); }
            }
            return best;
        }

        public static GizmoPart HitRotate(IViewportProjector p, Vector3 center, Vector3[] axes, Vector3 cameraForward, Point mouse)
        {
            var upp = p.WorldUnitsPerPixel(center);
            var best = GizmoPart.None;
            double bestDistance = HitTolerancePx;
            for (var i = 0; i < 3; i++)
            {
                var d = DistanceToCircle(p, center, axes[i], RingRadiusPx * upp, mouse);
                if (d < bestDistance) { bestDistance = d; best = AxisPart(i); }
            }
            var dv = DistanceToCircle(p, center, cameraForward, ViewRingRadiusPx * upp, mouse);
            if (dv < bestDistance) best = GizmoPart.View;
            return best;
        }

        private static double DistanceToCircle(IViewportProjector p, Vector3 center, Vector3 normal, float radius, Point mouse)
        {
            var (u, v) = OverlayDrawList.Basis(normal);
            var best = double.MaxValue;
            Point? previous = null;
            const int segments = 48;
            for (var i = 0; i <= segments; i++)
            {
                var a = i * MathF.Tau / segments;
                var s = p.WorldToScreen(center + (u * MathF.Cos(a) + v * MathF.Sin(a)) * radius);
                if (s is { } current && previous is { } prev) best = Math.Min(best, DistanceToSegment(mouse, prev, current));
                previous = s;
            }
            return best;
        }

        public static double DistanceToSegment(Point p, Point a, Point b)
        {
            var ab = b - a;
            var lengthSq = ab.X * ab.X + ab.Y * ab.Y;
            if (lengthSq < 1e-9) return (p - a).Length;
            var t = Math.Clamp(((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / lengthSq, 0, 1);
            return (p - (a + ab * t)).Length;
        }

        private static bool PointInQuad(Point p, Point[] q)
        {
            bool? sign = null;
            for (var i = 0; i < 4; i++)
            {
                var a = q[i];
                var b = q[(i + 1) % 4];
                var cross = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
                var s = cross >= 0;
                if (sign is null) sign = s;
                else if (sign != s) return false;
            }
            return true;
        }

        // ---------------- drag math ----------------

        /// <summary>Signed distance along <paramref name="axis"/> (through <paramref name="origin"/>) closest to the ray.</summary>
        public static float? AxisParameter(CoreRay ray, Vector3 origin, Vector3 axis)
        {
            var w0 = ray.Origin - origin;
            var a = Vector3.Dot(ray.Direction, ray.Direction);
            var b = Vector3.Dot(ray.Direction, axis);
            var c = Vector3.Dot(axis, axis);
            var d = Vector3.Dot(ray.Direction, w0);
            var e = Vector3.Dot(axis, w0);
            var denom = a * c - b * b;
            if (MathF.Abs(denom) < 1e-7f) return null; // looking straight down the axis
            return (a * e - b * d) / denom;
        }

        public static Vector3? IntersectPlane(CoreRay ray, Vector3 origin, Vector3 normal)
        {
            var denom = Vector3.Dot(ray.Direction, normal);
            if (MathF.Abs(denom) < 1e-6f) return null;
            var t = Vector3.Dot(origin - ray.Origin, normal) / denom;
            return ray.Origin + ray.Direction * t;
        }

        /// <summary>Screen-space angle (radians) of the mouse around the projected centre.</summary>
        public static double ScreenAngle(IViewportProjector p, Vector3 center, Point mouse)
        {
            var c = p.WorldToScreen(center) ?? mouse;
            return Math.Atan2(mouse.Y - c.Y, mouse.X - c.X);
        }
    }
}
