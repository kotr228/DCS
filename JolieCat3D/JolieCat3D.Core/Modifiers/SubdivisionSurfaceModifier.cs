using JolieCat3D.Core.Geometry;

namespace JolieCat3D.Core.Modifiers
{
    /// <summary>
    /// Smooths <see cref="Modifier.Apply"/>'s input mesh via <see cref="Iterations"/>
    /// repeated levels of true Catmull-Clark subdivision (see
    /// <see cref="CatmullClarkSubdivider"/>'s own remarks on the algorithm, and on how
    /// this differs from Edit Mode's own destructive, non-smoothing "Subdivide" button)
    /// - a modeling tool's standard "Subdivision Surface" modifier, letting a low-poly
    /// mesh (a cube, say) preview as a smoothly rounded one without permanently
    /// increasing its own base geometry's density.
    /// </summary>
    public sealed class SubdivisionSurfaceModifier : Modifier
    {
        public override string Name => "Subdivision Surface";

        private int _iterations = 1;

        /// <summary>How many times to repeat the subdivision, each pass smoothing the
        /// PREVIOUS pass's own already-smoothed output further - clamped to [0, 4] (0 is
        /// a legitimate "temporarily no-op without removing the modifier" state; above 4
        /// the vertex/face count grows so fast - roughly *4 per level - that a modest
        /// mesh already exceeds what this project's fixed-function WPF viewport should
        /// reasonably be asked to rebuild every <c>Scene3DRenderer.Refresh</c>).</summary>
        public int Iterations
        {
            get => _iterations;
            set => _iterations = Math.Clamp(value, 0, 4);
        }

        public override Mesh Apply(Mesh input)
        {
            ArgumentNullException.ThrowIfNull(input);

            var current = input;
            for (var i = 0; i < Iterations; i++)
                current = CatmullClarkSubdivider.Subdivide(current);

            return current;
        }
    }
}
