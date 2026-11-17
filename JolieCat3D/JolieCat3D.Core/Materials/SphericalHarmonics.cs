using System.Numerics;

namespace JolieCat3D.Core.Materials
{
    /// <summary>
    /// Order-2 (9 coefficient) spherical-harmonic projection of an environment - the
    /// standard real-time stand-in for diffuse light from a whole sky (Ramamoorthi &amp;
    /// Hanrahan's irradiance environment maps).
    /// </summary>
    public static class SphericalHarmonics
    {
        /// <summary>Cosine-lobe convolution factors per band (A0, A1, A2).</summary>
        private static readonly float[] BandFactor = { MathF.PI, 2f * MathF.PI / 3f, MathF.PI / 4f };

        public static void Basis(Vector3 d, Span<float> y)
        {
            y[0] = 0.282095f;
            y[1] = 0.488603f * d.Y;
            y[2] = 0.488603f * d.Z;
            y[3] = 0.488603f * d.X;
            y[4] = 1.092548f * d.X * d.Y;
            y[5] = 1.092548f * d.Y * d.Z;
            y[6] = 0.315392f * (3f * d.Z * d.Z - 1f);
            y[7] = 1.092548f * d.X * d.Z;
            y[8] = 0.546274f * (d.X * d.X - d.Y * d.Y);
        }

        /// <summary>Projects six square D3D-ordered cube faces of linear radiance
        /// (row-major, <paramref name="size"/>²) onto the 9 SH coefficients.</summary>
        public static Vector3[] ProjectCube(IReadOnlyList<Vector3[]> faces, int size)
        {
            var sh = new Vector3[9];
            Span<float> y = stackalloc float[9];
            var totalWeight = 0.0;
            for (var f = 0; f < 6; f++)
            {
                var face = faces[f];
                for (var py = 0; py < size; py++)
                for (var px = 0; px < size; px++)
                {
                    var u = (px + 0.5f) / size;
                    var v = (py + 0.5f) / size;
                    var a = 2f * u - 1f;
                    var b = 2f * v - 1f;
                    // Solid angle of the texel (up to a constant normalised below).
                    var weight = 1f / MathF.Pow(1f + a * a + b * b, 1.5f);
                    var dir = HdrImage.CubeFaceDirection(f, u, v);
                    Basis(dir, y);
                    var c = face[py * size + px];
                    if (!float.IsFinite(c.X) || !float.IsFinite(c.Y) || !float.IsFinite(c.Z)) continue;
                    for (var i = 0; i < 9; i++) sh[i] += c * (y[i] * weight);
                    totalWeight += weight;
                }
            }
            if (totalWeight <= 0) return sh;
            var scale = (float)(4.0 * Math.PI / totalWeight);
            for (var i = 0; i < 9; i++) sh[i] *= scale;
            return sh;
        }

        /// <summary>Coefficients pre-multiplied by the cosine convolution, so that
        /// irradiance E(n) = Σ c'ᵢ·Yᵢ(n). A Lambert surface then reflects albedo·E/π.</summary>
        public static Vector3[] ToIrradiance(Vector3[] radianceSh)
        {
            var result = new Vector3[9];
            for (var i = 0; i < 9; i++) result[i] = radianceSh[i] * BandFactor[i == 0 ? 0 : i < 4 ? 1 : 2];
            return result;
        }

        /// <summary>Irradiance arriving at a surface with normal <paramref name="n"/>
        /// from coefficients made by <see cref="ToIrradiance"/>.</summary>
        public static Vector3 Irradiance(Vector3[] irradianceSh, Vector3 n)
        {
            Span<float> y = stackalloc float[9];
            Basis(n, y);
            var e = Vector3.Zero;
            for (var i = 0; i < 9; i++) e += irradianceSh[i] * y[i];
            return Vector3.Max(e, Vector3.Zero);
        }
    }
}
