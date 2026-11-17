using System.Numerics;
using JolieCat3D.Core.Modifiers;
using JolieCat3D.Core.Numerics;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.Core.Constraints
{
    /// <summary>A constraint whose settings are a flat list of values (shown generically by
    /// the Inspector), optionally with a target object.</summary>
    public interface IParameterizedConstraint
    {
        IReadOnlyList<ModifierParameter> Parameters { get; }
        bool UsesTarget { get; }
        Node? Target { get; set; }
    }

    /// <summary>Type names (for saving) of the parameterized constraints.</summary>
    public static class ParameterizedConstraints
    {
        public static readonly IReadOnlyDictionary<string, Func<Constraint>> Factories = new Dictionary<string, Func<Constraint>>
        {
            ["CopyLocation"] = () => new CopyLocationConstraint(),
            ["CopyRotation"] = () => new CopyRotationConstraint(),
            ["CopyScale"] = () => new CopyScaleConstraint(),
            ["LimitLocation"] = () => new LimitLocationConstraint(),
            ["LimitRotation"] = () => new LimitRotationConstraint(),
            ["LimitScale"] = () => new LimitScaleConstraint(),
        };

        public static string? TypeName(Constraint constraint) => constraint switch
        {
            CopyLocationConstraint => "CopyLocation",
            CopyRotationConstraint => "CopyRotation",
            CopyScaleConstraint => "CopyScale",
            LimitLocationConstraint => "LimitLocation",
            LimitRotationConstraint => "LimitRotation",
            LimitScaleConstraint => "LimitScale",
            _ => null,
        };

        internal static readonly string[] OffOn = { "Off", "On" };

        internal static void SetWorldPosition(Node node, Vector3 world)
        {
            if (node.Parent is { } parent && Matrix4x4.Invert(parent.GetWorldTransform(), out var toParent))
                world = Vector3.Transform(world, toParent);
            node.LocalPosition = world;
        }

        internal static void SetWorldRotation(Node node, Quaternion world) =>
            node.LocalRotation = node.Parent is null ? Quaternion.Normalize(world) : Quaternion.Normalize(Quaternion.Inverse(node.Parent.GetWorldRotation()) * world);

        internal static Vector3 WorldScale(Node node) =>
            Matrix4x4.Decompose(node.GetWorldTransform(), out var scale, out _, out _) ? scale : node.LocalScale;
    }

    /// <summary>Shared target + influence plumbing.</summary>
    public abstract class TargetConstraint : Constraint, IParameterizedConstraint
    {
        public Node? Target { get; set; }
        public float Influence { get; set; } = 1f;
        public virtual bool UsesTarget => true;
        public abstract IReadOnlyList<ModifierParameter> Parameters { get; }
        protected ModifierParameter InfluenceParameter => new("Influence", () => Influence, v => Influence = Math.Clamp(v, 0f, 1f));
        protected static ModifierParameter Toggle(string label, Func<bool> get, Action<bool> set) =>
            new(label, () => get() ? 1 : 0, v => set(v >= 0.5f), ParameterizedConstraints.OffOn);
        protected T CopyBase<T>(T copy) where T : TargetConstraint
        {
            copy.IsEnabled = IsEnabled;
            copy.Target = Target;
            copy.Influence = Influence;
            return copy;
        }
    }

    /// <summary>Copy Location (Blender): follows the target's world position on the chosen axes.</summary>
    public sealed class CopyLocationConstraint : TargetConstraint
    {
        public override string Name => "Copy Location";
        public bool X { get; set; } = true;
        public bool Y { get; set; } = true;
        public bool Z { get; set; } = true;

        public override IReadOnlyList<ModifierParameter> Parameters => new[]
        {
            Toggle("X", () => X, v => X = v), Toggle("Y", () => Y, v => Y = v), Toggle("Z", () => Z, v => Z = v), InfluenceParameter,
        };

        public override void Apply(Node node)
        {
            if (Target is null || ReferenceEquals(Target, node)) return;
            var own = node.GetWorldPosition();
            var target = Target.GetWorldPosition();
            var wanted = new Vector3(X ? target.X : own.X, Y ? target.Y : own.Y, Z ? target.Z : own.Z);
            ParameterizedConstraints.SetWorldPosition(node, Vector3.Lerp(own, wanted, Influence));
        }

        public override Constraint Clone() => CopyBase(new CopyLocationConstraint { X = X, Y = Y, Z = Z });
    }

    /// <summary>Copy Rotation (Blender): matches the target's world rotation.</summary>
    public sealed class CopyRotationConstraint : TargetConstraint
    {
        public override string Name => "Copy Rotation";
        public override IReadOnlyList<ModifierParameter> Parameters => new[] { InfluenceParameter };

        public override void Apply(Node node)
        {
            if (Target is null || ReferenceEquals(Target, node)) return;
            ParameterizedConstraints.SetWorldRotation(node, Quaternion.Slerp(node.GetWorldRotation(), Target.GetWorldRotation(), Influence));
        }

        public override Constraint Clone() => CopyBase(new CopyRotationConstraint());
    }

    /// <summary>Copy Scale (Blender): matches the target's world scale.</summary>
    public sealed class CopyScaleConstraint : TargetConstraint
    {
        public override string Name => "Copy Scale";
        public override IReadOnlyList<ModifierParameter> Parameters => new[] { InfluenceParameter };

        public override void Apply(Node node)
        {
            if (Target is null || ReferenceEquals(Target, node)) return;
            var parentScale = node.Parent is null ? Vector3.One : ParameterizedConstraints.WorldScale(node.Parent);
            var wanted = ParameterizedConstraints.WorldScale(Target) / Vector3.Max(parentScale, new Vector3(1e-6f));
            node.LocalScale = Vector3.Lerp(node.LocalScale, wanted, Influence);
        }

        public override Constraint Clone() => CopyBase(new CopyScaleConstraint());
    }

    /// <summary>Base for the Limit constraints: per-axis min/max on a local channel.</summary>
    public abstract class LimitConstraint : TargetConstraint
    {
        public override bool UsesTarget => false;
        public bool[] Use { get; } = new bool[3];
        public float[] Min { get; } = new float[3];
        public float[] Max { get; } = new float[3];

        public override IReadOnlyList<ModifierParameter> Parameters
        {
            get
            {
                var list = new List<ModifierParameter>();
                var axes = new[] { "X", "Y", "Z" };
                for (var i = 0; i < 3; i++)
                {
                    var a = i;
                    list.Add(Toggle($"Limit {axes[a]}", () => Use[a], v => Use[a] = v));
                    list.Add(new ModifierParameter($"Min {axes[a]}", () => Min[a], v => Min[a] = v));
                    list.Add(new ModifierParameter($"Max {axes[a]}", () => Max[a], v => Max[a] = v));
                }
                list.Add(InfluenceParameter);
                return list;
            }
        }

        protected Vector3 Clamp(Vector3 v)
        {
            float C(int i, float x)
            {
                if (!Use[i]) return x;
                float lo = MathF.Min(Min[i], Max[i]), hi = MathF.Max(Min[i], Max[i]);
                return x + (Math.Clamp(x, lo, hi) - x) * Influence;
            }
            return new Vector3(C(0, v.X), C(1, v.Y), C(2, v.Z));
        }

        protected T CopyLimits<T>(T copy) where T : LimitConstraint
        {
            CopyBase(copy);
            Array.Copy(Use, copy.Use, 3);
            Array.Copy(Min, copy.Min, 3);
            Array.Copy(Max, copy.Max, 3);
            return copy;
        }
    }

    /// <summary>Limit Location (Blender, local space).</summary>
    public sealed class LimitLocationConstraint : LimitConstraint
    {
        public override string Name => "Limit Location";
        public override void Apply(Node node) => node.LocalPosition = Clamp(node.LocalPosition);
        public override Constraint Clone() => CopyLimits(new LimitLocationConstraint());
    }

    /// <summary>Limit Rotation (Blender, local Euler degrees).</summary>
    public sealed class LimitRotationConstraint : LimitConstraint
    {
        public override string Name => "Limit Rotation";

        public override void Apply(Node node)
        {
            var euler = EulerAngles.ToDegrees(node.LocalRotation);
            var clamped = Clamp(euler);
            if (clamped != euler) node.LocalRotation = EulerAngles.FromDegrees(clamped.X, clamped.Y, clamped.Z);
        }

        public override Constraint Clone() => CopyLimits(new LimitRotationConstraint());
    }

    /// <summary>Limit Scale (Blender, local).</summary>
    public sealed class LimitScaleConstraint : LimitConstraint
    {
        public override string Name => "Limit Scale";

        public LimitScaleConstraint()
        {
            Min[0] = Min[1] = Min[2] = 1;
            Max[0] = Max[1] = Max[2] = 1;
        }

        public override void Apply(Node node) => node.LocalScale = Clamp(node.LocalScale);
        public override Constraint Clone() => CopyLimits(new LimitScaleConstraint());
    }
}
