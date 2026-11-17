using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.Core.Modifiers
{
    /// <summary>
    /// Non-destructive polygon reduction (Blender's Decimate, "Collapse" mode) - keeps
    /// about <see cref="Ratio"/> of the triangles. Typical use: game LODs. See
    /// <see cref="MeshDecimator"/>.
    /// </summary>
    public sealed class DecimateModifier : Modifier
    {
        public override string Name => "Decimate";

        private float _ratio = 0.5f;

        /// <summary>Fraction of triangles to keep, 0.01..1.</summary>
        public float Ratio
        {
            get => _ratio;
            set => _ratio = Math.Clamp(value, 0.01f, 1f);
        }

        public override Mesh Apply(Mesh input, Node? owner = null)
        {
            ArgumentNullException.ThrowIfNull(input);
            return MeshDecimator.Decimate(input, Ratio);
        }

        public override Modifier Clone() => new DecimateModifier { IsEnabled = IsEnabled, Ratio = Ratio };
    }
}
