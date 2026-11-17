using System.IO;
using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JolieCat3D.Core.Constraints;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Materials;
using JolieCat3D.Core.Modifiers;
using JolieCat3D.Core.Scene;
using JolieCat3D.Core.Skinning;
using JolieCat3D.Engine.Viewport;

namespace JolieCat3D.Engine.Rendering
{
    /// <summary>Final-render settings (Render Image, F12).</summary>
    public sealed class PathTracerSettings
    {
        public int MaxBounces { get; set; } = 4;
        public float Exposure { get; set; } = 1f;
        /// <summary>Multiplier on the sky light (environment).</summary>
        public float SkyStrength { get; set; } = 1f;
        public Vector3 SkyZenith { get; set; } = new(0.42f, 0.55f, 0.80f);
        public Vector3 SkyHorizon { get; set; } = new(0.80f, 0.82f, 0.86f);
        public Vector3 SkyGround { get; set; } = new(0.22f, 0.20f, 0.19f);
        /// <summary>Adds a sun when the scene has no lights (like the viewport's studio light).</summary>
        public bool DefaultSunWhenUnlit { get; set; } = true;
        /// <summary>Thin-lens depth of field: aperture radius in world units (0 = pinhole, all sharp).</summary>
        public float ApertureRadius { get; set; }

        /// <summary>Distance along the view direction that is in perfect focus.</summary>
        public float FocusDistance { get; set; } = 5f;

        /// <summary>Per-sample luminance clamp against fireflies (0 = off).</summary>
        public float FireflyClamp { get; set; } = 20f;

        /// <summary>Sample an HDR panorama's bright spots (sun) directly - sharp shadows, far less noise.</summary>
        public bool ImportanceSampleEnvironment { get; set; } = true;
    }

    /// <summary>
    /// A CPU path tracer for final stills - this app's "Cycles". Unidirectional, progressive
    /// (one sample per pixel per <see cref="RenderPass"/>), multithreaded over rows.
    ///
    /// Uses the same GGX/Lambert BRDF and light model as the viewport's Rendered mode, plus
    /// global illumination (diffuse and glossy bounces), soft sky light, emissive surfaces,
    /// textures (base colour, metallic-roughness, emission, alpha clip/blend) and
    /// anti-aliasing. Point/spot/sun lights are sampled directly with shadow rays; emissive
    /// surfaces and the sky are found by BSDF sampling. Meshes are drawn as the viewport
    /// draws them (shape keys, modifiers, skinning); hidden objects, bones and helper
    /// meshes are skipped.
    /// </summary>
    public sealed class PathTracer
    {
        private sealed class Texture
        {
            public int Width, Height;
            public Vector4[] Linear = Array.Empty<Vector4>(); // rgb linear, a

            public Vector4 Sample(Vector2 uv)
            {
                var x = uv.X - MathF.Floor(uv.X);
                var y = uv.Y - MathF.Floor(uv.Y);
                var fx = x * Width - 0.5f;
                var fy = y * Height - 0.5f;
                var x0 = (int)MathF.Floor(fx);
                var y0 = (int)MathF.Floor(fy);
                var tx = fx - x0;
                var ty = fy - y0;
                Vector4 P(int px, int py) => Linear[(((py % Height) + Height) % Height) * Width + (((px % Width) + Width) % Width)];
                return Vector4.Lerp(Vector4.Lerp(P(x0, y0), P(x0 + 1, y0), tx), Vector4.Lerp(P(x0, y0 + 1), P(x0 + 1, y0 + 1), tx), ty);
            }
        }

        private sealed class Mat
        {
            public Vector3 Albedo = new(0.8f);
            public float Opacity = 1f;
            public float Roughness = 0.5f, Metallic;
            public Vector3 Emission;
            public Texture? BaseTex, MrTex, EmissiveTex, NormalTex;
            public Vector2 UvOffset, UvScale = Vector2.One;
            public MaterialAlphaMode AlphaMode;
            public float AlphaCutoff = 0.5f;
        }

        private readonly struct Tri
        {
            public readonly Vector3 NA, NB, NC;
            public readonly Vector2 UA, UB, UC;
            public readonly int Material;

            public Tri(Vector3 na, Vector3 nb, Vector3 nc, Vector2 ua, Vector2 ub, Vector2 uc, int material)
            {
                NA = na; NB = nb; NC = nc; UA = ua; UB = ub; UC = uc; Material = material;
            }
        }

        private readonly record struct Light(int Type, Vector3 Radiance, Vector3 Position, Vector3 Direction, float Range, float CosOuter, float CosInner,
            Vector3 HalfU = default, Vector3 HalfV = default);

        /// <summary>One light sample towards <paramref name="p"/>: direction to the light,
        /// arriving radiance (viewport light model) and the shadow-ray length. Area lights
        /// pick a random point on their rectangle, so shadows soften over many samples.</summary>
        private static bool SampleLight(in Light light, Vector3 p, Random rng, out Vector3 dir, out Vector3 radiance, out float maxDistance)
        {
            radiance = light.Radiance;
            maxDistance = float.MaxValue;
            if (light.Type == 0)
            {
                dir = -light.Direction;
                return true;
            }
            var lightPoint = light.Position;
            if (light.Type == 3)
                lightPoint += light.HalfU * (rng.NextSingle() * 2f - 1f) + light.HalfV * (rng.NextSingle() * 2f - 1f);
            var d = lightPoint - p;
            var dist = d.Length();
            dir = Vector3.Zero;
            if (dist < 1e-5f) return false;
            dir = d / dist;
            var rel = dist / light.Range;
            var att = Math.Clamp(1f - MathF.Pow(rel, 4), 0f, 1f);
            radiance *= att * att / (1f + 4f * rel * rel);
            if (light.Type == 2) radiance *= SmoothStep(light.CosOuter, light.CosInner, Vector3.Dot(-dir, light.Direction));
            if (light.Type == 3) radiance *= MathF.Max(0f, Vector3.Dot(-dir, light.Direction)); // emits from its front only
            maxDistance = dist;
            return radiance != Vector3.Zero;
        }

        private readonly TriangleBvh _bvh;
        private readonly List<(Vector3 A, Vector3 B, Vector3 C)> _positions = new();
        private readonly List<Tri> _tris = new();
        private readonly List<Mat> _materials = new();
        private readonly List<Light> _lights = new();
        private readonly Dictionary<string, Texture?> _textureCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly PathTracerSettings _settings;
        private readonly Matrix4x4 _inverseViewProj;
        private readonly Vector3 _cameraPosition;
        private readonly bool _ortho;
        private readonly Vector3 _cameraForward, _cameraRight, _cameraUp;
        private readonly Vector4[] _accumulation;
        // Skybox cube faces (+X,-X,+Y,-Y,+Z,-Z, D3D order, linear colour), when the scene has one.
        private Texture[]? _skyFaces;
        // Equirectangular HDR panorama (unclamped linear light), when the skybox is a .hdr.
        private HdrImage? _skyPanorama;
        private float _worldStrength = 1f;
        private float _skyRotation;
        // Importance sampling of the HDR panorama (null for gradient / cube skies).
        private EnvironmentSampler? _envSampler;
        // First-hit guide buffers for the denoiser (sums over samples).
        private readonly Vector3[] _albedoSum;
        private readonly Vector3[] _normalSum;
        private readonly float[] _depthSum;

        public int Width { get; }
        public int Height { get; }
        public int SampleCount { get; private set; }
        public int TriangleCount => _positions.Count;

        public PathTracer(Scene3D scene, ViewportCamera camera, int width, int height, PathTracerSettings? settings = null)
        {
            Width = Math.Max(1, width);
            Height = Math.Max(1, height);
            _settings = settings ?? new PathTracerSettings();
            _accumulation = new Vector4[Width * Height];
            _albedoSum = new Vector3[Width * Height];
            _normalSum = new Vector3[Width * Height];
            _depthSum = new float[Width * Height];

            var viewProj = camera.GetView() * camera.GetProjection(Width / (float)Height);
            Matrix4x4.Invert(viewProj, out _inverseViewProj);
            _cameraPosition = camera.Position;
            _ortho = camera.IsOrthographic;
            _cameraForward = camera.Forward;
            _cameraRight = camera.Right;
            _cameraUp = camera.Up;

            LoadSkybox(scene.Environment?.SkyboxSource);
            _skyRotation = scene.Environment?.SkyboxRotation ?? 0f;
            _worldStrength = Math.Max(0f, scene.Environment?.SkyboxStrength ?? 1f);
            BuildScene(scene);
            _bvh = new TriangleBvh(_positions);
            if (_skyPanorama is { } panorama && _settings.ImportanceSampleEnvironment) _envSampler = EnvironmentSampler.Build(panorama, _skyRotation, _settings.SkyStrength * _worldStrength);
        }

        // ============================= scene =============================

        private void BuildScene(Scene3D scene)
        {
            try { ConstraintSolver.Apply(scene); } catch (Exception) { }
            var materialIndex = new Dictionary<Material, int>();
            foreach (var node in scene.Traverse())
            {
                if (node.Light is { } light) AddLight(node, light);
                if (node.IsHidden || node.Bone is not null || node.Light is not null || node.Mesh is not { } mesh) continue;

                Mesh evaluated;
                try
                {
                    evaluated = ModifierStack.Evaluate(ShapeKeys.Evaluate(mesh), node.Modifiers, node);
                    if (node.SkinBinding is { } binding) evaluated = SkinningEvaluator.Deform(evaluated, node.GetWorldTransform(), binding);
                }
                catch (Exception) { evaluated = mesh; }

                var world = node.GetWorldTransform();
                Matrix4x4.Invert(world, out var inverse);
                var normalMatrix = Matrix4x4.Transpose(inverse);

                int MaterialFor(int slot)
                {
                    var material = slot >= 0 && slot < evaluated.MaterialSlots.Count ? evaluated.MaterialSlots[slot] : evaluated.Material;
                    material ??= mesh.Material;
                    if (material is null) { _materials.Add(new Mat()); return _materials.Count - 1; }
                    if (!materialIndex.TryGetValue(material, out var index))
                    {
                        index = _materials.Count;
                        _materials.Add(Convert(material));
                        materialIndex[material] = index;
                    }
                    return index;
                }

                void Add(int a, int b, int c, int slot)
                {
                    var va = evaluated.Vertices[a];
                    var vb = evaluated.Vertices[b];
                    var vc = evaluated.Vertices[c];
                    var pa = Vector3.Transform(va.Position, world);
                    var pb = Vector3.Transform(vb.Position, world);
                    var pc = Vector3.Transform(vc.Position, world);
                    if (Vector3.Cross(pb - pa, pc - pa).LengthSquared() < 1e-24f) return;
                    _positions.Add((pa, pb, pc));
                    _tris.Add(new Tri(
                        SafeNormal(Vector3.TransformNormal(va.Normal, normalMatrix)),
                        SafeNormal(Vector3.TransformNormal(vb.Normal, normalMatrix)),
                        SafeNormal(Vector3.TransformNormal(vc.Normal, normalMatrix)),
                        va.UV, vb.UV, vc.UV, MaterialFor(slot)));
                }

                foreach (var f in evaluated.Faces) Add(f.A, f.B, f.C, -1);
                foreach (var p in evaluated.Polygons)
                    for (var i = 1; i + 1 < p.Indices.Count; i++) Add(p.Indices[0], p.Indices[i], p.Indices[i + 1], p.MaterialSlotIndex);
            }

            // A world texture lights the scene on its own (as in Blender); only the plain gradient sky gets a stand-in sun.
            if (_lights.Count == 0 && _settings.DefaultSunWhenUnlit && _skyPanorama == null && _skyFaces == null)
                _lights.Add(new Light(0, new Vector3(3.2f), Vector3.Zero, Vector3.Normalize(new Vector3(-0.45f, -1f, -0.35f)), 0, 0, 0));
        }

        private static Vector3 SafeNormal(Vector3 n) => n.LengthSquared() > 1e-20f ? Vector3.Normalize(n) : Vector3.Zero;

        private void AddLight(Node node, LightData light)
        {
            if (node.IsHidden) return;
            // Same units as the viewport's Rendered mode (DxSceneRenderer.BuildLightConstants).
            var radiance = SrgbToLinear(new Vector3(light.Color.R, light.Color.G, light.Color.B)) * MathF.Max(0, light.Intensity) * MathF.PI;
            var half = Math.Clamp(light.SpotAngle, 1f, 179f) * MathF.PI / 360f;
            var type = light.Type switch { LightType.Point => 1, LightType.Spot => 2, LightType.Area => 3, _ => 0 };
            var world = node.GetWorldTransform();
            var halfU = Vector3.TransformNormal(Vector3.UnitX, world) * (light.Width / 2f);
            var halfV = Vector3.TransformNormal(Vector3.UnitY, world) * (light.Height / 2f);
            _lights.Add(new Light(type, radiance, node.GetWorldPosition(), Vector3.Normalize(node.GetWorldForward()),
                MathF.Max(0.01f, light.Range), MathF.Cos(half), MathF.Cos(half * 0.6f), halfU, halfV));
        }

        private Mat Convert(Material m)
        {
            var mat = new Mat
            {
                Albedo = SrgbToLinear(new Vector3(m.DiffuseColor.R, m.DiffuseColor.G, m.DiffuseColor.B)),
                Opacity = Math.Clamp(m.Opacity * m.DiffuseColor.A, 0f, 1f),
                Roughness = Math.Clamp(m.Roughness, 0.02f, 1f),
                Metallic = Math.Clamp(m.Metallic, 0f, 1f),
                UvOffset = m.DiffuseTextureOffset,
                UvScale = m.DiffuseTextureScale,
                AlphaMode = m.AlphaMode,
                AlphaCutoff = m.AlphaCutoff,
                BaseTex = m.PaintedTextureBuffer is { } painted ? FromBuffer(painted) : LoadTexture(m.DiffuseTexturePath, srgb: true),
                MrTex = LoadTexture(m.MetallicRoughnessTexturePath, srgb: false),
                NormalTex = LoadTexture(m.NormalTexturePath, srgb: false),
            };
            if (mat.BaseTex is not null && m.DiffuseColor.R > 0.999f && m.DiffuseColor.G > 0.999f && m.DiffuseColor.B > 0.999f) mat.Albedo = Vector3.One;
            if (m.IsEmissive)
            {
                mat.Emission = SrgbToLinear(new Vector3(m.EmissiveColor.R, m.EmissiveColor.G, m.EmissiveColor.B)) * m.EmissiveStrength;
                mat.EmissiveTex = LoadTexture(m.EmissiveTexturePath, srgb: true);
            }
            return mat;
        }

        private static Texture FromBuffer(TextureBuffer buffer)
        {
            var tex = new Texture { Width = buffer.Width, Height = buffer.Height, Linear = new Vector4[buffer.Width * buffer.Height] };
            for (var i = 0; i < tex.Linear.Length; i++)
            {
                var c = buffer.Pixels[i];
                tex.Linear[i] = new Vector4(SrgbToLinear(new Vector3(c.R, c.G, c.B)), c.A);
            }
            return tex;
        }

        private Texture? LoadTexture(string? path, bool srgb)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var key = path + (srgb ? "|s" : "|l");
            if (_textureCache.TryGetValue(key, out var cached)) return cached;
            Texture? tex = null;
            try
            {
                if (File.Exists(path))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(Path.GetFullPath(path));
                    bitmap.EndInit();
                    var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                    var w = converted.PixelWidth;
                    var h = converted.PixelHeight;
                    var bytes = new byte[w * h * 4];
                    converted.CopyPixels(bytes, w * 4, 0);
                    tex = new Texture { Width = w, Height = h, Linear = new Vector4[w * h] };
                    for (var i = 0; i < w * h; i++)
                    {
                        var rgb = new Vector3(bytes[i * 4 + 2], bytes[i * 4 + 1], bytes[i * 4]) / 255f;
                        tex.Linear[i] = new Vector4(srgb ? SrgbToLinear(rgb) : rgb, bytes[i * 4 + 3] / 255f);
                    }
                }
            }
            catch (Exception) { tex = null; } // a broken texture renders as untextured
            _textureCache[key] = tex;
            return tex;
        }

        // ============================= rendering =============================

        /// <summary>Adds one sample per pixel (call repeatedly for a progressive render).</summary>
        public void RenderPass(CancellationToken cancellation = default)
        {
            var pass = SampleCount;
            Parallel.For(0, Height, new ParallelOptions { CancellationToken = cancellation }, y =>
            {
                var rng = new Random(unchecked(y * 73856093 ^ pass * 19349663 ^ 0x5EED)); // deterministic (HashCode is per-process random)
                for (var x = 0; x < Width; x++)
                {
                    var (origin, direction) = CameraRay(x + rng.NextSingle(), y + rng.NextSingle());
                    if (_settings.ApertureRadius > 0 && !_ortho)
                    {
                        // Thin lens: aim at the focal plane through a random point on the aperture disc.
                        var focus = origin + direction * (_settings.FocusDistance / MathF.Max(1e-4f, Vector3.Dot(direction, _cameraForward)));
                        var radius = _settings.ApertureRadius * MathF.Sqrt(rng.NextSingle());
                        var angle = rng.NextSingle() * MathF.PI * 2f;
                        origin += _cameraRight * (radius * MathF.Cos(angle)) + _cameraUp * (radius * MathF.Sin(angle));
                        direction = Vector3.Normalize(focus - origin);
                    }
                    var color = Trace(origin, direction, rng, out var firstAlbedo, out var firstNormal, out var firstDepth);
                    var index = y * Width + x;
                    _albedoSum[index] += firstAlbedo;
                    _normalSum[index] += firstNormal;
                    _depthSum[index] += firstDepth;
                    if (!float.IsFinite(color.X) || !float.IsFinite(color.Y) || !float.IsFinite(color.Z)) color = Vector3.Zero;
                    if (_settings.FireflyClamp > 0)
                    {
                        var lum = Luminance(color);
                        if (lum > _settings.FireflyClamp) color *= _settings.FireflyClamp / lum;
                    }
                    _accumulation[y * Width + x] += new Vector4(color, 1);
                }
            });
            SampleCount++;
        }

        /// <summary>
        /// Lightmap bake: the light arriving at every texel of <paramref name="mesh"/>'s UV
        /// layout (direct light with shadows + global illumination from sky, emitters and
        /// bounces), as "light colour" to multiply with albedo in a game engine - white
        /// means fully lit. <paramref name="mesh"/> is the object as drawn, in its own local
        /// space (<paramref name="world"/> places it); it should also be part of the scene
        /// this tracer was built from so it shadows itself. UVs must not overlap.
        /// </summary>
        public TextureBuffer BakeLightmap(Mesh mesh, Matrix4x4 world, int size, int samples, IProgress<float>? progress = null, CancellationToken cancel = default)
        {
            var hdr = BakeLightmapHdr(mesh, world, size, samples, progress, cancel);
            var buffer = new TextureBuffer(size, size, new Core.Numerics.Color4(0, 0, 0, 1));
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var c = LinearToSrgb(Vector3.Clamp(hdr[x, y], Vector3.Zero, Vector3.One));
                buffer.SetPixel(x, y, new Core.Numerics.Color4(c.X, c.Y, c.Z, 1));
            }
            return buffer;
        }

        /// <summary>The same bake as <see cref="BakeLightmap"/>, kept linear and unclamped
        /// (light brighter than white survives) - save it as Radiance .hdr.</summary>
        public HdrImage BakeLightmapHdr(Mesh mesh, Matrix4x4 world, int size, int samples, IProgress<float>? progress = null, CancellationToken cancel = default)
        {
            var normalMatrix = Matrix4x4.Invert(world, out var inv) ? Matrix4x4.Transpose(inv) : Matrix4x4.Identity;
            var jobs = new List<(int X, int Y, Vector3 P, Vector3 N)>();
            TextureBaker.RasterizeUv(mesh, size, (x, y, f, w) =>
            {
                var va = mesh.Vertices[f.A]; var vb = mesh.Vertices[f.B]; var vc = mesh.Vertices[f.C];
                var p = va.Position * w.X + vb.Position * w.Y + vc.Position * w.Z;
                var n = va.Normal * w.X + vb.Normal * w.Y + vc.Normal * w.Z;
                if (n.LengthSquared() < 1e-12f) n = Vector3.Cross(vb.Position - va.Position, vc.Position - va.Position);
                jobs.Add((x, y, Vector3.Transform(p, world), Vector3.Normalize(Vector3.TransformNormal(n, normalMatrix))));
            });

            var r = new float[size * size];
            var g = new float[size * size];
            var b = new float[size * size];
            var covered = new bool[size * size];
            var done = 0;
            Parallel.For(0, jobs.Count, new ParallelOptions { CancellationToken = cancel }, i =>
            {
                var (x, y, p, n) = jobs[i];
                var rng = new Random(unchecked(x * 73856093 ^ y * 19349663 ^ 0xBA4E));
                var origin = p + n * 1e-3f;
                var light = Vector3.Zero;

                // Direct: analytic lights with shadow rays (Lambert, as light colour = E / pi).
                foreach (var l in _lights)
                {
                    // Area lights: several samples per texel for soft shadows.
                    var count = l.Type == 3 ? 8 : 1;
                    for (var k = 0; k < count; k++)
                    {
                        if (!SampleLight(l, p, rng, out var dir, out var radiance, out var maxDistance)) continue;
                        var ndl = Vector3.Dot(n, dir);
                        if (ndl <= 0 || _bvh.Occluded(origin, dir, 1e-4f, maxDistance)) continue;
                        light += radiance * ndl / MathF.PI / count;
                    }
                }

                // Indirect: cosine-weighted hemisphere of traced radiance (average = E / pi).
                var (t, bt) = Basis(n);
                var indirect = Vector3.Zero;
                for (var s = 0; s < samples; s++)
                {
                    var u1 = (s + rng.NextSingle()) / samples;
                    var u2 = rng.NextSingle();
                    var rr = MathF.Sqrt(u1);
                    var phi = 2f * MathF.PI * u2;
                    var dir = Vector3.Normalize(t * (rr * MathF.Cos(phi)) + bt * (rr * MathF.Sin(phi)) + n * MathF.Sqrt(MathF.Max(0, 1f - u1)));
                    var cosPdf = MathF.Max(0f, Vector3.Dot(n, dir)) / MathF.PI;
                    var c = Trace(origin, dir, rng, out _, out _, out _, cosPdf);
                    if (_envSampler is { } env && env.Sample(rng, out var envDir, out var envRadiance, out var envPdf))
                    {
                        var ndl = Vector3.Dot(n, envDir);
                        if (ndl > 0 && !_bvh.Occluded(origin, envDir, 1e-4f, 1e30f))
                        {
                            // Same units as the traced samples (radiance; the average is E / pi).
                            var w = EnvironmentSampler.PowerHeuristic(envPdf, ndl / MathF.PI);
                            c += envRadiance * (ndl / MathF.PI / envPdf * w);
                        }
                    }
                    if (float.IsFinite(c.X) && float.IsFinite(c.Y) && float.IsFinite(c.Z)) indirect += Vector3.Min(c, new Vector3(_settings.FireflyClamp > 0 ? _settings.FireflyClamp : float.MaxValue));
                }
                light += indirect / Math.Max(1, samples);
                light *= _settings.Exposure;

                var index = y * size + x;
                r[index] = light.X; g[index] = light.Y; b[index] = light.Z;
                covered[index] = true;
                if ((Interlocked.Increment(ref done) & 255) == 0) progress?.Report(done / (float)jobs.Count);
            });

            // Smooth noise within islands, then bleed past island edges (no seams when filtered).
            foreach (var channel in new[] { r, g, b })
            {
                TextureBaker.Smooth(channel, (bool[])covered.Clone(), size, 2);
                TextureBaker.Dilate(channel, (bool[])covered.Clone(), size, 4);
            }
            var image = new HdrImage(size, size);
            for (var i = 0; i < image.Pixels.Length; i++) image.Pixels[i] = new Vector3(r[i], g[i], b[i]);
            progress?.Report(1f);
            return image;
        }

        /// <summary>The current image: averaged (optionally denoised), exposed, ACES
        /// tone-mapped, sRGB, BGRA32.</summary>
        public byte[] ToBgra(bool denoise = false)
        {
            var bytes = new byte[Width * Height * 4];
            var samples = Math.Max(1, SampleCount);
            var linear = denoise ? Denoised() : null;
            for (var i = 0; i < _accumulation.Length; i++)
            {
                var c = (linear is not null ? linear[i] : new Vector3(_accumulation[i].X, _accumulation[i].Y, _accumulation[i].Z) / samples) * _settings.Exposure;
                var mapped = LinearToSrgb(Aces(c));
                bytes[i * 4 + 0] = ToByte(mapped.Z);
                bytes[i * 4 + 1] = ToByte(mapped.Y);
                bytes[i * 4 + 2] = ToByte(mapped.X);
                bytes[i * 4 + 3] = 255;
            }
            return bytes;
        }

        /// <summary>Average linear colour per pixel (before exposure/tone mapping).</summary>
        public Vector3[] LinearImage()
        {
            var samples = Math.Max(1, SampleCount);
            return _accumulation.Select(a => new Vector3(a.X, a.Y, a.Z) / samples).ToArray();
        }

        /// <summary>
        /// Edge-aware à-trous wavelet denoiser (5 passes, 5x5 B3 kernel). Works on
        /// illumination (colour / first-hit albedo) so textures stay sharp, and stops at
        /// edges using the first-hit normal, depth and albedo; the colour weight relaxes as
        /// samples accumulate less noise.
        /// </summary>
        public Vector3[] Denoised()
        {
            var count = Width * Height;
            var samples = Math.Max(1, SampleCount);
            var albedo = new Vector3[count];
            var normal = new Vector3[count];
            var depth = new float[count];
            var light = new Vector3[count];
            for (var i = 0; i < count; i++)
            {
                albedo[i] = Vector3.Max(_albedoSum[i] / samples, new Vector3(0.02f));
                var n = _normalSum[i];
                normal[i] = n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : Vector3.Zero;
                depth[i] = _depthSum[i] / samples;
                light[i] = new Vector3(_accumulation[i].X, _accumulation[i].Y, _accumulation[i].Z) / samples / albedo[i];
            }

            ReadOnlySpan<float> kernel = stackalloc float[] { 1f / 16, 1f / 4, 3f / 8, 1f / 4, 1f / 16 };
            var colorSigma = 4f / MathF.Sqrt(samples) + 0.05f;
            var kernelArray = kernel.ToArray();
            var next = new Vector3[count];
            for (var pass = 0; pass < 5; pass++)
            {
                var step = 1 << pass;
                var source = light;
                Parallel.For(0, Height, y =>
                {
                    for (var x = 0; x < Width; x++)
                    {
                        var p = y * Width + x;
                        var lp = Luminance(source[p]);
                        var sum = Vector3.Zero;
                        var weightSum = 0f;
                        for (var ky = -2; ky <= 2; ky++)
                        {
                            var qy = y + ky * step;
                            if (qy < 0 || qy >= Height) continue;
                            for (var kx = -2; kx <= 2; kx++)
                            {
                                var qx = x + kx * step;
                                if (qx < 0 || qx >= Width) continue;
                                var q = qy * Width + qx;
                                var w = kernelArray[kx + 2] * kernelArray[ky + 2];
                                if (q != p)
                                {
                                    var wn = normal[p] == Vector3.Zero || normal[q] == Vector3.Zero
                                        ? (normal[p] == normal[q] ? 1f : 0f)
                                        : MathF.Pow(MathF.Max(0f, Vector3.Dot(normal[p], normal[q])), 32);
                                    var wd = MathF.Exp(-MathF.Abs(depth[p] - depth[q]) / (0.03f * MathF.Max(depth[p], 1e-3f) * step + 1e-4f));
                                    var wa = MathF.Exp(-Vector3.DistanceSquared(albedo[p], albedo[q]) / 0.02f);
                                    var wc = MathF.Exp(-MathF.Abs(lp - Luminance(source[q])) / (colorSigma * (lp + 0.05f)));
                                    w *= wn * wd * wa * wc;
                                }
                                sum += source[q] * w;
                                weightSum += w;
                            }
                        }
                        next[p] = weightSum > 1e-12f ? sum / weightSum : source[p];
                    }
                });
                (light, next) = (next, light);
            }

            for (var i = 0; i < count; i++) light[i] *= albedo[i];
            return light;
        }

        public BitmapSource ToBitmap(bool denoise = false)
        {
            var bitmap = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, ToBgra(denoise), Width * 4);
            bitmap.Freeze();
            return bitmap;
        }

        private (Vector3 Origin, Vector3 Direction) CameraRay(float px, float py)
        {
            var ndcX = 2f * px / Width - 1f;
            var ndcY = 1f - 2f * py / Height;
            var near = Unproject(new Vector4(ndcX, ndcY, 0f, 1f));
            var far = Unproject(new Vector4(ndcX, ndcY, 1f, 1f));
            var dir = far - near;
            if (dir.LengthSquared() < 1e-20f) dir = Vector3.UnitZ;
            return (_ortho ? near : _cameraPosition, Vector3.Normalize(_ortho ? dir : far - _cameraPosition));

            Vector3 Unproject(Vector4 ndc)
            {
                var v = Vector4.Transform(ndc, _inverseViewProj);
                return new Vector3(v.X, v.Y, v.Z) / v.W;
            }
        }

        /// <param name="firstPdf">Solid-angle pdf the caller drew <paramref name="direction"/>
        /// with (0 = not a BSDF-style sample, e.g. a camera ray): an escaping ray is then
        /// weighted against environment light sampling (MIS).</param>
        private Vector3 Trace(Vector3 origin, Vector3 direction, Random rng, out Vector3 firstAlbedo, out Vector3 firstNormal, out float firstDepth, float firstPdf = 0f)
        {
            // Defaults = sky: white albedo, no normal, far away.
            firstAlbedo = Vector3.One;
            firstNormal = Vector3.Zero;
            firstDepth = 1e6f;
            var recorded = false;
            var eye = origin;
            var radiance = Vector3.Zero;
            var throughput = Vector3.One;
            var passThrough = 0;
            var lastPdf = firstPdf;

            for (var bounce = 0; bounce <= _settings.MaxBounces;)
            {
                if (_bvh.Intersect(origin, direction, 1e-4f) is not { } hit)
                {
                    var sky = Sky(direction);
                    if (_envSampler is { } env && lastPdf > 0) sky *= EnvironmentSampler.PowerHeuristic(lastPdf, env.Pdf(direction));
                    radiance += throughput * sky;
                    break;
                }

                var tri = _tris[hit.Triangle];
                var (pa, pb, pc) = _positions[hit.Triangle];
                var w = 1f - hit.U - hit.V;
                var position = origin + direction * hit.T;
                var uvRaw = tri.UA * w + tri.UB * hit.U + tri.UC * hit.V;
                var mat = _materials[tri.Material];
                var uv = uvRaw * mat.UvScale + mat.UvOffset;

                // Material at the hit.
                var albedo = mat.Albedo;
                var alpha = mat.Opacity;
                if (mat.BaseTex is { } baseTex)
                {
                    var t = baseTex.Sample(uv);
                    albedo *= new Vector3(t.X, t.Y, t.Z);
                    alpha *= t.W;
                }
                var passes = mat.AlphaMode == MaterialAlphaMode.Clip
                    ? alpha < mat.AlphaCutoff
                    : (mat.AlphaMode == MaterialAlphaMode.Blend || alpha < 0.999f) && rng.NextSingle() > alpha;
                if (passes)
                {
                    origin = position + direction * 1e-4f;
                    if (++passThrough > 64) break;
                    continue;
                }

                var rough = mat.Roughness;
                var metal = mat.Metallic;
                if (mat.MrTex is { } mrTex)
                {
                    var mr = mrTex.Sample(uv);
                    rough = Math.Clamp(rough * mr.Y, 0.02f, 1f);
                    metal *= mr.Z;
                }

                var geometric = Vector3.Normalize(Vector3.Cross(pb - pa, pc - pa));
                var n = tri.NA * w + tri.NB * hit.U + tri.NC * hit.V;
                n = n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : geometric;
                var v = -direction;
                // Two-sided, winding-independent shading (same as the viewport).
                if (Vector3.Dot(geometric, v) < 0) geometric = -geometric;
                if (Vector3.Dot(n, geometric) < 0) n = -n;

                if (mat.NormalTex is { } normalTex)
                {
                    // Tangent frame from the triangle UVs (T = dP/du, B = dP/dv, like the viewport shader).
                    var uva = tri.UA * mat.UvScale; var uvb = tri.UB * mat.UvScale; var uvc = tri.UC * mat.UvScale;
                    var e1 = pb - pa; var e2 = pc - pa;
                    var du1 = uvb.X - uva.X; var dv1 = uvb.Y - uva.Y;
                    var du2 = uvc.X - uva.X; var dv2 = uvc.Y - uva.Y;
                    var det = du1 * dv2 - du2 * dv1;
                    if (MathF.Abs(det) > 1e-12f)
                    {
                        var r = 1f / det;
                        var tan = (e1 * dv2 - e2 * dv1) * r;
                        var bit = (e2 * du1 - e1 * du2) * r;
                        tan -= n * Vector3.Dot(n, tan);
                        bit -= n * Vector3.Dot(n, bit);
                        if (tan.LengthSquared() > 1e-20f && bit.LengthSquared() > 1e-20f)
                        {
                            var m = normalTex.Sample(uv);
                            var mapped = Vector3.Normalize(tan) * (m.X * 2 - 1) + Vector3.Normalize(bit) * (m.Y * 2 - 1) + n * (m.Z * 2 - 1);
                            if (mapped.LengthSquared() > 1e-12f)
                            {
                                mapped = Vector3.Normalize(mapped);
                                if (Vector3.Dot(mapped, geometric) > 0.01f) n = mapped;
                            }
                        }
                    }
                }

                if (!recorded)
                {
                    recorded = true;
                    firstAlbedo = Vector3.Lerp(albedo, Vector3.One, mat.Metallic * 0.5f);
                    firstNormal = n;
                    firstDepth = Vector3.Distance(eye, position);
                }

                if (mat.Emission != Vector3.Zero)
                {
                    var e = mat.Emission;
                    if (mat.EmissiveTex is { } emTex) { var t = emTex.Sample(uv); e *= new Vector3(t.X, t.Y, t.Z); }
                    radiance += throughput * e;
                }

                var offset = position + geometric * 1e-4f;

                // Direct light (analytic lights, shadow rays).
                foreach (var light in _lights)
                {
                    if (!SampleLight(light, position, rng, out var l, out var lightRadiance, out var maxDistance)) continue;
                    if (Vector3.Dot(n, l) <= 0 || Vector3.Dot(geometric, l) <= 0) continue;
                    if (_bvh.Occluded(offset, l, 1e-4f, maxDistance)) continue;
                    radiance += throughput * Brdf(n, v, l, albedo, rough, metal) * lightRadiance;
                }

                var specularChance = Math.Clamp(0.5f * (1f - rough) + 0.5f * metal + 0.1f, 0.1f, 0.9f);

                // Direct light from the HDR environment (importance sampled, MIS with the BSDF).
                if (_envSampler is { } environment && environment.Sample(rng, out var envDir, out var envRadiance, out var envPdf)
                    && Vector3.Dot(n, envDir) > 0 && Vector3.Dot(geometric, envDir) > 0
                    && !_bvh.Occluded(offset, envDir, 1e-4f, 1e30f))
                {
                    // With no further bounce the BSDF strategy never reaches the sky: full weight.
                    var mis = bounce == _settings.MaxBounces ? 1f
                        : EnvironmentSampler.PowerHeuristic(envPdf, BsdfPdf(n, v, envDir, rough, specularChance));
                    var contribution = Brdf(n, v, envDir, albedo, rough, metal) * envRadiance * (mis / envPdf);
                    if (float.IsFinite(contribution.X + contribution.Y + contribution.Z)) radiance += throughput * contribution;
                }

                if (bounce == _settings.MaxBounces) break;

                // Next direction: pick the specular or diffuse lobe.
                var f0 = Vector3.Lerp(new Vector3(0.04f), albedo, metal);
                var (tangent, bitangent) = Basis(n);
                Vector3 next;
                Vector3 weight;
                if (rng.NextSingle() < specularChance)
                {
                    var a = MathF.Max(rough * rough, 0.02f);
                    var u1 = rng.NextSingle();
                    var u2 = rng.NextSingle();
                    var cosTheta = MathF.Sqrt((1f - u1) / (1f + (a * a - 1f) * u1));
                    var sinTheta = MathF.Sqrt(MathF.Max(0, 1f - cosTheta * cosTheta));
                    var phi = 2f * MathF.PI * u2;
                    var h = Vector3.Normalize(tangent * (sinTheta * MathF.Cos(phi)) + bitangent * (sinTheta * MathF.Sin(phi)) + n * cosTheta);
                    next = Vector3.Reflect(-v, h);
                    var ndl = Vector3.Dot(n, next);
                    var ndv = MathF.Max(Vector3.Dot(n, v), 1e-4f);
                    var ndh = MathF.Max(Vector3.Dot(n, h), 1e-4f);
                    var vdh = MathF.Max(Vector3.Dot(v, h), 1e-4f);
                    if (ndl <= 0 || Vector3.Dot(geometric, next) <= 0) break;
                    var fresnel = f0 + (Vector3.One - f0) * MathF.Pow(1f - vdh, 5);
                    weight = fresnel * (GeometrySmith(ndv, ndl, rough) * vdh / (ndv * ndh)) / specularChance;
                }
                else
                {
                    var u1 = rng.NextSingle();
                    var u2 = rng.NextSingle();
                    var r = MathF.Sqrt(u1);
                    var phi = 2f * MathF.PI * u2;
                    next = Vector3.Normalize(tangent * (r * MathF.Cos(phi)) + bitangent * (r * MathF.Sin(phi)) + n * MathF.Sqrt(MathF.Max(0, 1f - u1)));
                    if (Vector3.Dot(geometric, next) <= 0) break;
                    var h = Vector3.Normalize(v + next);
                    var fresnel = f0 + (Vector3.One - f0) * MathF.Pow(1f - Math.Clamp(Vector3.Dot(h, v), 0f, 1f), 5);
                    var kd = (Vector3.One - fresnel) * (1f - metal);
                    weight = kd * albedo / (1f - specularChance);
                }

                throughput *= weight;
                bounce++;
                lastPdf = _envSampler is null ? 0f : BsdfPdf(n, v, next, rough, specularChance);

                // Russian roulette.
                if (bounce >= 3)
                {
                    var survive = Math.Clamp(MathF.Max(throughput.X, MathF.Max(throughput.Y, throughput.Z)), 0.05f, 0.95f);
                    if (rng.NextSingle() > survive) break;
                    throughput /= survive;
                }

                origin = offset;
                direction = next;
            }
            return radiance;
        }

        private void LoadSkybox(string? source)
        {
            if (EnvironmentSettings.IsPanorama(source))
            {
                try { _skyPanorama = HdrImage.LoadHdr(source!); }
                catch (Exception) { _skyPanorama = null; }
                return;
            }
            if (string.IsNullOrWhiteSpace(source) || !Dx.TextureCache.TryResolveFacePaths(source, out var paths)) return;
            try
            {
                _skyFaces = paths.Select(path =>
                {
                    var (pixels, w, h) = Dx.TextureCache.DecodeBgra(path);
                    var tex = new Texture { Width = w, Height = h, Linear = new Vector4[w * h] };
                    for (var i = 0; i < w * h; i++)
                        tex.Linear[i] = new Vector4(SrgbToLinear(new Vector3(pixels[i * 4 + 2], pixels[i * 4 + 1], pixels[i * 4]) / 255f), 1f);
                    return tex;
                }).ToArray();
            }
            catch (Exception) { _skyFaces = null; } // unreadable skybox: fall back to the gradient
        }

        /// <summary>Samples the skybox like a D3D cube map (same as the viewport background).</summary>
        private Vector3 SampleSkybox(Texture[] faces, Vector3 d)
        {
            var ax = MathF.Abs(d.X); var ay = MathF.Abs(d.Y); var az = MathF.Abs(d.Z);
            int face; float sc, tc, ma;
            if (ax >= ay && ax >= az) { ma = ax; if (d.X > 0) { face = 0; sc = -d.Z; tc = -d.Y; } else { face = 1; sc = d.Z; tc = -d.Y; } }
            else if (ay >= az) { ma = ay; if (d.Y > 0) { face = 2; sc = d.X; tc = d.Z; } else { face = 3; sc = d.X; tc = -d.Z; } }
            else { ma = az; if (d.Z > 0) { face = 4; sc = d.X; tc = -d.Y; } else { face = 5; sc = -d.X; tc = -d.Y; } }
            var uv = new Vector2((sc / ma + 1f) * 0.5f, (tc / ma + 1f) * 0.5f);
            var tex = faces[face];
            // Clamp inside the face (no wrap across cube edges).
            var x = Math.Clamp((int)(uv.X * tex.Width), 0, tex.Width - 1);
            var y = Math.Clamp((int)(uv.Y * tex.Height), 0, tex.Height - 1);
            var c = tex.Linear[y * tex.Width + x];
            return new Vector3(c.X, c.Y, c.Z);
        }

        private Vector3 Sky(Vector3 direction)
        {
            if (_skyPanorama is { } panorama) return panorama.SampleEquirect(direction, _skyRotation) * (_settings.SkyStrength * _worldStrength);
            if (_skyFaces is { } faces) return SampleSkybox(faces, direction) * (_settings.SkyStrength * _worldStrength);
            var y = direction.Y;
            var color = y >= 0
                ? Vector3.Lerp(_settings.SkyHorizon, _settings.SkyZenith, MathF.Pow(y, 0.6f))
                : Vector3.Lerp(_settings.SkyHorizon, _settings.SkyGround, MathF.Min(1f, -y * 4f));
            return color * _settings.SkyStrength;
        }

        // ============================= BRDF (matches the viewport shader) =============================

        private static Vector3 Brdf(Vector3 n, Vector3 v, Vector3 l, Vector3 albedo, float rough, float metal)
        {
            var h = Vector3.Normalize(v + l);
            var ndl = Math.Clamp(Vector3.Dot(n, l), 0f, 1f);
            var ndv = Math.Clamp(Vector3.Dot(n, v), 0f, 1f) + 1e-4f;
            var ndh = Math.Clamp(Vector3.Dot(n, h), 0f, 1f);
            var a = MathF.Max(rough * rough, 0.02f);
            var f0 = Vector3.Lerp(new Vector3(0.04f), albedo, metal);
            var fresnel = f0 + (Vector3.One - f0) * MathF.Pow(1f - Math.Clamp(Vector3.Dot(h, v), 0f, 1f), 5);
            var spec = DistributionGgx(ndh, a) * GeometrySmith(ndv, ndl, rough) * fresnel / (4f * ndv * ndl + 1e-4f);
            var kd = (Vector3.One - fresnel) * (1f - metal);
            return (kd * albedo / MathF.PI + spec) * ndl;
        }

        /// <summary>Solid-angle pdf of the lobe mixture Trace samples (GGX specular with
        /// probability <paramref name="specularChance"/>, cosine diffuse otherwise).</summary>
        private static float BsdfPdf(Vector3 n, Vector3 v, Vector3 l, float rough, float specularChance)
        {
            var ndl = Vector3.Dot(n, l);
            if (ndl <= 0) return 0f;
            var h = Vector3.Normalize(v + l);
            var ndh = MathF.Max(Vector3.Dot(n, h), 1e-4f);
            var vdh = MathF.Max(Vector3.Dot(v, h), 1e-4f);
            var a = MathF.Max(rough * rough, 0.02f);
            var specular = DistributionGgx(ndh, a) * ndh / (4f * vdh);
            return specularChance * specular + (1f - specularChance) * ndl / MathF.PI;
        }

        private static float DistributionGgx(float ndh, float a)
        {
            var a2 = a * a;
            var d = ndh * ndh * (a2 - 1f) + 1f;
            return a2 / (MathF.PI * d * d + 1e-5f);
        }

        private static float GeometrySmith(float ndv, float ndl, float r)
        {
            var k = (r + 1f) * (r + 1f) / 8f;
            return ndv / (ndv * (1f - k) + k) * (ndl / (ndl * (1f - k) + k));
        }

        private static (Vector3 T, Vector3 B) Basis(Vector3 n)
        {
            var helper = MathF.Abs(n.X) > 0.9f ? Vector3.UnitY : Vector3.UnitX;
            var t = Vector3.Normalize(Vector3.Cross(helper, n));
            return (t, Vector3.Cross(n, t));
        }

        // ============================= colour =============================

        private static float SmoothStep(float edge0, float edge1, float x)
        {
            var t = Math.Clamp((x - edge0) / MathF.Max(1e-6f, edge1 - edge0), 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        private static float Luminance(Vector3 c) => 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;

        private static Vector3 SrgbToLinear(Vector3 c) => new(S2L(c.X), S2L(c.Y), S2L(c.Z));
        private static float S2L(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
        private static Vector3 LinearToSrgb(Vector3 c) => new(L2S(c.X), L2S(c.Y), L2S(c.Z));
        private static float L2S(float c) => c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;

        private static Vector3 Aces(Vector3 x)
        {
            const float a = 2.51f, b = 0.03f, c = 2.43f, d = 0.59f, e = 0.14f;
            var r = x * (a * x + new Vector3(b)) / (x * (c * x + new Vector3(d)) + new Vector3(e));
            return Vector3.Clamp(r, Vector3.Zero, Vector3.One);
        }

        private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);
    }
}
