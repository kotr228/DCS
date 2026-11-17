using System.Numerics;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Modifiers;
using JolieCat3D.Core.Scene;
using JolieCat3D.Engine.Rendering;
using JolieCat3D.Engine.Viewport;
using JolieCat3D.Service;
using JolieCat3D.Service.Export;

namespace JolieCat3D.Tests
{
    /// <summary>Runs the main tools on a real model (TestAssets/bottle.glb) to catch
    /// problems that synthetic meshes don't show.</summary>
    public sealed class RealAssetSmokeTests
    {
        private static string? FindAsset(string name)
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                var path = Path.Combine(dir.FullName, "TestAssets", name);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        [Fact]
        public void BottleSurvivesTheMainToolChain()
        {
            var asset = FindAsset("bottle.glb");
            if (asset is null) return; // asset not checked out

            var (scene, _) = MeshFileService.ImportSceneWithAnimation(asset);
            var meshes = scene.Traverse().Where(n => n.Mesh is { Vertices.Count: > 0 }).ToList();
            Assert.NotEmpty(meshes);

            foreach (var node in meshes)
            {
                var mesh = node.Mesh!;
                var triangles = mesh.GetRenderFaces().Count();

                var lod = MeshDecimator.Decimate(mesh, 0.5f);
                Assert.InRange(lod.GetRenderFaces().Count(), 1, triangles);
                Assert.All(lod.Vertices, v => Assert.True(float.IsFinite(v.Position.X) && float.IsFinite(v.Normal.Y)));

                var unwrapped = mesh.Clone();
                Assert.True(LscmUnwrapper.SmartProject(unwrapped) > 0);
                Assert.All(unwrapped.Vertices, v => { Assert.InRange(v.UV.X, -1e-3f, 1.001f); Assert.InRange(v.UV.Y, -1e-3f, 1.001f); });

                var bevelled = new BevelModifier { Width = 0.002f, Segments = 1, AngleDegrees = 60 }.Apply(mesh);
                Assert.All(bevelled.Vertices, v => Assert.True(float.IsFinite(v.Position.X)));

                var subdivided = CatmullClarkSubdivider.Subdivide(mesh);
                Assert.True(subdivided.Polygons.Count >= triangles);
            }

            // Final render of the whole model.
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var node in meshes)
            {
                var (a, b) = node.GetWorldBounds();
                min = Vector3.Min(min, a);
                max = Vector3.Max(max, b);
            }
            var camera = new ViewportCamera();
            camera.Frame(min, max, 1f);
            var tracer = new PathTracer(scene, camera, 32, 32, new PathTracerSettings { MaxBounces = 2 });
            tracer.RenderPass();
            tracer.RenderPass();
            Assert.All(tracer.LinearImage(), c => Assert.True(float.IsFinite(c.X) && float.IsFinite(c.Y) && float.IsFinite(c.Z)));
            Assert.True(tracer.TriangleCount > 0);

            // And back out to glTF.
            var output = Path.Combine(Path.GetTempPath(), "jc3d-bottle-" + Guid.NewGuid().ToString("N") + ".glb");
            try
            {
                GltfExporter.Export(scene, null, output);
                var reloaded = MeshFileService.ImportScene(output);
                Assert.Equal(meshes.Sum(n => n.Mesh!.GetRenderFaces().Count()),
                    reloaded.Traverse().Where(n => n.Mesh is not null).Sum(n => n.Mesh!.GetRenderFaces().Count()));
            }
            finally
            {
                File.Delete(output);
            }
        }
    }
}
