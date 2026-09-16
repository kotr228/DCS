using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.Core.Modifiers
{
    /// <summary>Evaluates a <see cref="Scene.Node.Modifiers"/> list against its own
    /// <see cref="Scene.Node.Mesh"/> - the single entry point
    /// <c>JolieCat3D.Engine.Geometry.SceneGraphBuilder</c> calls for every meshed node,
    /// so it never needs to know how many modifiers exist, which are enabled, or how to
    /// chain one's output into the next's input itself.</summary>
    public static class ModifierStack
    {
        /// <summary>Applies every <see cref="Modifier.IsEnabled"/> modifier in
        /// <paramref name="modifiers"/>, in list order, each one's own output feeding the
        /// next's input - exactly <paramref name="mesh"/> itself (not a copy) with an
        /// empty or all-disabled stack, so the overwhelmingly common "no modifiers"
        /// case costs nothing beyond the empty loop. <paramref name="owner"/> (the
        /// <see cref="Node"/> <paramref name="modifiers"/> actually belongs to) is passed
        /// through to every <see cref="Modifier.Apply"/> call - optional, since only
        /// <see cref="BooleanModifier"/> currently needs it (see its own remarks).</summary>
        public static Mesh Evaluate(Mesh mesh, IReadOnlyList<Modifier> modifiers, Node? owner = null)
        {
            ArgumentNullException.ThrowIfNull(mesh);
            ArgumentNullException.ThrowIfNull(modifiers);

            var current = mesh;
            foreach (var modifier in modifiers)
            {
                if (!modifier.IsEnabled) continue;
                current = modifier.Apply(current, owner);
            }

            return current;
        }
    }
}
