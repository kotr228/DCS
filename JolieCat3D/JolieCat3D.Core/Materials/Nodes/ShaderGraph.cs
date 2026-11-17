using System.Numerics;
using JolieCat3D.Core.Numerics;

namespace JolieCat3D.Core.Materials.Nodes
{
    /// <summary>
    /// A material node tree (Blender's Shader Editor, texture-space subset): procedural and
    /// image nodes wired into a <see cref="ShaderNodeTypes.Output"/> node whose inputs
    /// (Base Color, Roughness, Metallic, Emission, Normal, Alpha) are baked by
    /// <see cref="ShaderGraphBaker"/> into the material's ordinary PBR texture maps - so the
    /// viewport, the path tracer and every exporter use the result unchanged.
    /// Plain data (strings, numbers, lists) so it serialises as-is.
    /// </summary>
    public sealed class ShaderGraph
    {
        public List<ShaderNode> Nodes { get; set; } = new();
        public List<ShaderLink> Links { get; set; } = new();

        /// <summary>Path prefix the baked maps were last written to ("…/Brick" gives
        /// "…/Brick_BaseColor.png" etc.); null until the first bake.</summary>
        public string? BakePathPrefix { get; set; }

        /// <summary>Bake resolution (square).</summary>
        public int BakeSize { get; set; } = 1024;

        public ShaderNode? Find(int id) => Nodes.FirstOrDefault(n => n.Id == id);

        public ShaderNode? Output => Nodes.FirstOrDefault(n => n.Type == ShaderNodeTypes.Output);

        public ShaderNode AddNode(string type, float x = 0, float y = 0)
        {
            var node = ShaderNodeTypes.Create(type);
            node.Id = Nodes.Count == 0 ? 1 : Nodes.Max(n => n.Id) + 1;
            node.X = x;
            node.Y = y;
            Nodes.Add(node);
            return node;
        }

        public void RemoveNode(int id)
        {
            Nodes.RemoveAll(n => n.Id == id);
            Links.RemoveAll(l => l.FromNode == id || l.ToNode == id);
        }

        /// <summary>The link feeding an input, if any.</summary>
        public ShaderLink? LinkInto(int node, string input) => Links.FirstOrDefault(l => l.ToNode == node && l.ToInput == input);

        /// <summary>Connects an output to an input (an input takes one link; the old one is
        /// replaced). Refuses links that would make a cycle; returns whether it connected.</summary>
        public bool Connect(int fromNode, string output, int toNode, string input)
        {
            if (fromNode == toNode || Find(fromNode) is null || Find(toNode) is null) return false;
            if (DependsOn(fromNode, toNode)) return false;
            Links.RemoveAll(l => l.ToNode == toNode && l.ToInput == input);
            Links.Add(new ShaderLink { FromNode = fromNode, FromOutput = output, ToNode = toNode, ToInput = input });
            return true;
        }

        /// <summary>True when <paramref name="node"/> (transitively) reads from <paramref name="upstream"/>.</summary>
        public bool DependsOn(int node, int upstream)
        {
            var seen = new HashSet<int>();
            var stack = new Stack<int>();
            stack.Push(node);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                if (n == upstream) return true;
                if (!seen.Add(n)) continue;
                foreach (var l in Links) if (l.ToNode == n) stack.Push(l.FromNode);
            }
            return false;
        }

        public ShaderGraph Clone() => new()
        {
            Nodes = Nodes.Select(n => n.Clone()).ToList(),
            Links = Links.Select(l => l with { }).ToList(),
            BakePathPrefix = BakePathPrefix,
            BakeSize = BakeSize,
        };

        /// <summary>A starter tree: Principled-style output fed by a noise-driven colour ramp.</summary>
        public static ShaderGraph CreateDefault(Color4? baseColor = null)
        {
            var graph = new ShaderGraph();
            var output = graph.AddNode(ShaderNodeTypes.Output, 520, 80);
            if (baseColor is { } c) output.Inputs["Base Color"] = new[] { c.R, c.G, c.B, 1f };
            return graph;
        }
    }

    public sealed class ShaderNode
    {
        public int Id { get; set; }
        public string Type { get; set; } = ShaderNodeTypes.Value;
        public float X { get; set; }
        public float Y { get; set; }

        /// <summary>Unlinked input values (1 float, or RGBA / XYZ as 3-4 floats).</summary>
        public Dictionary<string, float[]> Inputs { get; set; } = new();

        /// <summary>Choices such as the Math operation, Mix blend mode or image path.</summary>
        public Dictionary<string, string> Options { get; set; } = new();

        /// <summary>Colour Ramp stops (position 0..1 + RGBA).</summary>
        public List<RampStop>? Ramp { get; set; }

        public string Option(string name) =>
            Options.TryGetValue(name, out var v) ? v : ShaderNodeTypes.Get(Type).Options.FirstOrDefault(o => o.Name == name)?.Choices.FirstOrDefault() ?? string.Empty;

        public Vector4 InputValue(string name)
        {
            if (Inputs.TryGetValue(name, out var v) && v.Length > 0)
                return new Vector4(v[0], v.Length > 1 ? v[1] : v[0], v.Length > 2 ? v[2] : v[0], v.Length > 3 ? v[3] : 1f);
            var def = ShaderNodeTypes.Get(Type).Inputs.FirstOrDefault(i => i.Name == name);
            return def?.Default ?? Vector4.Zero;
        }

        public ShaderNode Clone() => new()
        {
            Id = Id,
            Type = Type,
            X = X,
            Y = Y,
            Inputs = Inputs.ToDictionary(kv => kv.Key, kv => (float[])kv.Value.Clone()),
            Options = new Dictionary<string, string>(Options),
            Ramp = Ramp?.Select(r => r with { }).ToList(),
        };
    }

    public sealed record ShaderLink
    {
        public int FromNode { get; set; }
        public string FromOutput { get; set; } = string.Empty;
        public int ToNode { get; set; }
        public string ToInput { get; set; } = string.Empty;
    }

    public sealed record RampStop
    {
        public float Position { get; set; }
        public float R { get; set; }
        public float G { get; set; }
        public float B { get; set; }
        public float A { get; set; } = 1;
    }
}
