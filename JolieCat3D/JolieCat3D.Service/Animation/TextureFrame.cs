using System.Numerics;

namespace JolieCat3D.Service.Animation
{
    /// <summary>One frame of an animated texture sequence - a diffuse texture path plus
    /// the UV sub-rectangle to sample from it (see <c>Core.Materials.Material.DiffuseTexturePath</c>/
    /// <c>DiffuseTextureOffset</c>/<c>DiffuseTextureScale</c>, which these three fields
    /// map onto directly). <see cref="Offset"/> (0,0)/<see cref="Scale"/> (1,1) means
    /// "the whole image" - the common case for a clipbar sequence of separately exported
    /// frame images, each its own whole file; a sprite-sheet-driven sequence instead
    /// keeps the SAME <see cref="TexturePath"/> across every frame and only changes
    /// <see cref="Offset"/>/<see cref="Scale"/> (one shared sheet, a different cell each
    /// frame).</summary>
    public readonly record struct TextureFrame(string TexturePath, Vector2 Offset, Vector2 Scale)
    {
        /// <summary>The whole image, no cropping - the default sub-rectangle a plain
        /// (non-sprite-sheet) frame image uses.</summary>
        public static TextureFrame WholeImage(string texturePath) => new(texturePath, Vector2.Zero, Vector2.One);
    }

    /// <summary>One <see cref="TextureFrame"/> recorded at a single point in time
    /// (seconds) on a <see cref="TextureAnimationTrack"/> - texture frames are always
    /// STEPPED/discrete (see <see cref="TextureAnimationTrack.Evaluate"/>'s own remarks
    /// on why), so unlike <see cref="Keyframe"/> there is no interpolation mode to
    /// record here at all.</summary>
    public readonly record struct TextureKeyframe(double Time, TextureFrame Frame);
}
