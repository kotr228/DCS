using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.Core.Modifiers
{
    /// <summary>
    /// Bevel modifier (Blender, "Angle" limit method): non-destructively bevels every
    /// edge sharper than <see cref="AngleDegrees"/> by <see cref="Width"/>, with
    /// <see cref="Segments"/> for a rounded profile. Pair with Weighted Normal for clean
    /// hard-surface game props. See <see cref="MeshBevel"/>.
    /// </summary>
    public sealed class BevelModifier : Modifier
    {
        public override string Name => "Bevel";

        private float _width = 0.05f;
        private int _segments = 2;
        private float _angle = 30f;

        public float Width
        {
            get => _width;
            set => _width = Math.Max(0f, value);
        }

        public int Segments
        {
            get => _segments;
            set => _segments = Math.Clamp(value, 1, 16);
        }

        public float AngleDegrees
        {
            get => _angle;
            set => _angle = Math.Clamp(value, 0f, 180f);
        }

        public override Mesh Apply(Mesh input, Node? owner = null)
        {
            ArgumentNullException.ThrowIfNull(input);
            if (Width <= 0) return input;
            var output = input.Clone();
            output.ShapeKeys.Clear();
            output.ShapeBasis = null;
            return MeshBevel.BevelEdgesByAngle(output, AngleDegrees, Width, Segments) is null ? input : output;
        }

        public override Modifier Clone() => new BevelModifier { IsEnabled = IsEnabled, Width = Width, Segments = Segments, AngleDegrees = AngleDegrees };
    }
}
