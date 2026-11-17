using System.Numerics;

namespace JolieCat3D.Core.Materials.Nodes
{
    public enum SocketKind { Float, Color, Vector }

    /// <summary>An input socket. <see cref="ImplicitUv"/> inputs read the texture
    /// coordinate when nothing is linked (like Blender's texture Vector inputs).</summary>
    public sealed record ShaderSocket(string Name, SocketKind Kind, Vector4 Default, bool ImplicitUv = false, float Min = float.NegativeInfinity, float Max = float.PositiveInfinity);

    /// <summary>A drop-down option; an empty <see cref="Choices"/> list means free text (a file path).</summary>
    public sealed record ShaderOption(string Name, IReadOnlyList<string> Choices);

    public sealed record ShaderNodeDefinition(string Type, string Title, string Category, IReadOnlyList<ShaderSocket> Inputs,
        IReadOnlyList<(string Name, SocketKind Kind)> Outputs, IReadOnlyList<ShaderOption> Options);

    /// <summary>The node catalogue (names follow Blender).</summary>
    public static class ShaderNodeTypes
    {
        public const string Output = "Output";
        public const string TexCoord = "TexCoord";
        public const string Value = "Value";
        public const string Rgb = "RGB";
        public const string Mapping = "Mapping";
        public const string Image = "ImageTexture";
        public const string Noise = "Noise";
        public const string Voronoi = "Voronoi";
        public const string Checker = "Checker";
        public const string Gradient = "Gradient";
        public const string Brick = "Brick";
        public const string Wave = "Wave";
        public const string ColorRamp = "ColorRamp";
        public const string Mix = "Mix";
        public const string Math = "Math";
        public const string Invert = "Invert";
        public const string HueSat = "HueSat";
        public const string BrightContrast = "BrightContrast";
        public const string SeparateRgb = "SeparateRGB";
        public const string CombineRgb = "CombineRGB";
        public const string Bump = "Bump";
        public const string NormalMap = "NormalMap";

        public static readonly string[] MixModes = { "Mix", "Add", "Multiply", "Subtract", "Screen", "Overlay", "Darken", "Lighten", "Difference" };
        public static readonly string[] MathOps = { "Add", "Subtract", "Multiply", "Divide", "Power", "Minimum", "Maximum", "Greater Than", "Less Than", "Absolute", "Sine", "Modulo", "Snap" };
        private static readonly string[] YesNo = { "Yes", "No" };
        private static readonly string[] NoYes = { "No", "Yes" };

        private static Vector4 F(float v) => new(v, v, v, 1);
        private static Vector4 C(float r, float g, float b) => new(r, g, b, 1);
        private static ShaderSocket Uv() => new("Vector", SocketKind.Vector, Vector4.Zero, ImplicitUv: true);
        private static ShaderSocket Fl(string name, float v, float min = float.NegativeInfinity, float max = float.PositiveInfinity) => new(name, SocketKind.Float, F(v), false, min, max);
        private static ShaderSocket Col(string name, float r, float g, float b) => new(name, SocketKind.Color, C(r, g, b));

        private static readonly Dictionary<string, ShaderNodeDefinition> Definitions = new[]
        {
            new ShaderNodeDefinition(Output, "Material Output", "Output",
                new[] { Col("Base Color", 0.8f, 0.8f, 0.8f), Fl("Metallic", 0, 0, 1), Fl("Roughness", 0.5f, 0, 1), Col("Emission", 0, 0, 0), Fl("Emission Strength", 1, 0), Fl("Alpha", 1, 0, 1), new ShaderSocket("Normal", SocketKind.Vector, new Vector4(0.5f, 0.5f, 1, 1)) },
                Array.Empty<(string, SocketKind)>(), Array.Empty<ShaderOption>()),
            new ShaderNodeDefinition(TexCoord, "Texture Coordinate", "Input", Array.Empty<ShaderSocket>(), new[] { ("UV", SocketKind.Vector) }, Array.Empty<ShaderOption>()),
            new ShaderNodeDefinition(Value, "Value", "Input", new[] { Fl("Value", 0.5f) }, new[] { ("Value", SocketKind.Float) }, Array.Empty<ShaderOption>()),
            new ShaderNodeDefinition(Rgb, "RGB", "Input", new[] { Col("Color", 0.5f, 0.5f, 0.5f) }, new[] { ("Color", SocketKind.Color) }, Array.Empty<ShaderOption>()),
            new ShaderNodeDefinition(Mapping, "Mapping", "Vector",
                new[] { Uv(), new ShaderSocket("Location", SocketKind.Vector, Vector4.Zero), Fl("Rotation", 0), new ShaderSocket("Scale", SocketKind.Vector, Vector4.One) },
                new[] { ("Vector", SocketKind.Vector) }, Array.Empty<ShaderOption>()),
            new ShaderNodeDefinition(Image, "Image Texture", "Texture", new[] { Uv() },
                new[] { ("Color", SocketKind.Color), ("Alpha", SocketKind.Float) }, new[] { new ShaderOption("Image", Array.Empty<string>()) }),
            new ShaderNodeDefinition(Noise, "Noise Texture", "Texture",
                new[] { Uv(), Fl("Scale", 5, 0), Fl("Detail", 2, 0, 15), Fl("Roughness", 0.5f, 0, 1), Fl("Distortion", 0) },
                new[] { ("Fac", SocketKind.Float), ("Color", SocketKind.Color) }, new[] { new ShaderOption("Seamless", YesNo) }),
            new ShaderNodeDefinition(Voronoi, "Voronoi Texture", "Texture",
                new[] { Uv(), Fl("Scale", 5, 0), Fl("Randomness", 1, 0, 1) },
                new[] { ("Distance", SocketKind.Float), ("Color", SocketKind.Color) }, new[] { new ShaderOption("Feature", new[] { "F1", "Distance to Edge" }), new ShaderOption("Seamless", YesNo) }),
            new ShaderNodeDefinition(Checker, "Checker Texture", "Texture",
                new[] { Uv(), Col("Color1", 0.8f, 0.8f, 0.8f), Col("Color2", 0.2f, 0.2f, 0.2f), Fl("Scale", 4, 0) },
                new[] { ("Color", SocketKind.Color), ("Fac", SocketKind.Float) }, Array.Empty<ShaderOption>()),
            new ShaderNodeDefinition(Gradient, "Gradient Texture", "Texture", new[] { Uv() },
                new[] { ("Color", SocketKind.Color), ("Fac", SocketKind.Float) }, new[] { new ShaderOption("Type", new[] { "Linear", "Quadratic", "Diagonal", "Radial", "Spherical" }) }),
            new ShaderNodeDefinition(Brick, "Brick Texture", "Texture",
                new[] { Uv(), Col("Color1", 0.8f, 0.8f, 0.8f), Col("Color2", 0.2f, 0.2f, 0.2f), Col("Mortar", 0, 0, 0), Fl("Scale", 2, 0), Fl("Mortar Size", 0.04f, 0, 0.5f), Fl("Offset", 0.5f, 0, 1), Fl("Brick Width", 0.5f, 0.01f), Fl("Row Height", 0.25f, 0.01f) },
                new[] { ("Color", SocketKind.Color), ("Fac", SocketKind.Float) }, Array.Empty<ShaderOption>()),
            new ShaderNodeDefinition(Wave, "Wave Texture", "Texture",
                new[] { Uv(), Fl("Scale", 5, 0), Fl("Distortion", 0), Fl("Detail", 2, 0, 15) },
                new[] { ("Color", SocketKind.Color), ("Fac", SocketKind.Float) }, new[] { new ShaderOption("Type", new[] { "Bands", "Rings" }), new ShaderOption("Profile", new[] { "Sine", "Saw" }) }),
            new ShaderNodeDefinition(ColorRamp, "Color Ramp", "Converter", new[] { Fl("Fac", 0.5f, 0, 1) },
                new[] { ("Color", SocketKind.Color), ("Alpha", SocketKind.Float) }, new[] { new ShaderOption("Interpolation", new[] { "Linear", "Constant", "Ease" }) }),
            new ShaderNodeDefinition(Mix, "Mix Color", "Color", new[] { Fl("Fac", 0.5f, 0, 1), Col("A", 0.5f, 0.5f, 0.5f), Col("B", 0.5f, 0.5f, 0.5f) },
                new[] { ("Result", SocketKind.Color) }, new[] { new ShaderOption("Blend", MixModes), new ShaderOption("Clamp", NoYes) }),
            new ShaderNodeDefinition(Math, "Math", "Converter", new[] { Fl("A", 0.5f), Fl("B", 0.5f) },
                new[] { ("Value", SocketKind.Float) }, new[] { new ShaderOption("Operation", MathOps), new ShaderOption("Clamp", NoYes) }),
            new ShaderNodeDefinition(Invert, "Invert", "Color", new[] { Fl("Fac", 1, 0, 1), Col("Color", 0, 0, 0) }, new[] { ("Color", SocketKind.Color) }, Array.Empty<ShaderOption>()),
            new ShaderNodeDefinition(HueSat, "Hue/Saturation/Value", "Color",
                new[] { Fl("Hue", 0.5f, 0, 1), Fl("Saturation", 1, 0), Fl("Value", 1, 0), Fl("Fac", 1, 0, 1), Col("Color", 0.8f, 0.8f, 0.8f) },
                new[] { ("Color", SocketKind.Color) }, Array.Empty<ShaderOption>()),
            new ShaderNodeDefinition(BrightContrast, "Bright/Contrast", "Color", new[] { Col("Color", 1, 1, 1), Fl("Bright", 0), Fl("Contrast", 0) },
                new[] { ("Color", SocketKind.Color) }, Array.Empty<ShaderOption>()),
            new ShaderNodeDefinition(SeparateRgb, "Separate RGB", "Converter", new[] { Col("Color", 0.8f, 0.8f, 0.8f) },
                new[] { ("R", SocketKind.Float), ("G", SocketKind.Float), ("B", SocketKind.Float) }, Array.Empty<ShaderOption>()),
            new ShaderNodeDefinition(CombineRgb, "Combine RGB", "Converter", new[] { Fl("R", 0), Fl("G", 0), Fl("B", 0) },
                new[] { ("Color", SocketKind.Color) }, Array.Empty<ShaderOption>()),
            new ShaderNodeDefinition(Bump, "Bump", "Vector", new[] { Fl("Strength", 1, 0, 1), Fl("Distance", 0.1f, 0), Fl("Height", 0.5f) },
                new[] { ("Normal", SocketKind.Vector) }, new[] { new ShaderOption("Invert", NoYes) }),
            new ShaderNodeDefinition(NormalMap, "Normal Map", "Vector", new[] { Fl("Strength", 1, 0), new ShaderSocket("Color", SocketKind.Color, new Vector4(0.5f, 0.5f, 1, 1)) },
                new[] { ("Normal", SocketKind.Vector) }, Array.Empty<ShaderOption>()),
        }.ToDictionary(d => d.Type);

        public static IEnumerable<ShaderNodeDefinition> All => Definitions.Values;

        public static ShaderNodeDefinition Get(string type) =>
            Definitions.TryGetValue(type, out var d) ? d : throw new ArgumentException($"Unknown shader node type '{type}'.", nameof(type));

        public static bool Exists(string type) => Definitions.ContainsKey(type);

        public static ShaderNode Create(string type)
        {
            var def = Get(type);
            var node = new ShaderNode { Type = type };
            if (type == ColorRamp)
                node.Ramp = new List<RampStop> { new() { Position = 0, R = 0, G = 0, B = 0 }, new() { Position = 1, R = 1, G = 1, B = 1 } };
            return node;
        }
    }
}
