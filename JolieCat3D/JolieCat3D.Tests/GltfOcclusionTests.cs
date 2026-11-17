using JolieCat3D.Core.Numerics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Materials;
using JolieCat3D.Core.Scene;
using JolieCat3D.Service;
using JolieCat3D.Service.Animation;
using JolieCat3D.Service.Export;

namespace JolieCat3D.Tests
{
    public sealed class GltfOcclusionTests
    {
        private static void WritePng(string path, byte gray)
        {
            var pixels = Enumerable.Repeat(gray, 4 * 4 * 4).ToArray();
            for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            var bitmap = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, pixels, 16);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }

        [Theory]
        [InlineData(MaterialAlphaMode.Clip)]
        [InlineData(MaterialAlphaMode.Blend)]
        [InlineData(MaterialAlphaMode.Opaque)]
        public void AlphaModeAndDoubleSidedRoundTrip(MaterialAlphaMode mode)
        {
            var dir = Path.Combine(Path.GetTempPath(), "jc3d-alpha-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var scene = new Scene3D();
                var mesh = Primitives.CreatePlane();
                mesh.Material = new Material("Leaf") { AlphaMode = mode, AlphaCutoff = 0.3f, DoubleSided = true };
                scene.AddRootNode(new Node("Leaf") { Mesh = mesh });

                var glb = Path.Combine(dir, "leaf.glb");
                GltfExporter.Export(scene, new AnimationTimeline(), glb);
                var fromGltf = MeshFileService.ImportScene(glb).Traverse().Select(n => n.Mesh?.Material).First(m => m is not null)!;
                Assert.Equal(mode, fromGltf.AlphaMode);
                Assert.True(fromGltf.DoubleSided);
                if (mode == MaterialAlphaMode.Clip) Assert.Equal(0.3f, fromGltf.AlphaCutoff, 3);

                var data = JolieCat3D.Service.Project.Jolie3DProjectSerializer.BuildData(scene, new AnimationTimeline());
                var json = System.Text.Json.JsonSerializer.Serialize(data);
                var (loaded, _) = JolieCat3D.Service.Project.Jolie3DProjectSerializer.FromData(System.Text.Json.JsonSerializer.Deserialize<JolieCat3D.Service.Project.ProjectFileData>(json)!);
                var fromProject = loaded.RootNodes[0].Mesh!.Material!;
                Assert.Equal(mode, fromProject.AlphaMode);
                Assert.Equal(0.3f, fromProject.AlphaCutoff);
                Assert.True(fromProject.DoubleSided);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }
        }

        [Fact]
        public void ExportOptionsFilterSelectionAndHiddenObjects()
        {
            var dir = Path.Combine(Path.GetTempPath(), "jc3d-opts-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var scene = new Scene3D();
                var group = new Node("Group") { LocalPosition = new System.Numerics.Vector3(10, 0, 0) };
                var wanted = new Node("Wanted") { Mesh = Primitives.CreateCube(), LocalPosition = new System.Numerics.Vector3(1, 0, 0) };
                var other = new Node("Other") { Mesh = Primitives.CreateCube() };
                var collider = new Node("UCX_Wanted") { Mesh = Primitives.CreateCube(), IsHidden = true };
                group.AddChild(wanted);
                wanted.AddChild(collider);
                scene.AddRootNode(group);
                scene.AddRootNode(other);

                var path = Path.Combine(dir, "sel.glb");
                GltfExporter.Export(scene, null, path, new GltfExportOptions { SelectedRoots = new[] { wanted }, IncludeHidden = false });
                var loaded = MeshFileService.ImportScene(path);
                var names = loaded.Traverse().Select(n => n.Name).ToList();
                Assert.Contains("Wanted", names);
                Assert.DoesNotContain("Other", names);
                Assert.DoesNotContain("UCX_Wanted", names);
                // The unselected parent is kept as a plain transform, so the position is right.
                var exported = loaded.Traverse().First(n => n.Name == "Wanted");
                Assert.Equal(11f, exported.GetWorldPosition().X, 3);
                Assert.Null(loaded.Traverse().First(n => n.Name == "Group").Mesh);

                var all = Path.Combine(dir, "all.glb");
                GltfExporter.Export(scene, null, all, new GltfExportOptions());
                Assert.Contains("UCX_Wanted", MeshFileService.ImportScene(all).Traverse().Select(n => n.Name));
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }
        }

        [Fact]
        public void EmissionRoundTripsThroughGlbAndProjectFiles()
        {
            var dir = Path.Combine(Path.GetTempPath(), "jc3d-em-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var mapPath = Path.Combine(dir, "glow.png");
                WritePng(mapPath, 200);
                var scene = new Scene3D();
                var mesh = Primitives.CreateCube();
                mesh.Material = new Material("Neon") { EmissiveColor = new Color4(1f, 0.5f, 0f), EmissiveStrength = 4f, EmissiveTexturePath = mapPath };
                scene.AddRootNode(new Node("Sign") { Mesh = mesh });

                var glb = Path.Combine(dir, "sign.glb");
                GltfExporter.Export(scene, new AnimationTimeline(), glb);
                var fromGltf = MeshFileService.ImportScene(glb).Traverse().Select(n => n.Mesh?.Material).First(m => m is not null)!;
                Assert.Equal(1f, fromGltf.EmissiveColor.R, 3);
                Assert.Equal(0.5f, fromGltf.EmissiveColor.G, 3);
                Assert.Equal(4f, fromGltf.EmissiveStrength, 3);
                Assert.True(File.Exists(fromGltf.EmissiveTexturePath));

                var data = JolieCat3D.Service.Project.Jolie3DProjectSerializer.BuildData(scene, new AnimationTimeline());
                var json = System.Text.Json.JsonSerializer.Serialize(data);
                var (loaded, _) = JolieCat3D.Service.Project.Jolie3DProjectSerializer.FromData(System.Text.Json.JsonSerializer.Deserialize<JolieCat3D.Service.Project.ProjectFileData>(json)!);
                var fromProject = loaded.RootNodes[0].Mesh!.Material!;
                Assert.Equal(4f, fromProject.EmissiveStrength);
                Assert.Equal(0.5f, fromProject.EmissiveColor.G);
                Assert.Equal(mapPath, fromProject.EmissiveTexturePath);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }
        }

        [Fact]
        public void OcclusionTextureRoundTripsThroughGlb()
        {
            var dir = Path.Combine(Path.GetTempPath(), "jc3d-ao-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var aoPath = Path.Combine(dir, "ao.png");
                WritePng(aoPath, 128);
                var scene = new Scene3D();
                var mesh = Primitives.CreateCube();
                mesh.Material = new Material("Mat") { OcclusionTexturePath = aoPath };
                scene.AddRootNode(new Node("Box") { Mesh = mesh });

                var glb = Path.Combine(dir, "box.glb");
                GltfExporter.Export(scene, new AnimationTimeline(), glb);
                var loaded = MeshFileService.ImportScene(glb);

                var material = loaded.Traverse().Select(n => n.Mesh?.Material).First(m => m is not null)!;
                Assert.False(string.IsNullOrEmpty(material.OcclusionTexturePath));
                Assert.True(File.Exists(material.OcclusionTexturePath));
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }
        }
    }
}
