using System.Numerics;
using System.Windows;
using JolieCat3D.Core.Camera;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Materials;
using JolieCat3D.Core.Numerics;
using JolieCat3D.Core.Scene;
using JolieCat3D.Engine.Gizmos;
using JolieCat3D.Engine.Rendering;
using JolieCat3D.Engine.Viewport;

namespace JolieCat3D.Tests
{
    public sealed class ViewportCameraTests
    {
        private static readonly Size View = new(800, 600);

        [Fact]
        public void WorldToScreenAndScreenToRayRoundTrip()
        {
            var camera = new ViewportCamera { Target = new Vector3(1, 2, 3), Distance = 10 };
            var world = new Vector3(1.5f, 2.2f, 2.5f);
            var screen = camera.WorldToScreen(world, View)!.Value;
            var ray = camera.ScreenToRay(screen, View);

            // The point must lie on the ray through its own projection.
            var toPoint = world - ray.Origin;
            var along = Vector3.Dot(toPoint, ray.Direction);
            var distance = (toPoint - ray.Direction * along).Length();
            Assert.True(distance < 1e-3f, $"distance {distance}");
        }

        [Fact]
        public void TargetProjectsToViewportCenter()
        {
            var camera = new ViewportCamera { Target = new Vector3(-4, 1, 7), Distance = 5 };
            var screen = camera.WorldToScreen(camera.Target, View)!.Value;
            Assert.Equal(400, screen.X, 1);
            Assert.Equal(300, screen.Y, 1);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FrameKeepsBoundsInsideTheView(bool ortho)
        {
            var camera = new ViewportCamera { IsOrthographic = ortho };
            var min = new Vector3(-3, 0, -1);
            var max = new Vector3(5, 4, 2);
            camera.Frame(min, max, (float)(View.Width / View.Height));
            for (var i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z);
                var p = camera.WorldToScreen(corner, View)!.Value;
                Assert.InRange(p.X, -1, View.Width + 1);
                Assert.InRange(p.Y, -1, View.Height + 1);
            }
        }

        [Fact]
        public void FrontPresetMatchesViewPresetMath()
        {
            var camera = new ViewportCamera();
            camera.AlignToPreset(ViewPreset.Front);
            var (direction, up) = ViewPresetMath.GetOrientation(ViewPreset.Front);
            Assert.True(Vector3.Dot(camera.Forward, Vector3.Normalize(direction)) > 0.999f);
            Assert.True(Vector3.Dot(camera.Up, Vector3.Normalize(up)) > 0.999f);
        }

        [Fact]
        public void PanMovesTheTargetButKeepsOrientation()
        {
            var camera = new ViewportCamera();
            var rotation = camera.Rotation;
            var target = camera.Target;
            camera.Pan(100, 0, 600);
            Assert.Equal(rotation, camera.Rotation);
            // Dragging right moves the scene right, i.e. the target moves left.
            Assert.True(Vector3.Dot(camera.Target - target, camera.Right) < 0);
        }

        [Fact]
        public void ZoomTowardsCursorMovesTargetTowardsThatPoint()
        {
            var camera = new ViewportCamera { Target = Vector3.Zero, Distance = 10 };
            var ray = camera.ScreenToRay(new Point(700, 300), View);
            var before = camera.Target;
            camera.Zoom(0.5f, ray);
            Assert.Equal(5, camera.Distance, 3);
            Assert.True(Vector3.Dot(camera.Target - before, camera.Right) > 0);
        }

        [Fact]
        public void SceneCameraRoundTripsThroughPiloting()
        {
            var node = new Node("Cam")
            {
                Camera = new CameraData { FieldOfView = 60 },
                LocalPosition = new Vector3(1, 2, 3),
                LocalRotation = Quaternion.CreateFromYawPitchRoll(0.3f, -0.2f, 0),
            };
            var camera = new ViewportCamera();
            camera.MatchSceneCamera(node);
            Assert.True(Vector3.Distance(camera.Position, node.GetWorldPosition()) < 1e-3f);

            camera.Orbit(0.2f, 0);
            camera.WriteToSceneCamera(node);
            Assert.True(Vector3.Distance(camera.Position, node.GetWorldPosition()) < 1e-3f);
            Assert.True(Vector3.Dot(camera.Forward, node.GetWorldForward()) > 0.9999f);
        }
    }

    public sealed class EditSessionTests
    {
        [Fact]
        public void SelectionSurvivesTheMeshShrinkingUnderneathIt()
        {
            var node = new Node("N") { Mesh = Primitives.CreateCube() };
            var session = new JolieCat3D.Engine.Editing.MeshEditSession();
            session.Attach(node);
            var original = node.Mesh;
            var bigger = original.Clone();
            MeshOperators.InsetFaces(bigger, Enumerable.Range(0, bigger.Vertices.Count).ToHashSet(), 0.3f);
            node.Mesh = bigger;
            session.SetSelection(Enumerable.Range(bigger.Vertices.Count - 4, 4));

            node.Mesh = original; // what undo does
            Assert.Null(session.GetSelectionWorldCentroid());
            Assert.False(session.HasSelection);
        }
    }

    public sealed class GizmoMathTests
    {
        [Fact]
        public void AxisParameterFindsClosestPointOnAxis()
        {
            var ray = new Ray(new Vector3(3, 5, 0), -Vector3.UnitY);
            var t = GizmoHandles.AxisParameter(ray, Vector3.Zero, Vector3.UnitX);
            Assert.Equal(3f, t!.Value, 4);
        }

        [Fact]
        public void AxisParameterIsNullWhenLookingDownTheAxis()
        {
            var ray = new Ray(new Vector3(0, 0, 5), -Vector3.UnitZ);
            Assert.Null(GizmoHandles.AxisParameter(ray, Vector3.Zero, Vector3.UnitZ));
        }

        [Fact]
        public void DistanceToSegmentClampsToEnds()
        {
            Assert.Equal(5, GizmoHandles.DistanceToSegment(new Point(-3, 4), new Point(0, 0), new Point(10, 0)), 5);
            Assert.Equal(2, GizmoHandles.DistanceToSegment(new Point(5, 2), new Point(0, 0), new Point(10, 0)), 5);
        }
    }

    public sealed class MirrorEditTests
    {
        [Fact]
        public void XMirrorMovesTheOppositeVertexAndKeepsCentreOnPlane()
        {
            var mesh = new Mesh("M");
            var right = mesh.AddVertex(new Vertex(new Vector3(1, 0, 0), Vector3.UnitY, Vector2.Zero));
            var left = mesh.AddVertex(new Vertex(new Vector3(-1, 0, 0), Vector3.UnitY, Vector2.Zero));
            var centre = mesh.AddVertex(new Vertex(new Vector3(0, 0, 1), Vector3.UnitY, Vector2.Zero));
            mesh.AddTriangle(right, left, centre);
            var node = new Node("N") { Mesh = mesh };
            var session = new JolieCat3D.Engine.Editing.MeshEditSession { MirrorX = true };
            session.Attach(node);
            session.SetSelection(new[] { right, centre });

            session.BeginProportionalDrag();
            Assert.Contains(left, session.AffectedVertexIndices); // part of undo/cancel capture
            session.ApplyTranslation(new Vector3(0.5f, 0.2f, 0));
            session.EndProportionalDrag();

            Assert.Equal(new Vector3(1.5f, 0.2f, 0), mesh.Vertices[right].Position);
            Assert.Equal(new Vector3(-1.5f, 0.2f, 0), mesh.Vertices[left].Position);
            Assert.Equal(new Vector3(0, 0.2f, 1), mesh.Vertices[centre].Position);
        }
    }

    /// <summary>Runs the real DirectX pipeline (hardware, or WARP without a GPU) offscreen.</summary>
    public sealed class RendererSmokeTests
    {
        [Fact]
        public void RendersVisibleObjectOverDarkBackground()
        {
            RunSta(() =>
            {
                var scene = new Scene3D();
                var cube = new Node("Cube") { Mesh = Primitives.CreateCube(2f) };
                cube.Mesh.Material = new Material("Red") { DiffuseColor = new Color4(1, 0, 0) };
                scene.AddRootNode(cube);
                var camera = new ViewportCamera();
                camera.Frame(new Vector3(-1), new Vector3(1), 1f);

                using var renderer = new OffscreenRenderer();
                renderer.Settings.ShowGrid = false;
                var bitmap = renderer.Render(scene, camera, 64, 64);
                var pixels = new byte[64 * 64 * 4];
                bitmap.CopyPixels(pixels, 64 * 4, 0);

                var center = (32 * 64 + 32) * 4;
                // BGRA: centre is the red cube, the corner is the dark background.
                Assert.True(pixels[center + 2] > pixels[center + 1] + 40, "cube should be red");
                Assert.True(pixels[2] < 80 && pixels[1] < 80, "background should be dark");
            });
        }

        [Fact]
        public void WireframeOverlayDrawsEdgesOverTheSurface()
        {
            RunSta(() =>
            {
                int DarkPixels(bool overlay)
                {
                    var scene = new Scene3D();
                    var sphere = new Node("S") { Mesh = Primitives.CreateSphere(1f, 12, 16) };
                    sphere.Mesh.Material = new Material("W") { DiffuseColor = new Color4(1, 1, 1) };
                    scene.AddRootNode(sphere);
                    var camera = new ViewportCamera();
                    camera.Frame(new Vector3(-1), new Vector3(1), 1f);
                    using var renderer = new OffscreenRenderer();
                    renderer.Settings.ShowGrid = false;
                    renderer.Settings.WireframeOverlay = overlay;
                    var image = renderer.Render(scene, camera, 96, 96);
                    var data = new byte[96 * 96 * 4];
                    image.CopyPixels(data, 96 * 4, 0);
                    // Count dark pixels in the middle of the sphere.
                    var dark = 0;
                    for (var y = 30; y < 66; y++) for (var x = 30; x < 66; x++) if (data[(y * 96 + x) * 4 + 1] < 70) dark++;
                    return dark;
                }
                Assert.True(DarkPixels(true) > DarkPixels(false) + 30);
            });
        }

        [Fact]
        public void HiddenObjectsAreNotDrawn()
        {
            RunSta(() =>
            {
                byte CenterRed(bool hidden)
                {
                    var scene = new Scene3D();
                    var cube = new Node("Cube") { Mesh = Primitives.CreateCube(2f), IsHidden = hidden };
                    cube.Mesh.Material = new Material("Red") { DiffuseColor = new Color4(1, 0, 0) };
                    scene.AddRootNode(cube);
                    var camera = new ViewportCamera();
                    camera.Frame(new Vector3(-1), new Vector3(1), 1f);
                    using var renderer = new OffscreenRenderer();
                    renderer.Settings.ShowGrid = false;
                    var image = renderer.Render(scene, camera, 64, 64);
                    var data = new byte[64 * 64 * 4];
                    image.CopyPixels(data, 64 * 4, 0);
                    return data[(32 * 64 + 32) * 4 + 2];
                }
                Assert.True(CenterRed(false) > CenterRed(true) + 60);
            });
        }

        [Fact]
        public void AlphaClipCutsOutTransparentTexels()
        {
            RunSta(() =>
            {
                var png = Path.Combine(Path.GetTempPath(), "jc3d-clip-" + Guid.NewGuid().ToString("N") + ".png");
                try
                {
                    // Fully transparent red texture.
                    var pixels = new byte[4 * 4 * 4];
                    for (var i = 0; i < pixels.Length; i += 4) { pixels[i + 2] = 255; pixels[i + 3] = 0; }
                    var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(4, 4, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, 16);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using (var stream = File.Create(png)) encoder.Save(stream);

                    byte CenterRed(MaterialAlphaMode mode)
                    {
                        var scene = new Scene3D();
                        var cube = new Node("Cube") { Mesh = Primitives.CreateCube(2f) };
                        cube.Mesh.Material = new Material("Leaf") { DiffuseTexturePath = png, AlphaMode = mode, AlphaCutoff = 0.5f };
                        scene.AddRootNode(cube);
                        var camera = new ViewportCamera();
                        camera.Frame(new Vector3(-1), new Vector3(1), 1f);
                        using var renderer = new OffscreenRenderer();
                        renderer.Settings.ShowGrid = false;
                        renderer.Settings.ShadingMode = ShadingMode.Material;
                        var image = renderer.Render(scene, camera, 64, 64);
                        var data = new byte[64 * 64 * 4];
                        image.CopyPixels(data, 64 * 4, 0);
                        return data[(32 * 64 + 32) * 4 + 2];
                    }

                    var opaque = CenterRed(MaterialAlphaMode.Opaque);
                    var clipped = CenterRed(MaterialAlphaMode.Clip);
                    Assert.True(opaque > clipped + 50, $"opaque cube should show the red texture ({opaque} vs {clipped})");
                    Assert.True(clipped < 80, $"clipped cube should vanish ({clipped})");
                }
                finally
                {
                    File.Delete(png);
                }
            });
        }

        [Fact]
        public void EmissiveMaterialGlowsWithoutLight()
        {
            RunSta(() =>
            {
                byte CenterGreen(float strength)
                {
                    var scene = new Scene3D();
                    var cube = new Node("Cube") { Mesh = Primitives.CreateCube(2f) };
                    cube.Mesh.Material = new Material("Glow") { DiffuseColor = new Color4(0, 0, 0), EmissiveColor = new Color4(0, 1, 0), EmissiveStrength = strength };
                    scene.AddRootNode(cube);
                    var camera = new ViewportCamera();
                    camera.Frame(new Vector3(-1), new Vector3(1), 1f);
                    using var renderer = new OffscreenRenderer();
                    renderer.Settings.ShowGrid = false;
                    renderer.Settings.ShadingMode = ShadingMode.Material;
                    var bitmap = renderer.Render(scene, camera, 64, 64);
                    var pixels = new byte[64 * 64 * 4];
                    bitmap.CopyPixels(pixels, 64 * 4, 0);
                    return pixels[(32 * 64 + 32) * 4 + 1];
                }

                var off = CenterGreen(0f);
                var on = CenterGreen(2f);
                Assert.True(on > off + 100, $"emissive green {on} vs black {off}");
            });
        }

        [Fact]
        public void HdriWorldLightsMaterialPreview()
        {
            RunSta(() =>
            {
                var dir = Path.Combine(Path.GetTempPath(), "jc3d-ibl-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                try
                {
                    // Green world above, black below: a white sphere is lit green from the top.
                    var pano = new HdrImage(64, 32);
                    for (var y = 0; y < 16; y++) for (var x = 0; x < 64; x++) pano[x, y] = new Vector3(0.05f, 1.5f, 0.05f);
                    var path = Path.Combine(dir, "green.hdr");
                    pano.SaveHdr(path);

                    (byte R, byte G) Sample(string? sky, int px, int py)
                    {
                        var scene = new Scene3D { Environment = new EnvironmentSettings { SkyboxSource = sky } };
                        var sphere = new Node("S") { Mesh = Primitives.CreateSphere() };
                        sphere.Mesh.Material = new Material("White") { DiffuseColor = new Color4(1, 1, 1), Roughness = 0.8f };
                        scene.AddRootNode(sphere);
                        var camera = new ViewportCamera();
                        camera.Frame(new Vector3(-1), new Vector3(1), 1f);
                        using var renderer = new OffscreenRenderer();
                        renderer.Settings.ShowGrid = false;
                        renderer.Settings.ShadingMode = ShadingMode.Material;
                        var bitmap = renderer.Render(scene, camera, 64, 64);
                        var pixels = new byte[64 * 64 * 4];
                        bitmap.CopyPixels(pixels, 64 * 4, 0);
                        var o = (py * 64 + px) * 4;
                        return (pixels[o + 2], pixels[o + 1]);
                    }

                    var studio = Sample(null, 32, 32);
                    var lit = Sample(path, 32, 32);
                    Assert.True(Math.Abs(studio.G - studio.R) < 20, $"studio light is neutral: {studio}");
                    Assert.True(lit.G > lit.R + 60, $"world light tints the surface: {lit}");
                }
                finally
                {
                    try { Directory.Delete(dir, true); } catch (IOException) { }
                }
            });
        }

        [Theory]
        [InlineData(ShadingMode.Wireframe)]
        [InlineData(ShadingMode.Solid)]
        [InlineData(ShadingMode.Material)]
        [InlineData(ShadingMode.Rendered)]
        public void EveryShadingModeRenders(ShadingMode mode)
        {
            RunSta(() =>
            {
                var scene = new Scene3D();
                var sphere = new Node("S") { Mesh = Primitives.CreateSphere() };
                scene.AddRootNode(sphere);
                scene.AddRootNode(new Node("L") { Light = new LightData { Type = LightType.Point, Range = 5 }, LocalPosition = new Vector3(1, 2, 1) });
                scene.AddRootNode(new Node("C") { Camera = new CameraData(), LocalPosition = new Vector3(0, 0, 4) });

                using var renderer = new OffscreenRenderer();
                renderer.Settings.ShadingMode = mode;
                renderer.Settings.Bloom = true;
                var overlay = new OverlayDrawList();
                overlay.AddLine(Vector3.Zero, Vector3.UnitX, new Color4(1, 0, 0), 2, OverlayLayer.OnTop);
                overlay.AddPoint(Vector3.Zero, new Color4(1, 1, 0), 6);
                overlay.AddCone(Vector3.UnitX, Vector3.UnitX * 1.2f, 0.05f, new Color4(1, 0, 0));

                var bitmap = renderer.Render(scene, new ViewportCamera(), 96, 72, selected: sphere, overlay: overlay);
                Assert.Equal(96, bitmap.PixelWidth);
                Assert.Equal(72, bitmap.PixelHeight);
            });
        }

        private static void RunSta(Action action)
        {
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { error = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error is not null) throw new Exception(error.Message, error);
        }
    }
}
