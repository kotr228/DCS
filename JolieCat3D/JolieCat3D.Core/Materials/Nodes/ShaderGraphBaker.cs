using System.Numerics;
using JolieCat3D.Core.Numerics;

namespace JolieCat3D.Core.Materials.Nodes
{
    /// <summary>The maps a <see cref="ShaderGraph"/> bakes to. A null map means that output
    /// is a constant and goes into the material's plain value instead.</summary>
    public sealed class ShaderBakeResult
    {
        /// <summary>Base colour (sRGB) with alpha.</summary>
        public TextureBuffer? BaseColor { get; init; }

        /// <summary>glTF layout: G = roughness, B = metallic (R = 1).</summary>
        public TextureBuffer? MetallicRoughness { get; init; }

        /// <summary>Tangent-space normal map.</summary>
        public TextureBuffer? Normal { get; init; }

        public TextureBuffer? Emission { get; init; }

        public Color4 BaseColorValue { get; init; }
        public float AlphaValue { get; init; } = 1;
        public bool AlphaLinked { get; init; }
        public float RoughnessValue { get; init; }
        public float MetallicValue { get; init; }
        public Color4 EmissionValue { get; init; }
        public float EmissionStrength { get; init; }

        /// <summary>Points <paramref name="material"/> at the saved maps (pass the paths the
        /// non-null maps were written to) and sets the constant values.</summary>
        public void ApplyTo(Material material, string? baseColorPath, string? metallicRoughnessPath, string? normalPath, string? emissionPath)
        {
            ArgumentNullException.ThrowIfNull(material);
            if (BaseColor is not null && baseColorPath is not null)
            {
                material.DiffuseColor = Color4.White;
                material.DiffuseTexturePath = baseColorPath;
                material.DiffuseTextureOffset = Vector2.Zero;
                material.DiffuseTextureScale = Vector2.One;
                material.PaintedTextureBuffer = null;
            }
            else
            {
                material.DiffuseColor = BaseColorValue;
                material.DiffuseTexturePath = null;
            }
            material.Opacity = AlphaLinked ? 1f : AlphaValue;
            if (AlphaLinked && material.AlphaMode == MaterialAlphaMode.Opaque) material.AlphaMode = MaterialAlphaMode.Blend;

            if (MetallicRoughness is not null && metallicRoughnessPath is not null)
            {
                material.Roughness = 1f;
                material.Metallic = 1f;
                material.MetallicRoughnessTexturePath = metallicRoughnessPath;
            }
            else
            {
                material.Roughness = RoughnessValue;
                material.Metallic = MetallicValue;
                material.MetallicRoughnessTexturePath = null;
            }

            material.NormalTexturePath = Normal is not null ? normalPath : null;

            if (Emission is not null && emissionPath is not null)
            {
                material.EmissiveColor = Color4.White;
                material.EmissiveTexturePath = emissionPath;
            }
            else
            {
                material.EmissiveColor = EmissionValue;
                material.EmissiveTexturePath = null;
            }
            material.EmissiveStrength = EmissionStrength;
        }
    }

    /// <summary>Evaluates a graph's Material Output over the UV square into texture maps.</summary>
    public static class ShaderGraphBaker
    {
        public static ShaderBakeResult Bake(ShaderGraph graph, int size, Func<string, TextureBuffer?>? imageLoader = null,
            IProgress<float>? progress = null, CancellationToken cancel = default)
        {
            ArgumentNullException.ThrowIfNull(graph);
            size = Math.Clamp(size, 4, 8192);
            var output = graph.Output ?? throw new InvalidOperationException("The node tree has no Material Output node.");
            var eval = new ShaderGraphEvaluator(graph, imageLoader, size);

            Func<Vector2, Vector4>? Linked(string input) => eval.IsLinked(output, input) ? eval.CompileInput(output, input) : null;
            var baseColor = Linked("Base Color");
            var alpha = Linked("Alpha");
            var rough = Linked("Roughness");
            var metal = Linked("Metallic");
            var normal = Linked("Normal");
            var emission = Linked("Emission");
            var baseConst = output.InputValue("Base Color");
            var alphaConst = ShaderGraphEvaluator.ToFloat(output.InputValue("Alpha"));
            var roughConst = ShaderGraphEvaluator.ToFloat(output.InputValue("Roughness"));
            var metalConst = ShaderGraphEvaluator.ToFloat(output.InputValue("Metallic"));
            var emissionConst = output.InputValue("Emission");

            TextureBuffer? Make(bool needed) => needed ? new TextureBuffer(size, size) : null;
            var colorMap = Make(baseColor is not null || alpha is not null);
            var mrMap = Make(rough is not null || metal is not null);
            var normalMap = Make(normal is not null);
            var emissionMap = Make(emission is not null);

            var done = 0;
            Parallel.For(0, size, new ParallelOptions { CancellationToken = cancel }, y =>
            {
                for (var x = 0; x < size; x++)
                {
                    var uv = new Vector2((x + 0.5f) / size, (y + 0.5f) / size);
                    if (colorMap is not null)
                    {
                        var c = baseColor?.Invoke(uv) ?? baseConst;
                        var a = alpha is not null ? ShaderGraphEvaluator.ToFloat(alpha(uv)) : alphaConst;
                        colorMap.SetPixel(x, y, Clamp(new Vector4(c.X, c.Y, c.Z, a)));
                    }
                    if (mrMap is not null)
                    {
                        var r = rough is not null ? ShaderGraphEvaluator.ToFloat(rough(uv)) : roughConst;
                        var m = metal is not null ? ShaderGraphEvaluator.ToFloat(metal(uv)) : metalConst;
                        mrMap.SetPixel(x, y, Clamp(new Vector4(1, r, m, 1)));
                    }
                    if (normalMap is not null)
                    {
                        var n = normal!(uv);
                        normalMap.SetPixel(x, y, Clamp(new Vector4(n.X, n.Y, n.Z, 1)));
                    }
                    if (emissionMap is not null)
                    {
                        var e = emission!(uv);
                        emissionMap.SetPixel(x, y, Clamp(new Vector4(e.X, e.Y, e.Z, 1)));
                    }
                }
                var count = Interlocked.Increment(ref done);
                if ((count & 15) == 0) progress?.Report((float)count / size);
            });
            progress?.Report(1f);

            return new ShaderBakeResult
            {
                BaseColor = colorMap,
                MetallicRoughness = mrMap,
                Normal = normalMap,
                Emission = emissionMap,
                BaseColorValue = Clamp(baseConst with { W = 1 }),
                AlphaValue = Math.Clamp(alphaConst, 0, 1),
                AlphaLinked = alpha is not null,
                RoughnessValue = Math.Clamp(roughConst, 0, 1),
                MetallicValue = Math.Clamp(metalConst, 0, 1),
                EmissionValue = Clamp(emissionConst with { W = 1 }),
                EmissionStrength = MathF.Max(0, ShaderGraphEvaluator.ToFloat(output.InputValue("Emission Strength"))),
            };
        }

        /// <summary>A small RGBA preview of one node output, or of an Output node input.</summary>
        public static Vector4[] Preview(ShaderGraph graph, int nodeId, string socket, int size, Func<string, TextureBuffer?>? imageLoader = null)
        {
            var node = graph.Find(nodeId) ?? throw new ArgumentException("No such node.", nameof(nodeId));
            var eval = new ShaderGraphEvaluator(graph, imageLoader, size);
            var f = node.Type == ShaderNodeTypes.Output ? eval.CompileInput(node, socket) : eval.CompileOutput(node, socket);
            var pixels = new Vector4[size * size];
            Parallel.For(0, size, y =>
            {
                for (var x = 0; x < size; x++)
                    pixels[y * size + x] = f(new Vector2((x + 0.5f) / size, (y + 0.5f) / size));
            });
            return pixels;
        }

        private static Color4 Clamp(Vector4 v)
        {
            if (!float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z) || !float.IsFinite(v.W)) v = new Vector4(0, 0, 0, 1);
            return new Color4(Vector4.Clamp(v, Vector4.Zero, Vector4.One));
        }
    }
}
