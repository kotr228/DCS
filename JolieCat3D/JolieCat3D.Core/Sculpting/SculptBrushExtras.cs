using System.Numerics;
using JolieCat3D.Core.Geometry;

namespace JolieCat3D.Core.Sculpting
{
    /// <summary>
    /// The rest of Blender's everyday sculpt brushes: Inflate, Flatten, Pinch, Crease and
    /// Clay. All work on position groups (every vertex entry at one position moves
    /// together), so UV seams and hard edges never tear open while sculpting.
    /// </summary>
    public static class SculptBrushExtras
    {
        /// <summary>A brush is applied once per mouse move, so each dab only does a small
        /// part of the job: <c>strength</c> (0..1, like Blender) scales these per-dab rates.</summary>
        private const float OffsetPerDab = 0.04f;   // fraction of the radius
        private const float BlendPerDab = 0.15f;    // fraction of the way to the target
        private sealed class Stroke
        {
            public required Mesh Mesh;
            public required Matrix4x4 World;
            public required Matrix4x4 ToLocal;
            public required List<(List<int> Members, Vector3 WorldPos, Vector3 WorldNormal, float Falloff)> Groups;
            public Vector3 AreaNormal;
            public Vector3 AreaCentre;
        }

        private static Stroke? Begin(Mesh mesh, Matrix4x4 world, Vector3 hit, float radius)
        {
            ArgumentNullException.ThrowIfNull(mesh);
            if (radius <= 0 || !Matrix4x4.Invert(world, out var toLocal)) return null;
            var groups = new Dictionary<(int, int, int), (List<int> Members, Vector3 Normal)>();
            for (var i = 0; i < mesh.Vertices.Count; i++)
            {
                var v = mesh.Vertices[i];
                var key = MeshOperators.Key(v.Position);
                if (!groups.TryGetValue(key, out var g)) groups[key] = g = (new List<int>(), Vector3.Zero);
                g.Members.Add(i);
                groups[key] = (g.Members, g.Normal + v.Normal);
            }

            var stroke = new Stroke { Mesh = mesh, World = world, ToLocal = toLocal, Groups = new() };
            float weightSum = 0;
            foreach (var (members, normalSum) in groups.Values)
            {
                var worldPos = Vector3.Transform(mesh.Vertices[members[0]].Position, world);
                var distance = Vector3.Distance(worldPos, hit);
                if (distance > radius) continue;
                var t = distance / radius;
                var falloff = 1 - t * t * (3 - 2 * t); // smooth falloff
                var n = Vector3.TransformNormal(normalSum, world);
                n = n.LengthSquared() > 1e-20f ? Vector3.Normalize(n) : Vector3.UnitY;
                stroke.Groups.Add((members, worldPos, n, falloff));
                stroke.AreaNormal += n * falloff;
                stroke.AreaCentre += worldPos * falloff;
                weightSum += falloff;
            }
            if (stroke.Groups.Count == 0 || weightSum <= 0 || stroke.AreaNormal.LengthSquared() < 1e-12f) return null;
            stroke.AreaNormal = Vector3.Normalize(stroke.AreaNormal);
            stroke.AreaCentre /= weightSum;
            return stroke;
        }

        private static void Move(Stroke stroke, Func<(Vector3 WorldPos, Vector3 WorldNormal, float Falloff), Vector3> newWorldPosition)
        {
            foreach (var g in stroke.Groups)
            {
                var local = Vector3.Transform(newWorldPosition((g.WorldPos, g.WorldNormal, g.Falloff)), stroke.ToLocal);
                foreach (var index in g.Members) stroke.Mesh.SetVertexPosition(index, local);
            }
            stroke.Mesh.RecalculateNormals();
        }

        /// <summary>Each point moves along its own normal (blows the surface up; invert shrinks).</summary>
        public static void ApplyInflate(Mesh mesh, Matrix4x4 world, Vector3 hit, float radius, float strength, bool invert)
        {
            if (Begin(mesh, world, hit, radius) is not { } s) return;
            var k = (invert ? -strength : strength) * radius * OffsetPerDab;
            Move(s, g => g.WorldPos + g.WorldNormal * (k * g.Falloff));
        }

        /// <summary>Pulls points onto the average plane under the brush (invert pushes away: Contrast).</summary>
        public static void ApplyFlatten(Mesh mesh, Matrix4x4 world, Vector3 hit, float radius, float strength, bool invert)
        {
            if (Begin(mesh, world, hit, radius) is not { } s) return;
            var amount = Math.Clamp(strength * BlendPerDab, 0f, 1f) * (invert ? -1 : 1);
            Move(s, g =>
            {
                var height = Vector3.Dot(g.WorldPos - s.AreaCentre, s.AreaNormal);
                return g.WorldPos - s.AreaNormal * (height * amount * g.Falloff);
            });
        }

        /// <summary>Draws points sideways towards the brush centre (sharpens ridges; invert spreads).</summary>
        public static void ApplyPinch(Mesh mesh, Matrix4x4 world, Vector3 hit, float radius, float strength, bool invert)
        {
            if (Begin(mesh, world, hit, radius) is not { } s) return;
            var amount = Math.Clamp(strength * BlendPerDab * 0.5f, 0f, 0.5f) * (invert ? -1 : 1);
            Move(s, g =>
            {
                var toCentre = hit - g.WorldPos;
                var tangential = toCentre - s.AreaNormal * Vector3.Dot(toCentre, s.AreaNormal);
                return g.WorldPos + tangential * (amount * g.Falloff);
            });
        }

        /// <summary>A sharp groove: draws inwards with a tight falloff and pinches the sides
        /// together (invert raises a sharp ridge).</summary>
        public static void ApplyCrease(Mesh mesh, Matrix4x4 world, Vector3 hit, float radius, float strength, bool invert)
        {
            if (Begin(mesh, world, hit, radius) is not { } s) return;
            var depth = (invert ? strength : -strength) * radius * OffsetPerDab;
            var pinch = Math.Clamp(strength * BlendPerDab * 0.3f, 0f, 0.3f);
            Move(s, g =>
            {
                var toCentre = hit - g.WorldPos;
                var tangential = toCentre - s.AreaNormal * Vector3.Dot(toCentre, s.AreaNormal);
                var sharp = g.Falloff * g.Falloff;
                return g.WorldPos + s.AreaNormal * (depth * sharp) + tangential * (pinch * g.Falloff);
            });
        }

        /// <summary>Builds material up to a plane just above the surface: fills hollows
        /// first and flattens the top, like Blender's Clay (invert carves down).</summary>
        public static void ApplyClay(Mesh mesh, Matrix4x4 world, Vector3 hit, float radius, float strength, bool invert)
        {
            if (Begin(mesh, world, hit, radius) is not { } s) return;
            var sign = invert ? -1f : 1f;
            var plane = s.AreaCentre + s.AreaNormal * (strength * radius * OffsetPerDab * 2f * sign);
            Move(s, g =>
            {
                var height = Vector3.Dot(g.WorldPos - plane, s.AreaNormal) * sign;
                if (height >= 0) return g.WorldPos; // already above the clay layer
                return g.WorldPos - s.AreaNormal * (height * sign * Math.Clamp(g.Falloff * BlendPerDab * 3f, 0, 1));
            });
        }
    }
}
