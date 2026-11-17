using System.Numerics;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.Core.Modifiers
{
    /// <summary>
    /// Weighted Normal (Blender): each vertex normal becomes the area-weighted average of
    /// the faces around it that lie within <see cref="AngleDegrees"/> of the vertex's own
    /// faces - big flat faces dominate, so bevelled hard-surface models shade cleanly
    /// (the standard trick for game props). Positions are unchanged.
    /// </summary>
    public sealed class WeightedNormalModifier : Modifier
    {
        public override string Name => "Weighted Normal";

        private float _angle = 30f;

        /// <summary>Faces bending more than this from a vertex's own face don't contribute (keeps hard edges).</summary>
        public float AngleDegrees
        {
            get => _angle;
            set => _angle = Math.Clamp(value, 0f, 180f);
        }

        public override Mesh Apply(Mesh input, Node? owner = null)
        {
            ArgumentNullException.ThrowIfNull(input);
            var output = input.Clone();
            var faces = input.GetRenderFaces().ToList();
            if (faces.Count == 0) return output;

            var faceNormal = new Vector3[faces.Count];
            var faceArea = new float[faces.Count];
            var cornerFaces = new Dictionary<(int, int, int), List<int>>();
            var vertexFaces = new List<int>[input.Vertices.Count];
            for (var i = 0; i < vertexFaces.Length; i++) vertexFaces[i] = new List<int>();
            for (var fi = 0; fi < faces.Count; fi++)
            {
                var f = faces[fi];
                var a = input.Vertices[f.A].Position;
                var cross = Vector3.Cross(input.Vertices[f.B].Position - a, input.Vertices[f.C].Position - a);
                faceArea[fi] = cross.Length() * 0.5f;
                faceNormal[fi] = faceArea[fi] > 1e-12f ? Vector3.Normalize(cross) : Vector3.Zero;
                foreach (var v in new[] { f.A, f.B, f.C })
                {
                    vertexFaces[v].Add(fi);
                    var key = MeshOperators.Key(input.Vertices[v].Position);
                    (cornerFaces.TryGetValue(key, out var l) ? l : cornerFaces[key] = new List<int>()).Add(fi);
                }
            }

            var cosLimit = MathF.Cos(AngleDegrees * MathF.PI / 180f);
            for (var v = 0; v < input.Vertices.Count; v++)
            {
                if (vertexFaces[v].Count == 0) continue;
                var own = Vector3.Zero;
                foreach (var fi in vertexFaces[v]) own += faceNormal[fi] * faceArea[fi];
                if (own.LengthSquared() < 1e-20f) continue;
                own = Vector3.Normalize(own);

                var sum = Vector3.Zero;
                foreach (var fi in cornerFaces[MeshOperators.Key(input.Vertices[v].Position)].Distinct())
                    if (Vector3.Dot(faceNormal[fi], own) >= cosLimit) sum += faceNormal[fi] * faceArea[fi];
                if (sum.LengthSquared() > 1e-20f) output.SetVertexNormal(v, Vector3.Normalize(sum));
            }
            return output;
        }

        public override Modifier Clone() => new WeightedNormalModifier { IsEnabled = IsEnabled, AngleDegrees = AngleDegrees };
    }
}
