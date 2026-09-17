using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.Core.Modifiers
{
    /// <summary>
    /// "Auto Smooth" - non-destructively smooth-shades <see cref="Modifier.Apply"/>'s
    /// own input mesh EXCEPT across any edge whose two faces meet at more than
    /// <see cref="AngleThresholdDegrees"/>, so a hard-surface model can look smoothly
    /// curved on its rounded parts while keeping a crisp, faceted look on its sharp
    /// corners/edges - the task's own "keep sharp hard-surface edges crisp" ask.
    /// Unlike <see cref="Geometry.Mesh.ShadeSmooth"/>/<see cref="Geometry.Mesh.ShadeFlat"/>
    /// (immediate, destructive authoring choices - the same "Blender itself treats
    /// these as instant mesh edits, not a modifier" precedent), this belongs in the
    /// non-destructive stack: the threshold is something a user tunes interactively
    /// while watching the result, exactly the kind of adjustable, toggleable,
    /// re-orderable step <see cref="Modifier"/> exists for. All the actual algorithm
    /// lives in <see cref="EdgeSplitter"/> (kept independent/directly testable - see
    /// its own remarks); this class is just the thin Modifier-shaped wrapper around it.
    /// </summary>
    public sealed class EdgeSplitModifier : Modifier
    {
        public override string Name => "Edge Split";

        /// <summary>The dihedral angle (degrees) above which an edge is treated as
        /// hard/split rather than smoothed across - 30, the task's own example
        /// default, and Blender's own long-standing default for the same feature.
        /// Clamped to [0, 180]: 0 degenerates to fully <see cref="Geometry.Mesh.ShadeFlat"/>
        /// (nothing smooths at all, even perfectly coplanar faces, since a real angle
        /// can never be LESS than 0), 180 degenerates to fully
        /// <see cref="Geometry.Mesh.ShadeSmooth"/> (no dihedral angle can ever exceed
        /// it, so every manifold interior edge stays smooth) - both genuinely useful,
        /// intentional endpoints, not values worth rejecting.</summary>
        public float AngleThresholdDegrees
        {
            get => _angleThresholdDegrees;
            set => _angleThresholdDegrees = Math.Clamp(value, 0f, 180f);
        }
        private float _angleThresholdDegrees = 30f;

        public override Mesh Apply(Mesh input, Node? owner = null)
        {
            ArgumentNullException.ThrowIfNull(input);
            return EdgeSplitter.Apply(input, AngleThresholdDegrees);
        }

        public override Modifier Clone() => new EdgeSplitModifier
        {
            IsEnabled = IsEnabled,
            AngleThresholdDegrees = AngleThresholdDegrees,
        };
    }
}
