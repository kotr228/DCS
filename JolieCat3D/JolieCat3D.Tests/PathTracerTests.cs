using System.Numerics;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Materials;
using JolieCat3D.Core.Numerics;
using JolieCat3D.Core.Scene;
using JolieCat3D.Engine.Rendering;
using JolieCat3D.Engine.Viewport;

namespace JolieCat3D.Tests
{
    public sealed class PathTracerTests
    {
        private static (byte B, byte G, byte R) Pixel(byte[] bgra, int width, int x, int y)
        {
            var i = (y * width + x) * 4;
            return (bgra[i], bgra[i + 1], bgra[i + 2]);
        }

        private static ViewportCamera CameraOn(Vector3 min, Vector3 max)
        {
            var camera = new ViewportCamera();
            camera.Frame(min, max, 1f);
            return camera;
        }

        [Fact]
        public void RedCubeUnderTheDefaultSunAndSky()
        {
            var scene = new Scene3D();
            var cube = new Node("Cube") { Mesh = Primitives.CreateCube(2f) };
            cube.Mesh.Material = new Material("Red") { DiffuseColor = new Color4(1, 0, 0) };
            scene.AddRootNode(cube);

            var tracer = new PathTracer(scene, CameraOn(new Vector3(-1), new Vector3(1)), 48, 48);
            for (var i = 0; i < 8; i++) tracer.RenderPass();
            Assert.Equal(8, tracer.SampleCount);

            var image = tracer.ToBgra();
            var center = Pixel(image, 48, 24, 24);
            Assert.True(center.R > center.G + 40 && center.R > center.B + 40, $"cube should be red: {center}");
            var corner = Pixel(image, 48, 1, 1);
            Assert.True(corner.B > 100, $"sky should light the background: {corner}");
        }

        [Fact]
        public void EmissiveSurfaceLightsUpADarkWorld()
        {
            byte Center(float emission)
            {
                var scene = new Scene3D();
                var cube = new Node("Lamp") { Mesh = Primitives.CreateCube(2f) };
                cube.Mesh.Material = new Material("Lamp") { DiffuseColor = new Color4(0.5f, 0.5f, 0.5f), EmissiveColor = new Color4(1, 1, 1), EmissiveStrength = emission };
                scene.AddRootNode(cube);
                var settings = new PathTracerSettings { SkyStrength = 0f, DefaultSunWhenUnlit = false };
                var tracer = new PathTracer(scene, CameraOn(new Vector3(-1), new Vector3(1)), 32, 32, settings);
                for (var i = 0; i < 4; i++) tracer.RenderPass();
                return Pixel(tracer.ToBgra(), 32, 16, 16).G;
            }

            Assert.True(Center(0f) < 10);
            Assert.True(Center(2f) > 150);
        }

        [Fact]
        public void ObjectsCastShadowsFromTheSun()
        {
            var scene = new Scene3D();
            scene.AddRootNode(new Node("Ground") { Mesh = Primitives.CreatePlane(8, 8) });
            scene.AddRootNode(new Node("Blocker") { Mesh = Primitives.CreateCube(1f), LocalPosition = new Vector3(0, 1.5f, 0) });
            // Sun shining 45 degrees down along +X: the shadow lands at x = 0.5..2.5.
            var sunDir = Vector3.Normalize(new Vector3(1, -1, 0));
            var axis = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, sunDir));
            scene.AddRootNode(new Node("Sun")
            {
                Light = new LightData { Type = LightType.Directional, Intensity = 2f },
                LocalRotation = Quaternion.CreateFromAxisAngle(axis, MathF.Acos(Vector3.Dot(Vector3.UnitZ, sunDir))),
            });

            var camera = new ViewportCamera
            {
                Rotation = ViewportCamera.FromYawPitch(0, -MathF.PI / 2 + 1e-3f),
                Target = Vector3.Zero,
                Distance = 10,
                IsOrthographic = true,
                FixedOrthographicHeight = 6,
            };
            var tracer = new PathTracer(scene, camera, 40, 40, new PathTracerSettings { SkyStrength = 0.15f });
            for (var i = 0; i < 16; i++) tracer.RenderPass();
            var image = tracer.ToBgra();

            // Ground at x = +1.5 and x = -1.5 (pixel columns 30 and 10, either orientation):
            // one is in the cube's shadow, the other in full sun.
            int Patch(int cx) { var sum = 0; for (var dy = -2; dy <= 2; dy++) for (var dx = -2; dx <= 2; dx++) sum += Pixel(image, 40, cx + dx, 20 + dy).G; return sum / 25; }
            var a = Patch(30);
            var b = Patch(10);
            Assert.True(Math.Min(a, b) < Math.Max(a, b) * 0.6, $"expected a shadow: {a} vs {b}");
        }

        [Fact]
        public void NormalMapsChangeShading()
        {
            byte Brightness(byte red)
            {
                var png = Path.Combine(Path.GetTempPath(), "jc3d-nm-" + Guid.NewGuid().ToString("N") + ".png");
                try
                {
                    var pixels = new byte[4 * 4 * 4];
                    for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 200; pixels[i + 1] = 128; pixels[i + 2] = red; pixels[i + 3] = 255; }
                    var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(4, 4, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, 16);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using (var stream = File.Create(png)) encoder.Save(stream);

                    var scene = new Scene3D();
                    var plane = new Node("Ground") { Mesh = Primitives.CreatePlane(4, 4) };
                    plane.Mesh.Material = new Material("Bumpy") { NormalTexturePath = png };
                    scene.AddRootNode(plane);
                    var sunDir = Vector3.Normalize(new Vector3(1, -1, 0));
                    var axis = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, sunDir));
                    scene.AddRootNode(new Node("Sun")
                    {
                        Light = new LightData { Type = LightType.Directional, Intensity = 1f },
                        LocalRotation = Quaternion.CreateFromAxisAngle(axis, MathF.Acos(Vector3.Dot(Vector3.UnitZ, sunDir))),
                    });
                    var camera = new ViewportCamera
                    {
                        Rotation = ViewportCamera.FromYawPitch(0, -MathF.PI / 2 + 1e-3f),
                        Distance = 10,
                        IsOrthographic = true,
                        FixedOrthographicHeight = 2,
                    };
                    var tracer = new PathTracer(scene, camera, 16, 16, new PathTracerSettings { SkyStrength = 0f, MaxBounces = 0 });
                    tracer.RenderPass();
                    return Pixel(tracer.ToBgra(), 16, 8, 8).G;
                }
                finally
                {
                    File.Delete(png);
                }
            }

            // Tangent-space normals tilted to +u and to -u light the plane differently.
            var a = Brightness(230);
            var b = Brightness(25);
            Assert.True(Math.Abs(a - b) > 30, $"normal map should change shading: {a} vs {b}");
        }

        [Fact]
        public void DenoiserReducesNoiseOnFlatSurfaces()
        {
            var scene = new Scene3D();
            scene.AddRootNode(new Node("Ground") { Mesh = Primitives.CreatePlane(8, 8) });
            var camera = new ViewportCamera
            {
                Rotation = ViewportCamera.FromYawPitch(0, -MathF.PI / 2 + 1e-3f),
                Distance = 10,
                IsOrthographic = true,
                FixedOrthographicHeight = 4,
            };
            var tracer = new PathTracer(scene, camera, 32, 32, new PathTracerSettings { DefaultSunWhenUnlit = false });
            tracer.RenderPass();
            tracer.RenderPass();

            static float Variance(Vector3[] image)
            {
                var lum = image.Select(c => 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z).ToArray();
                var mean = lum.Average();
                return lum.Select(l => (l - mean) * (l - mean)).Average();
            }

            var raw = Variance(tracer.LinearImage());
            var clean = Variance(tracer.Denoised());
            Assert.True(clean < raw * 0.3f, $"denoised variance {clean} vs raw {raw}");
        }

        [Fact]
        public void SkyboxLightsAndColoursTheWorld()
        {
            var dir = Path.Combine(Path.GetTempPath(), "jc3d-sky-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                // A pure red skybox (all six faces).
                foreach (var suffix in new[] { "_f", "_b", "_l", "_r", "_u", "_d" })
                {
                    var pixels = new byte[4 * 4 * 4];
                    for (var i = 0; i < pixels.Length; i += 4) { pixels[i + 2] = 255; pixels[i + 3] = 255; }
                    var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(4, 4, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, 16);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(dir, "cube" + suffix + ".png"));
                    encoder.Save(stream);
                }
                var scene = new Scene3D();
                scene.Environment = new EnvironmentSettings { SkyboxSource = dir };
                var cube = new Node("Cube") { Mesh = Primitives.CreateCube(2f) };
                cube.Mesh.Material = new Material("White") { DiffuseColor = new Color4(1, 1, 1) };
                scene.AddRootNode(cube);
                var tracer = new PathTracer(scene, CameraOn(new Vector3(-1), new Vector3(1)), 32, 32, new PathTracerSettings { DefaultSunWhenUnlit = false });
                for (var i = 0; i < 4; i++) tracer.RenderPass();
                var image = tracer.ToBgra();
                var corner = Pixel(image, 32, 1, 1);
                Assert.True(corner.R > 200 && corner.G < 40, $"background should be the red sky: {corner}");
                var center = Pixel(image, 32, 16, 16);
                Assert.True(center.R > center.G + 60, $"white cube lit by a red sky turns red: {center}");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }
        }

        [Fact]
        public void LightmapShowsTheShadowOfABlocker()
        {
            var scene = new Scene3D();
            var ground = Primitives.CreatePlane(8, 8);
            scene.AddRootNode(new Node("Ground") { Mesh = ground });
            scene.AddRootNode(new Node("Blocker") { Mesh = Primitives.CreateCube(2f), LocalPosition = new Vector3(0, 2f, 0) });
            scene.AddRootNode(new Node("Sun")
            {
                Light = new LightData { Type = LightType.Directional, Intensity = 2f },
                LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2), // straight down
            });
            var tracer = new PathTracer(scene, new ViewportCamera(), 1, 1, new PathTracerSettings { SkyStrength = 0.2f, MaxBounces = 1 });
            var map = tracer.BakeLightmap(ground, Matrix4x4.Identity, 32, 8);

            // The plane's UVs span 0..1 over its 8x8 size: the centre texel lies under the cube.
            var centre = map.GetPixel(16, 16);
            var edge = map.GetPixel(2, 2);
            Assert.True(edge.R > centre.R + 0.2f, $"shadowed centre {centre.R} vs lit edge {edge.R}");
        }

        [Fact]
        public void HdrLightmapKeepsLightBrighterThanWhite()
        {
            var scene = new Scene3D();
            var ground = Primitives.CreatePlane(4, 4);
            scene.AddRootNode(new Node("Ground") { Mesh = ground });
            scene.AddRootNode(new Node("Sun")
            {
                Light = new LightData { Type = LightType.Directional, Intensity = 3f }, // light colour = intensity for a surface facing the sun
                LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2),
            });
            var tracer = new PathTracer(scene, new ViewportCamera(), 1, 1, new PathTracerSettings { SkyStrength = 0f, MaxBounces = 0 });
            var hdr = tracer.BakeLightmapHdr(ground, Matrix4x4.Identity, 16, 1);
            Assert.InRange(hdr[8, 8].X, 2.5f, 3.5f);

            // Through a Radiance file and back (RGBE keeps ~1% precision).
            using var stream = new MemoryStream();
            hdr.WriteHdr(stream);
            stream.Position = 0;
            var loaded = HdrImage.ReadHdr(stream);
            Assert.Equal(16, loaded.Width);
            Assert.InRange(loaded[8, 8].X, hdr[8, 8].X * 0.98f, hdr[8, 8].X * 1.02f);
        }

        [Fact]
        public void EquirectPanoramaMapsDirectionsAndCubeFaces()
        {
            // 8x4 panorama: top half bright blue sky, bottom half dim ground; one hot texel at the centre (-Z).
            var pano = new HdrImage(8, 4);
            for (var y = 0; y < 4; y++) for (var x = 0; x < 8; x++) pano[x, y] = y < 2 ? new Vector3(0.2f, 0.4f, 2f) : new Vector3(0.1f);
            Assert.True(pano.SampleEquirect(Vector3.UnitY).Z > 1.5f);
            Assert.True(pano.SampleEquirect(-Vector3.UnitY).Z < 0.2f);

            var marked = new HdrImage(64, 32);
            marked[32, 15] = marked[31, 15] = marked[32, 16] = marked[31, 16] = new Vector3(10);
            Assert.True(marked.SampleEquirect(-Vector3.UnitZ).X > 5f, "image centre looks down -Z");
            Assert.True(marked.SampleEquirect(Vector3.UnitZ).X < 0.01f);
            // Rotating the panorama 90 degrees moves the hot spot from -Z to -X.
            Assert.True(marked.SampleEquirect(-Vector3.UnitX, 90f).X > 5f);

            for (var face = 0; face < 6; face++)
            {
                var centre = HdrImage.CubeFaceDirection(face, 0.5f, 0.5f);
                var axis = face switch { 0 => Vector3.UnitX, 1 => -Vector3.UnitX, 2 => Vector3.UnitY, 3 => -Vector3.UnitY, 4 => Vector3.UnitZ, _ => -Vector3.UnitZ };
                Assert.True(Vector3.Dot(centre, axis) > 0.999f);
            }
            var faces = marked.ToCubeFaces(8);
            Assert.True(faces[5].Max(c => c.X) > 0.3f, "-Z face shows the hot spot");
            Assert.True(faces[4].Max(c => c.X) < 0.01f, "+Z face doesn't");
        }

        [Fact]
        public void HdrPanoramaLightsThePathTracedScene()
        {
            var dir = Path.Combine(Path.GetTempPath(), "jc3d-hdri-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                // An all-red world: a white object rendered under it comes out red.
                var pano = new HdrImage(16, 8);
                Array.Fill(pano.Pixels, new Vector3(2f, 0.05f, 0.05f));
                var path = Path.Combine(dir, "red.hdr");
                pano.SaveHdr(path);

                var scene = new Scene3D { Environment = new EnvironmentSettings { SkyboxSource = path, SkyboxRotation = 30 } };
                var cube = new Node("Cube") { Mesh = Primitives.CreateCube(1f) };
                cube.Mesh.Material = new Material("White") { DiffuseColor = new Color4(1, 1, 1) };
                scene.AddRootNode(cube);
                var camera = new ViewportCamera();
                camera.Frame(new Vector3(-1), new Vector3(1), 1f);
                var tracer = new PathTracer(scene, camera, 16, 16, new PathTracerSettings { MaxBounces = 2 });
                for (var i = 0; i < 48; i++) tracer.RenderPass();
                var centre = tracer.LinearImage()[8 * 16 + 8];
                Assert.True(centre.X > centre.Y * 4, $"lit red by the HDRI: {centre}");

                // The rotation is saved with the project.
                var data = JolieCat3D.Service.Project.Jolie3DProjectSerializer.BuildData(scene, new JolieCat3D.Service.Animation.AnimationTimeline());
                var (loaded, _) = JolieCat3D.Service.Project.Jolie3DProjectSerializer.FromData(data);
                Assert.Equal(30f, loaded.Environment!.SkyboxRotation);

                // World strength: saved with the project, and 0 switches the world light off.
                scene.Environment!.SkyboxStrength = 0f;
                var data0 = JolieCat3D.Service.Project.Jolie3DProjectSerializer.BuildData(scene, new JolieCat3D.Service.Animation.AnimationTimeline());
                Assert.Equal(0f, JolieCat3D.Service.Project.Jolie3DProjectSerializer.FromData(data0).Item1.Environment!.SkyboxStrength);
                var dark = new PathTracer(scene, camera, 16, 16, new PathTracerSettings { MaxBounces = 2 });
                for (var i = 0; i < 4; i++) dark.RenderPass();
                Assert.True(dark.LinearImage()[8 * 16 + 8].Length() < 1e-3f, "strength 0 = no world light");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }
        }

        [Fact]
        public void SphericalHarmonicIrradianceMatchesAnalyticCases()
        {
            const int size = 16;
            Vector3[][] Faces(Func<Vector3, Vector3> radiance) => Enumerable.Range(0, 6).Select(f =>
            {
                var face = new Vector3[size * size];
                for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
                    face[y * size + x] = radiance(HdrImage.CubeFaceDirection(f, (x + 0.5f) / size, (y + 0.5f) / size));
                return face;
            }).ToArray();

            // A uniform world of radiance L gives irradiance pi*L from every side.
            var uniform = SphericalHarmonics.ToIrradiance(SphericalHarmonics.ProjectCube(Faces(_ => new Vector3(1, 0.5f, 2)), size));
            foreach (var n in new[] { Vector3.UnitY, -Vector3.UnitX, Vector3.Normalize(new Vector3(1, -1, 1)) })
            {
                var e = SphericalHarmonics.Irradiance(uniform, n);
                Assert.InRange(e.X, MathF.PI * 0.98f, MathF.PI * 1.02f);
                Assert.InRange(e.Z, MathF.PI * 2 * 0.98f, MathF.PI * 2 * 1.02f);
            }

            // Light from the upper hemisphere only: pi at the top (SH9 is close), ~0 underneath.
            var upper = SphericalHarmonics.ToIrradiance(SphericalHarmonics.ProjectCube(Faces(d => d.Y > 0 ? Vector3.One : Vector3.Zero), size));
            var top = SphericalHarmonics.Irradiance(upper, Vector3.UnitY).X;
            var bottom = SphericalHarmonics.Irradiance(upper, -Vector3.UnitY).X;
            var side = SphericalHarmonics.Irradiance(upper, Vector3.UnitX).X;
            Assert.InRange(top, MathF.PI * 0.9f, MathF.PI * 1.1f);
            Assert.True(bottom < 0.2f, $"bottom {bottom}");
            Assert.InRange(side, MathF.PI * 0.45f, MathF.PI * 0.55f);
        }

        [Fact]
        public void ImportanceSampledHdrSunIsMuchLessNoisy()
        {
            var dir = Path.Combine(Path.GetTempPath(), "jc3d-sun-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                // Dim world with a tiny, very bright sun high in the sky.
                var pano = new HdrImage(128, 64);
                Array.Fill(pano.Pixels, new Vector3(0.02f));
                for (var y = 10; y < 12; y++) for (var x = 63; x < 65; x++) pano[x, y] = new Vector3(4000f);
                var path = Path.Combine(dir, "sun.hdr");
                pano.SaveHdr(path);

                float Noise(bool importance)
                {
                    var scene = new Scene3D { Environment = new EnvironmentSettings { SkyboxSource = path } };
                    scene.AddRootNode(new Node("Ground") { Mesh = Primitives.CreatePlane(400, 400) });
                    var camera = new ViewportCamera();
                    camera.Frame(new Vector3(-1, 0, -1), new Vector3(1, 0, 1), 1f);
                    var tracer = new PathTracer(scene, camera, 24, 24, new PathTracerSettings { MaxBounces = 1, ImportanceSampleEnvironment = importance, FireflyClamp = 0, DefaultSunWhenUnlit = false });
                    for (var i = 0; i < 4; i++) tracer.RenderPass();
                    var lum = tracer.LinearImage().Select(c => (c.X + c.Y + c.Z) / 3f).ToArray();
                    var mean = lum.Average();
                    return MathF.Sqrt(lum.Select(l => (l - mean) * (l - mean)).Average()) / MathF.Max(mean, 1e-6f);
                }

                var sampled = Noise(true);
                var blind = Noise(false);
                Assert.True(sampled * 4 < blind, $"relative noise with importance sampling {sampled}, without {blind}");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }
        }

        [Fact]
        public void HdrReaderDecodesRunLengthRows()
        {
            // Hand-made 8x1 new-style RLE row: every pixel (128,64,32 | e=129) = (1, 0.5, 0.25) * ~1.
            var bytes = new List<byte>(System.Text.Encoding.ASCII.GetBytes("#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y 1 +X 8\n"));
            bytes.AddRange(new byte[] { 2, 2, 0, 8 });
            foreach (var value in new byte[] { 128, 64, 32, 129 }) { bytes.Add(128 + 8); bytes.Add(value); }
            var image = HdrImage.ReadHdr(new MemoryStream(bytes.ToArray()));
            Assert.InRange(image[7, 0].X, 0.99f, 1.01f);
            Assert.InRange(image[3, 0].Z, 0.24f, 0.26f);
        }

        [Fact]
        public void DepthOfFieldBlursOutOfFocusEdges()
        {
            // A white cube in front of a black world; measure how soft its silhouette is.
            float EdgeSoftness(float aperture)
            {
                var scene = new Scene3D();
                var cube = new Node("Cube") { Mesh = Primitives.CreateCube(1f) };
                cube.Mesh.Material = new Material("White") { DiffuseColor = new Color4(1, 1, 1), EmissiveColor = new Color4(1, 1, 1), EmissiveStrength = 1 };
                scene.AddRootNode(cube);
                var camera = new ViewportCamera { Rotation = Quaternion.Identity, Target = Vector3.Zero, Distance = 4 };
                var settings = new PathTracerSettings { SkyStrength = 0, DefaultSunWhenUnlit = false, MaxBounces = 0, ApertureRadius = aperture, FocusDistance = 20f };
                var tracer = new PathTracer(scene, camera, 64, 64, settings);
                for (var i = 0; i < 16; i++) tracer.RenderPass();
                var image = tracer.LinearImage();
                // Count partially-covered pixels along the middle row (0 < value < 0.9).
                var partial = 0;
                for (var x = 0; x < 64; x++) { var v = image[32 * 64 + x].X; if (v > 0.05f && v < 0.9f) partial++; }
                return partial;
            }
            var sharp = EdgeSoftness(0f);
            var blurred = EdgeSoftness(0.15f);
            Assert.True(blurred > sharp + 4, $"out-of-focus edge should be wider: sharp {sharp}, blurred {blurred}");
        }

        [Fact]
        public void ApertureFollowsFStop()
        {
            var camera = new CameraData { FieldOfView = 45, FStop = 2 };
            var wide = camera.ApertureRadius();
            camera.FStop = 8;
            Assert.Equal(wide / 4, camera.ApertureRadius(), 5);
            Assert.InRange(wide, 0.005f, 0.02f); // ~29 mm lens at f/2 -> ~7 mm radius
        }

        [Fact]
        public void AreaLightCastsSofterShadowsThanAPointLight()
        {
            // Width of the penumbra under a hovering plate, lit from above by point vs area light.
            int Penumbra(LightType type)
            {
                var scene = new Scene3D();
                scene.AddRootNode(new Node("Ground") { Mesh = Primitives.CreatePlane(8, 8) });
                scene.AddRootNode(new Node("Plate") { Mesh = Primitives.CreateCube(1f), LocalPosition = new Vector3(0, 1f, 0), LocalScale = new Vector3(1, 0.05f, 1) });
                scene.AddRootNode(new Node("Lamp")
                {
                    Light = new LightData { Type = type, Intensity = 3f, Range = 20, Width = 2f, Height = 2f },
                    LocalPosition = new Vector3(0, 3f, 0),
                    LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2), // facing down
                });
                var camera = new ViewportCamera { Rotation = ViewportCamera.FromYawPitch(0, -MathF.PI / 2 + 1e-3f), Distance = 10, IsOrthographic = true, FixedOrthographicHeight = 4 };
                var tracer = new PathTracer(scene, camera, 64, 64, new PathTracerSettings { SkyStrength = 0, MaxBounces = 0 });
                for (var i = 0; i < 24; i++) tracer.RenderPass();
                var image = tracer.LinearImage();
                // Down the middle column, through the shadow: count pixels between dark and lit.
                var column = Enumerable.Range(0, 64).Select(y => image[y * 64 + 32].Y).ToArray();
                var colMax = column.Max();
                return column.Count(v => v > colMax * 0.1f && v < colMax * 0.9f);
            }
            var hard = Penumbra(LightType.Point);
            var soft = Penumbra(LightType.Area);
            Assert.True(soft > hard + 2, $"area light penumbra {soft} vs point {hard}");
        }

        [Fact]
        public void HiddenObjectsAreNotRendered()
        {
            var scene = new Scene3D();
            var cube = new Node("Cube") { Mesh = Primitives.CreateCube(2f), IsHidden = true };
            cube.Mesh.Material = new Material("Red") { DiffuseColor = new Color4(1, 0, 0) };
            scene.AddRootNode(cube);
            var tracer = new PathTracer(scene, CameraOn(new Vector3(-1), new Vector3(1)), 16, 16);
            Assert.Equal(0, tracer.TriangleCount);
        }
    }
}
