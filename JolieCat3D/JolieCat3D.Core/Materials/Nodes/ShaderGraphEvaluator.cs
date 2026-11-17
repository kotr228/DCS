using System.Numerics;
using JolieCat3D.Core.Numerics;

namespace JolieCat3D.Core.Materials.Nodes
{
    /// <summary>
    /// Compiles a <see cref="ShaderGraph"/> into per-texel functions of the UV coordinate.
    /// Every socket value is a <see cref="Vector4"/>: colours are RGBA (display/sRGB
    /// values, like the colour pickers), floats are (v, v, v, 1), vectors are XYZ.
    /// Noise, Voronoi, Checker and Wave tile seamlessly across the 0..1 UV square when
    /// they read the plain UV with a whole-number scale - what game textures need.
    /// </summary>
    public sealed class ShaderGraphEvaluator
    {
        private readonly ShaderGraph _graph;
        private readonly Func<string, TextureBuffer?>? _imageLoader;
        private readonly Dictionary<string, TextureBuffer?> _images = new(StringComparer.OrdinalIgnoreCase);
        private readonly float _texel;

        public ShaderGraphEvaluator(ShaderGraph graph, Func<string, TextureBuffer?>? imageLoader = null, int resolution = 1024)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _imageLoader = imageLoader;
            _texel = 1f / Math.Max(1, resolution);
        }

        private static readonly Vector4 LumaWeights = new(0.2126f, 0.7152f, 0.0722f, 0);

        public static float ToFloat(Vector4 v) => Vector4.Dot(v, LumaWeights);
        private static Vector4 Fl(float v) => new(v, v, v, 1);

        /// <summary>The value an input receives: its link, its constant, or the UV.</summary>
        public Func<Vector2, Vector4> CompileInput(ShaderNode node, string input, int depth = 0)
        {
            if (depth > 256) return _ => Vector4.Zero; // defensive: cycles are refused by Connect
            if (_graph.LinkInto(node.Id, input) is { } link && _graph.Find(link.FromNode) is { } source)
                return CompileOutput(source, link.FromOutput, depth + 1);
            var def = ShaderNodeTypes.Get(node.Type).Inputs.FirstOrDefault(i => i.Name == input);
            if (def is { ImplicitUv: true } && !node.Inputs.ContainsKey(input)) return uv => new Vector4(uv, 0, 1);
            var constant = node.InputValue(input);
            return _ => constant;
        }

        /// <summary>True when the input is fed by a link.</summary>
        public bool IsLinked(ShaderNode node, string input) => _graph.LinkInto(node.Id, input) is not null;

        public Func<Vector2, Vector4> CompileOutput(ShaderNode node, string output, int depth = 0)
        {
            Func<Vector2, Vector4> In(string name) => CompileInput(node, name, depth);
            Func<Vector2, float> InF(string name)
            {
                var f = In(name);
                return uv => ToFloat(f(uv));
            }
            bool PlainUv(string name) => !IsLinked(node, name) && !node.Inputs.ContainsKey(name);

            switch (node.Type)
            {
                case ShaderNodeTypes.TexCoord:
                    return uv => new Vector4(uv, 0, 1);
                case ShaderNodeTypes.Value:
                {
                    var v = InF("Value");
                    return uv => Fl(v(uv));
                }
                case ShaderNodeTypes.Rgb:
                    return In("Color");
                case ShaderNodeTypes.Mapping:
                {
                    var vec = In("Vector"); var loc = In("Location"); var rot = InF("Rotation"); var scale = In("Scale");
                    return uv =>
                    {
                        var v = vec(uv) * scale(uv);
                        var a = rot(uv) * MathF.PI / 180f;
                        var (s, c) = MathF.SinCos(a);
                        var l = loc(uv);
                        return new Vector4(v.X * c - v.Y * s + l.X, v.X * s + v.Y * c + l.Y, v.Z + l.Z, 1);
                    };
                }
                case ShaderNodeTypes.Image:
                {
                    var vec = In("Vector");
                    var image = LoadImage(node.Option("Image"));
                    if (image is null) return _ => output == "Alpha" ? Fl(1) : new Vector4(1, 0, 1, 1); // missing image: magenta, like Blender
                    return uv =>
                    {
                        var c = SampleImage(image, vec(uv));
                        return output == "Alpha" ? Fl(c.W) : c;
                    };
                }
                case ShaderNodeTypes.Noise:
                {
                    var vec = In("Vector"); var scale = InF("Scale"); var detail = InF("Detail"); var rough = InF("Roughness"); var distortion = InF("Distortion");
                    var seamless = node.Option("Seamless") == "Yes" && PlainUv("Vector");
                    return uv =>
                    {
                        var s = scale(uv);
                        var period = seamless ? Math.Max(1, (int)MathF.Round(s)) : 0;
                        if (seamless) s = period;
                        var p = vec(uv);
                        var q = new Vector3(p.X, p.Y, p.Z) * s;
                        var d = distortion(uv);
                        if (d != 0)
                            q += new Vector3(Noise.Fbm(q + new Vector3(0, 0, 101.3f), 1, 0.5f, period) - 0.5f,
                                             Noise.Fbm(q + new Vector3(0, 0, 203.7f), 1, 0.5f, period) - 0.5f, 0) * d;
                        var det = detail(uv); var r = rough(uv);
                        if (output == "Fac") return Fl(Noise.Fbm(q, det, r, period));
                        return new Vector4(Noise.Fbm(q, det, r, period), Noise.Fbm(q + new Vector3(0, 0, 31.4f), det, r, period), Noise.Fbm(q + new Vector3(0, 0, 57.9f), det, r, period), 1);
                    };
                }
                case ShaderNodeTypes.Voronoi:
                {
                    var vec = In("Vector"); var scale = InF("Scale"); var random = InF("Randomness");
                    var seamless = node.Option("Seamless") == "Yes" && PlainUv("Vector");
                    var edge = node.Option("Feature") == "Distance to Edge";
                    return uv =>
                    {
                        var s = scale(uv);
                        var period = seamless ? Math.Max(1, (int)MathF.Round(s)) : 0;
                        if (seamless) s = period;
                        var p = vec(uv);
                        var (distance, cell) = Noise.Voronoi(new Vector2(p.X, p.Y) * s, Math.Clamp(random(uv), 0, 1), period, edge);
                        return output == "Distance" ? Fl(distance) : new Vector4(Noise.HashColor(cell), 1);
                    };
                }
                case ShaderNodeTypes.Checker:
                {
                    var vec = In("Vector"); var c1 = In("Color1"); var c2 = In("Color2"); var scale = InF("Scale");
                    return uv =>
                    {
                        var p = vec(uv) * scale(uv);
                        // Tiny offset so texel centres on cell borders don't flicker.
                        var odd = ((int)MathF.Floor(p.X + 1e-5f) + (int)MathF.Floor(p.Y + 1e-5f) + (int)MathF.Floor(p.Z + 1e-5f)) & 1;
                        if (output == "Fac") return Fl(odd == 0 ? 1 : 0);
                        return odd == 0 ? c1(uv) : c2(uv);
                    };
                }
                case ShaderNodeTypes.Gradient:
                {
                    var vec = In("Vector");
                    var type = node.Option("Type");
                    return uv =>
                    {
                        var p = vec(uv);
                        var f = type switch
                        {
                            "Quadratic" => MathF.Pow(MathF.Max(p.X, 0), 2),
                            "Diagonal" => (p.X + p.Y) * 0.5f,
                            "Radial" => MathF.Atan2(p.Y, p.X) / (2 * MathF.PI) + 0.5f,
                            "Spherical" => MathF.Max(1 - new Vector3(p.X, p.Y, p.Z).Length(), 0),
                            _ => p.X,
                        };
                        f = Math.Clamp(f, 0, 1);
                        return Fl(f);
                    };
                }
                case ShaderNodeTypes.Brick:
                {
                    var vec = In("Vector"); var c1 = In("Color1"); var c2 = In("Color2"); var mortar = In("Mortar");
                    var scale = InF("Scale"); var mortarSize = InF("Mortar Size"); var offset = InF("Offset"); var width = InF("Brick Width"); var height = InF("Row Height");
                    return uv =>
                    {
                        var p = vec(uv) * scale(uv);
                        var bw = MathF.Max(width(uv), 1e-3f); var rh = MathF.Max(height(uv), 1e-3f);
                        var row = (int)MathF.Floor(p.Y / rh);
                        var x = p.X + ((row & 1) != 0 ? offset(uv) * bw : 0);
                        var brick = (int)MathF.Floor(x / bw);
                        var xx = x - brick * bw;
                        var yy = p.Y - row * rh;
                        var m = mortarSize(uv);
                        var inMortar = MathF.Min(MathF.Min(xx, bw - xx), MathF.Min(yy, rh - yy)) < m;
                        if (output == "Fac") return Fl(inMortar ? 1 : 0);
                        if (inMortar) return mortar(uv);
                        var tint = Noise.Hash01(brick, row, 7);
                        return Vector4.Lerp(c1(uv), c2(uv), tint);
                    };
                }
                case ShaderNodeTypes.Wave:
                {
                    var vec = In("Vector"); var scale = InF("Scale"); var distortion = InF("Distortion"); var detail = InF("Detail");
                    var rings = node.Option("Type") == "Rings";
                    var saw = node.Option("Profile") == "Saw";
                    return uv =>
                    {
                        var p = vec(uv);
                        var s = scale(uv);
                        var n = rings ? new Vector3(p.X - 0.5f, p.Y - 0.5f, p.Z).Length() * s : p.X * s;
                        var d = distortion(uv);
                        if (d != 0) n += d * (Noise.Fbm(new Vector3(p.X, p.Y, p.Z) * MathF.Max(1, MathF.Round(s)), detail(uv), 0.5f, Math.Max(1, (int)MathF.Round(s))) * 2 - 1);
                        var f = saw ? n - MathF.Floor(n) : 0.5f - 0.5f * MathF.Cos(n * 2 * MathF.PI);
                        return Fl(f);
                    };
                }
                case ShaderNodeTypes.ColorRamp:
                {
                    var fac = InF("Fac");
                    var stops = (node.Ramp ?? new List<RampStop>()).OrderBy(s => s.Position).ToArray();
                    var mode = node.Option("Interpolation");
                    return uv =>
                    {
                        var c = EvaluateRamp(stops, fac(uv), mode);
                        return output == "Alpha" ? Fl(c.W) : c;
                    };
                }
                case ShaderNodeTypes.Mix:
                {
                    var fac = InF("Fac"); var a = In("A"); var b = In("B");
                    var mode = node.Option("Blend");
                    var clamp = node.Option("Clamp") == "Yes";
                    return uv =>
                    {
                        var ca = a(uv); var cb = b(uv);
                        var t = Math.Clamp(fac(uv), 0, 1);
                        var blended = Blend(mode, ca, cb);
                        var r = Vector4.Lerp(ca, blended, t);
                        r.W = ca.W;
                        return clamp ? Vector4.Clamp(r, Vector4.Zero, Vector4.One) : r;
                    };
                }
                case ShaderNodeTypes.Math:
                {
                    var a = InF("A"); var b = InF("B");
                    var op = node.Option("Operation");
                    var clamp = node.Option("Clamp") == "Yes";
                    return uv =>
                    {
                        var r = MathOp(op, a(uv), b(uv));
                        if (!float.IsFinite(r)) r = 0;
                        return Fl(clamp ? Math.Clamp(r, 0, 1) : r);
                    };
                }
                case ShaderNodeTypes.Invert:
                {
                    var fac = InF("Fac"); var color = In("Color");
                    return uv =>
                    {
                        var c = color(uv);
                        var inv = new Vector4(1 - c.X, 1 - c.Y, 1 - c.Z, c.W);
                        return Vector4.Lerp(c, inv, Math.Clamp(fac(uv), 0, 1)) with { W = c.W };
                    };
                }
                case ShaderNodeTypes.HueSat:
                {
                    var hue = InF("Hue"); var sat = InF("Saturation"); var val = InF("Value"); var fac = InF("Fac"); var color = In("Color");
                    return uv =>
                    {
                        var c = color(uv);
                        var (h, s, v) = RgbToHsv(new Vector3(c.X, c.Y, c.Z));
                        h = (h + hue(uv) - 0.5f) % 1f; if (h < 0) h += 1;
                        var rgb = HsvToRgb(h, Math.Clamp(s * sat(uv), 0, 1), v * val(uv));
                        var r = Vector3.Lerp(new Vector3(c.X, c.Y, c.Z), rgb, Math.Clamp(fac(uv), 0, 1));
                        return new Vector4(r, c.W);
                    };
                }
                case ShaderNodeTypes.BrightContrast:
                {
                    var color = In("Color"); var bright = InF("Bright"); var contrast = InF("Contrast");
                    return uv =>
                    {
                        var c = color(uv);
                        var k = contrast(uv);
                        var a = 1 + k; var b = bright(uv) - k * 0.5f;
                        var r = Vector3.Max(new Vector3(c.X, c.Y, c.Z) * a + new Vector3(b), Vector3.Zero);
                        return new Vector4(r, c.W);
                    };
                }
                case ShaderNodeTypes.SeparateRgb:
                {
                    var color = In("Color");
                    var channel = output switch { "G" => 1, "B" => 2, _ => 0 };
                    return uv =>
                    {
                        var c = color(uv);
                        return Fl(channel == 0 ? c.X : channel == 1 ? c.Y : c.Z);
                    };
                }
                case ShaderNodeTypes.CombineRgb:
                {
                    var r = InF("R"); var g = InF("G"); var b = InF("B");
                    return uv => new Vector4(r(uv), g(uv), b(uv), 1);
                }
                case ShaderNodeTypes.Bump:
                {
                    var strength = InF("Strength"); var distance = InF("Distance"); var height = InF("Height");
                    var invert = node.Option("Invert") == "Yes" ? -1f : 1f;
                    var e = _texel;
                    return uv =>
                    {
                        var dx = (height(uv + new Vector2(e, 0)) - height(uv - new Vector2(e, 0))) / (2 * e);
                        var dy = (height(uv + new Vector2(0, e)) - height(uv - new Vector2(0, e))) / (2 * e);
                        var k = strength(uv) * distance(uv) * invert;
                        var n = Vector3.Normalize(new Vector3(-dx * k, -dy * k, 1));
                        return new Vector4(n * 0.5f + new Vector3(0.5f), 1);
                    };
                }
                case ShaderNodeTypes.NormalMap:
                {
                    var strength = InF("Strength"); var color = In("Color");
                    return uv =>
                    {
                        var c = color(uv);
                        var n = new Vector3(c.X, c.Y, c.Z) * 2 - Vector3.One;
                        var s = strength(uv);
                        n = new Vector3(n.X * s, n.Y * s, MathF.Max(n.Z, 1e-3f));
                        n = Vector3.Normalize(n);
                        return new Vector4(n * 0.5f + new Vector3(0.5f), 1);
                    };
                }
                default:
                    return _ => Vector4.Zero;
            }
        }

        private TextureBuffer? LoadImage(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || _imageLoader is null) return null;
            lock (_images)
            {
                if (!_images.TryGetValue(path, out var image))
                {
                    try { image = _imageLoader(path); }
                    catch (Exception) { image = null; }
                    _images[path] = image;
                }
                return image;
            }
        }

        /// <summary>Bilinear, repeating (UV v grows downwards, row 0 at the top).</summary>
        private static Vector4 SampleImage(TextureBuffer image, Vector4 uv)
        {
            int w = image.Width, h = image.Height;
            var fx = (uv.X - MathF.Floor(uv.X)) * w - 0.5f;
            var fy = (uv.Y - MathF.Floor(uv.Y)) * h - 0.5f;
            var x0 = (int)MathF.Floor(fx); var y0 = (int)MathF.Floor(fy);
            var tx = fx - x0; var ty = fy - y0;
            int Wx(int x) => ((x % w) + w) % w;
            int Wy(int y) => ((y % h) + h) % h;
            var px = image.Pixels;
            Vector4 P(int x, int y) => px[Wy(y) * w + Wx(x)].Value;
            return Vector4.Lerp(Vector4.Lerp(P(x0, y0), P(x0 + 1, y0), tx), Vector4.Lerp(P(x0, y0 + 1), P(x0 + 1, y0 + 1), tx), ty);
        }

        public static Vector4 EvaluateRamp(RampStop[] stops, float t, string mode)
        {
            if (stops.Length == 0) return Fl(t);
            static Vector4 C(RampStop s) => new(s.R, s.G, s.B, s.A);
            if (t <= stops[0].Position) return C(stops[0]);
            if (t >= stops[^1].Position) return C(stops[^1]);
            for (var i = 0; i + 1 < stops.Length; i++)
            {
                var a = stops[i]; var b = stops[i + 1];
                if (t > b.Position) continue;
                if (mode == "Constant") return C(a);
                var span = b.Position - a.Position;
                var k = span <= 1e-6f ? 1 : (t - a.Position) / span;
                if (mode == "Ease") k = k * k * (3 - 2 * k);
                return Vector4.Lerp(C(a), C(b), k);
            }
            return C(stops[^1]);
        }

        private static Vector4 Blend(string mode, Vector4 a, Vector4 b)
        {
            static float Overlay(float x, float y) => x < 0.5f ? 2 * x * y : 1 - 2 * (1 - x) * (1 - y);
            return mode switch
            {
                "Add" => a + b,
                "Multiply" => a * b,
                "Subtract" => a - b,
                "Screen" => Vector4.One - (Vector4.One - a) * (Vector4.One - b),
                "Overlay" => new Vector4(Overlay(a.X, b.X), Overlay(a.Y, b.Y), Overlay(a.Z, b.Z), a.W),
                "Darken" => Vector4.Min(a, b),
                "Lighten" => Vector4.Max(a, b),
                "Difference" => Vector4.Abs(a - b),
                _ => b,
            };
        }

        private static float MathOp(string op, float a, float b) => op switch
        {
            "Subtract" => a - b,
            "Multiply" => a * b,
            "Divide" => b == 0 ? 0 : a / b,
            "Power" => a < 0 && b != MathF.Floor(b) ? 0 : MathF.Pow(a, b),
            "Minimum" => MathF.Min(a, b),
            "Maximum" => MathF.Max(a, b),
            "Greater Than" => a > b ? 1 : 0,
            "Less Than" => a < b ? 1 : 0,
            "Absolute" => MathF.Abs(a),
            "Sine" => MathF.Sin(a),
            "Modulo" => b == 0 ? 0 : a % b,
            "Snap" => b == 0 ? a : MathF.Floor(a / b) * b,
            _ => a + b,
        };

        private static (float H, float S, float V) RgbToHsv(Vector3 c)
        {
            var max = MathF.Max(c.X, MathF.Max(c.Y, c.Z));
            var min = MathF.Min(c.X, MathF.Min(c.Y, c.Z));
            var d = max - min;
            float h = 0;
            if (d > 1e-6f)
            {
                if (max == c.X) h = ((c.Y - c.Z) / d) % 6;
                else if (max == c.Y) h = (c.Z - c.X) / d + 2;
                else h = (c.X - c.Y) / d + 4;
                h /= 6; if (h < 0) h += 1;
            }
            return (h, max <= 1e-6f ? 0 : d / max, max);
        }

        private static Vector3 HsvToRgb(float h, float s, float v)
        {
            var i = (int)MathF.Floor(h * 6) % 6;
            var f = h * 6 - MathF.Floor(h * 6);
            float p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
            return i switch
            {
                0 => new Vector3(v, t, p),
                1 => new Vector3(q, v, p),
                2 => new Vector3(p, v, t),
                3 => new Vector3(p, q, v),
                4 => new Vector3(t, p, v),
                _ => new Vector3(v, p, q),
            };
        }
    }

    /// <summary>Periodic gradient noise and cellular noise for the texture nodes.</summary>
    internal static class Noise
    {
        private static readonly int[] Perm = BuildPermutation();

        private static int[] BuildPermutation()
        {
            var p = Enumerable.Range(0, 256).ToArray();
            var rng = new Random(1337); // fixed: textures are reproducible
            for (var i = 255; i > 0; i--) { var j = rng.Next(i + 1); (p[i], p[j]) = (p[j], p[i]); }
            return p.Concat(p).ToArray();
        }

        private static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);

        private static float Grad(int hash, float x, float y, float z)
        {
            var h = hash & 15;
            var u = h < 8 ? x : y;
            var v = h < 4 ? y : h is 12 or 14 ? x : z;
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        }

        private static int Wrap(int i, int period) => period > 0 ? ((i % period) + period) % period : i;

        /// <summary>Improved Perlin noise in about [-1, 1]; with <paramref name="period"/> &gt; 0
        /// it repeats every <paramref name="period"/> units in X and Y.</summary>
        public static float Perlin(Vector3 p, int period)
        {
            var xi = (int)MathF.Floor(p.X); var yi = (int)MathF.Floor(p.Y); var zi = (int)MathF.Floor(p.Z);
            var x = p.X - xi; var y = p.Y - yi; var z = p.Z - zi;
            var u = Fade(x); var v = Fade(y); var w = Fade(z);
            int X0 = Wrap(xi, period) & 255, X1 = Wrap(xi + 1, period) & 255;
            int Y0 = Wrap(yi, period) & 255, Y1 = Wrap(yi + 1, period) & 255;
            int Z0 = zi & 255, Z1 = (zi + 1) & 255;
            int H(int a, int b, int c) => Perm[Perm[Perm[a] + b] + c];
            float L(float t, float a, float b) => a + t * (b - a);
            return L(w,
                L(v, L(u, Grad(H(X0, Y0, Z0), x, y, z), Grad(H(X1, Y0, Z0), x - 1, y, z)),
                     L(u, Grad(H(X0, Y1, Z0), x, y - 1, z), Grad(H(X1, Y1, Z0), x - 1, y - 1, z))),
                L(v, L(u, Grad(H(X0, Y0, Z1), x, y, z - 1), Grad(H(X1, Y0, Z1), x - 1, y, z - 1)),
                     L(u, Grad(H(X0, Y1, Z1), x, y - 1, z - 1), Grad(H(X1, Y1, Z1), x - 1, y - 1, z - 1))));
        }

        /// <summary>Fractal noise in about [0, 1] (Blender's Noise Texture Fac).</summary>
        public static float Fbm(Vector3 p, float detail, float roughness, int period)
        {
            detail = Math.Clamp(detail, 0, 15);
            var octaves = (int)MathF.Floor(detail);
            float sum = 0, amp = 1, max = 0, freq = 1;
            for (var i = 0; i <= octaves; i++)
            {
                sum += amp * Perlin(p * freq, period > 0 ? period * (int)freq : 0);
                max += amp;
                amp *= roughness;
                freq *= 2;
            }
            var fraction = detail - octaves;
            if (fraction > 0)
            {
                var extra = amp * Perlin(p * freq, period > 0 ? period * (int)freq : 0);
                sum += fraction * extra;
                max += fraction * amp;
            }
            return Math.Clamp(0.5f + 0.5f * sum / MathF.Max(max, 1e-6f) * 1.4f, 0, 1);
        }

        public static float Hash01(int x, int y, int seed)
        {
            unchecked
            {
                var h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        public static Vector3 HashColor((int X, int Y) cell) =>
            new(Hash01(cell.X, cell.Y, 11), Hash01(cell.X, cell.Y, 23), Hash01(cell.X, cell.Y, 37));

        /// <summary>F1 distance (or distance to the cell edge) and the nearest cell.</summary>
        public static (float Distance, (int X, int Y) Cell) Voronoi(Vector2 p, float randomness, int period, bool edge)
        {
            var cx = (int)MathF.Floor(p.X); var cy = (int)MathF.Floor(p.Y);
            Vector2 Point(int x, int y)
            {
                int hx = Wrap(x, period), hy = Wrap(y, period);
                return new Vector2(x + 0.5f + (Hash01(hx, hy, 1) - 0.5f) * randomness, y + 0.5f + (Hash01(hx, hy, 2) - 0.5f) * randomness);
            }

            var best = float.MaxValue;
            var bestPoint = Vector2.Zero;
            var bestCell = (0, 0);
            for (var j = -1; j <= 1; j++)
            for (var i = -1; i <= 1; i++)
            {
                var q = Point(cx + i, cy + j);
                var d = Vector2.DistanceSquared(p, q);
                if (d < best) { best = d; bestPoint = q; bestCell = (Wrap(cx + i, period), Wrap(cy + j, period)); }
            }
            if (!edge) return (MathF.Sqrt(best), bestCell);

            // Distance to the nearest bisector between the closest point and its neighbours.
            var edgeDistance = float.MaxValue;
            var bx = (int)MathF.Floor(bestPoint.X); var by = (int)MathF.Floor(bestPoint.Y);
            for (var j = -2; j <= 2; j++)
            for (var i = -2; i <= 2; i++)
            {
                var q = Point(bx + i, by + j);
                if (Vector2.DistanceSquared(q, bestPoint) < 1e-10f) continue;
                var mid = (q + bestPoint) * 0.5f;
                var dir = Vector2.Normalize(q - bestPoint);
                edgeDistance = MathF.Min(edgeDistance, Vector2.Dot(mid - p, dir));
            }
            return (MathF.Max(0, edgeDistance), bestCell);
        }
    }
}
