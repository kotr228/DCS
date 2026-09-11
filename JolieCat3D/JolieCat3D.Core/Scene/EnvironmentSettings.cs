namespace JolieCat3D.Core.Scene
{
    /// <summary>
    /// A scene-wide Image-Based Lighting/skybox setting (see <see cref="Scene3D.Environment"/>) -
    /// null (no <see cref="EnvironmentSettings"/> at all) means "no skybox, no
    /// environment tint", preserving every existing scene's look exactly.
    /// <c>JolieCat3D.Core</c> has no image codec of its own (the same reason
    /// <see cref="Materials.Material.DiffuseTexturePath"/> is just a file path - see its
    /// own remarks), so this too is just a path; <c>JolieCat3D.Engine.Rendering.EnvironmentVisualFactory</c>
    /// is what actually resolves and loads it.
    /// </summary>
    public sealed class EnvironmentSettings
    {
        /// <summary>The skybox's own directory-or-file-prefix, in exactly the convention
        /// <c>HelixToolkit.Wpf.PanoramaCube3D.Source</c> itself documents: either a
        /// directory containing 6 face images named <c>cube_f</c>/<c>cube_b</c>/
        /// <c>cube_l</c>/<c>cube_r</c>/<c>cube_u</c>/<c>cube_d</c> (front/back/left/right/
        /// up/down), or the literal shared prefix of 6 image files named
        /// <c>{prefix}_f.ext</c> etc. directly. Stored verbatim (not resolved to 6
        /// concrete file paths here) so <c>PanoramaCube3D</c> itself can resolve it
        /// exactly the same way at render time - <c>Engine.Rendering.EnvironmentVisualFactory</c>'s
        /// own remarks cover how this project independently re-derives the same 6 paths
        /// for its own average-color sampling.</summary>
        public string? SkyboxSource { get; set; }
    }
}
