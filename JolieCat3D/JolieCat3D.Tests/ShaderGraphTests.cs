using System.Numerics;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Materials;
using JolieCat3D.Core.Materials.Nodes;
using JolieCat3D.Core.Numerics;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.Tests
{
    public sealed class ShaderGraphTests
    {
        [Theory]
        [InlineData(ShaderNodeTypes.Noise, "Fac")]
        [InlineData(ShaderNodeTypes.Voronoi, "Distance")]
        [InlineData(ShaderNodeTypes.Wave, "Fac")]
        public void SeamlessTexturesTileAcrossTheUvSquare(string type, string output)
        {
            var graph = new ShaderGraph();
            var node = graph.AddNode(type);
            node.Inputs["Scale"] = new[] { 4f };
            var f = new ShaderGraphEvaluator(graph).CompileOutput(node, output);
            var varied = false;
            for (var i = 0; i < 20; i++)
            {
                var uv = new Vector2(i * 0.037f, i * 0.051f);
                var a = f(uv).X;
                Assert.Equal(a, f(uv + new Vector2(1, 0)).X, 3);
                Assert.Equal(a, f(uv + new Vector2(0, 1)).X, 3);
                varied |= MathF.Abs(a - f(new Vector2(0.5f, 0.5f)).X) > 0.02f;
            }
            Assert.True(varied, "the texture is not flat");
        }

        [Fact]
        public void CheckerRampMixAndMathEvaluate()
        {
            var graph = new ShaderGraph();
            var checker = graph.AddNode(ShaderNodeTypes.Checker);
            checker.Inputs["Scale"] = new[] { 2f };
            var f = new ShaderGraphEvaluator(graph).CompileOutput(checker, "Fac");
            Assert.Equal(1f, f(new Vector2(0.25f, 0.25f)).X);
            Assert.Equal(0f, f(new Vector2(0.75f, 0.25f)).X);
            Assert.Equal(1f, f(new Vector2(0.75f, 0.75f)).X);

            var ramp = graph.AddNode(ShaderNodeTypes.ColorRamp);
            ramp.Ramp = new List<RampStop> { new() { Position = 0, R = 1, G = 0, B = 0 }, new() { Position = 1, R = 0, G = 0, B = 1 } };
            var value = graph.AddNode(ShaderNodeTypes.Value);
            value.Inputs["Value"] = new[] { 0.25f };
            Assert.True(graph.Connect(value.Id, "Value", ramp.Id, "Fac"));
            var c = new ShaderGraphEvaluator(graph).CompileOutput(ramp, "Color")(Vector2.Zero);
            Assert.Equal(0.75f, c.X, 4);
            Assert.Equal(0.25f, c.Z, 4);

            var math = graph.AddNode(ShaderNodeTypes.Math);
            math.Options["Operation"] = "Power";
            math.Inputs["A"] = new[] { 3f };
            math.Inputs["B"] = new[] { 2f };
            Assert.Equal(9f, new ShaderGraphEvaluator(graph).CompileOutput(math, "Value")(Vector2.Zero).X, 4);

            var mix = graph.AddNode(ShaderNodeTypes.Mix);
            mix.Options["Blend"] = "Multiply";
            mix.Inputs["Fac"] = new[] { 1f };
            mix.Inputs["A"] = new[] { 0.5f, 1f, 1f, 1f };
            mix.Inputs["B"] = new[] { 0.5f, 0.5f, 0f, 1f };
            Assert.Equal(new Vector4(0.25f, 0.5f, 0, 1), new ShaderGraphEvaluator(graph).CompileOutput(mix, "Result")(Vector2.Zero));

            // A link that would close a loop is refused.
            Assert.True(graph.Connect(ramp.Id, "Alpha", math.Id, "A"));
            Assert.False(graph.Connect(math.Id, "Value", value.Id, "Value")); // value -> ramp -> math -> value
            Assert.True(graph.DependsOn(math.Id, value.Id));
            Assert.False(graph.DependsOn(value.Id, math.Id));
        }

        [Fact]
        public void ImageNodeSamplesThroughTheLoader()
        {
            var image = new TextureBuffer(2, 1);
            image.SetPixel(0, 0, new Color4(1, 0, 0));
            image.SetPixel(1, 0, new Color4(0, 0, 1));
            var graph = new ShaderGraph();
            var node = graph.AddNode(ShaderNodeTypes.Image);
            node.Options["Image"] = "tex.png";
            var f = new ShaderGraphEvaluator(graph, path => path == "tex.png" ? image : null).CompileOutput(node, "Color");
            Assert.Equal(1f, f(new Vector2(0.25f, 0.5f)).X, 3);
            Assert.Equal(1f, f(new Vector2(0.75f, 0.5f)).Z, 3);
            var missing = new ShaderGraphEvaluator(graph).CompileOutput(node, "Color")(Vector2.Zero);
            Assert.Equal(new Vector4(1, 0, 1, 1), missing);
        }

        [Fact]
        public void BakeProducesMapsAndAppliesThemToTheMaterial()
        {
            var graph = ShaderGraph.CreateDefault();
            var output = graph.Output!;
            output.Inputs["Metallic"] = new[] { 0.7f };
            output.Inputs["Emission Strength"] = new[] { 3f };
            var noise = graph.AddNode(ShaderNodeTypes.Noise);
            var ramp = graph.AddNode(ShaderNodeTypes.ColorRamp);
            var bump = graph.AddNode(ShaderNodeTypes.Bump);
            Assert.True(graph.Connect(noise.Id, "Fac", ramp.Id, "Fac"));
            Assert.True(graph.Connect(ramp.Id, "Color", output.Id, "Base Color"));
            Assert.True(graph.Connect(noise.Id, "Fac", output.Id, "Roughness"));
            Assert.True(graph.Connect(noise.Id, "Fac", bump.Id, "Height"));
            Assert.True(graph.Connect(bump.Id, "Normal", output.Id, "Normal"));

            var result = ShaderGraphBaker.Bake(graph, 32);
            Assert.NotNull(result.BaseColor);
            Assert.NotNull(result.MetallicRoughness);
            Assert.NotNull(result.Normal);
            Assert.Null(result.Emission);
            Assert.Equal(0.7f, result.MetallicRoughness!.GetPixel(5, 5).B, 2);   // constant metallic packed into B
            Assert.True(result.Normal!.Pixels.Any(p => MathF.Abs(p.R - 0.5f) > 0.02f), "bump tilts the normals");
            Assert.True(result.BaseColor!.Pixels.Select(p => p.R).Distinct().Count() > 10, "noise varies across the map");

            var material = new Material("Rock") { Roughness = 0.2f };
            result.ApplyTo(material, "c.png", "mr.png", "n.png", "e.png");
            Assert.Equal(("c.png", "mr.png", "n.png"), (material.DiffuseTexturePath, material.MetallicRoughnessTexturePath, material.NormalTexturePath));
            Assert.Null(material.EmissiveTexturePath);
            Assert.Equal((1f, 1f, 3f), (material.Roughness, material.Metallic, material.EmissiveStrength));
        }

        [Fact]
        public void NodeTreeSurvivesSavingTheProject()
        {
            var scene = new Scene3D();
            var cube = new Node("Cube") { Mesh = Primitives.CreateCube(1f) };
            var graph = ShaderGraph.CreateDefault();
            var brick = graph.AddNode(ShaderNodeTypes.Brick, 10, 20);
            brick.Inputs["Scale"] = new[] { 6f };
            graph.Connect(brick.Id, "Color", graph.Output!.Id, "Base Color");
            var ramp = graph.AddNode(ShaderNodeTypes.ColorRamp);
            ramp.Options["Interpolation"] = "Constant";
            graph.BakePathPrefix = "C:/tex/Brick";
            cube.Mesh.Material = new Material("Brick") { NodeGraph = graph };
            scene.AddRootNode(cube);

            var data = JolieCat3D.Service.Project.Jolie3DProjectSerializer.BuildData(scene, new JolieCat3D.Service.Animation.AnimationTimeline());
            var json = System.Text.Json.JsonSerializer.Serialize(data);
            var back = System.Text.Json.JsonSerializer.Deserialize<JolieCat3D.Service.Project.ProjectFileData>(json)!;
            var (loaded, _) = JolieCat3D.Service.Project.Jolie3DProjectSerializer.FromData(back);
            var loadedGraph = loaded.Traverse().First(n => n.Name == "Cube").Mesh!.Material!.NodeGraph!;
            Assert.Equal(3, loadedGraph.Nodes.Count);
            Assert.Single(loadedGraph.Links);
            Assert.Equal(6f, loadedGraph.Find(brick.Id)!.InputValue("Scale").X);
            Assert.Equal("Constant", loadedGraph.Find(ramp.Id)!.Option("Interpolation"));
            Assert.Equal(2, loadedGraph.Find(ramp.Id)!.Ramp!.Count);
            Assert.Equal("C:/tex/Brick", loadedGraph.BakePathPrefix);
            Assert.Equal((10f, 20f), (loadedGraph.Find(brick.Id)!.X, loadedGraph.Find(brick.Id)!.Y));
        }
    }
}
