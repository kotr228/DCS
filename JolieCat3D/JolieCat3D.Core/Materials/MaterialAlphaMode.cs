namespace JolieCat3D.Core.Materials
{
    /// <summary>How a material's alpha (opacity x texture alpha) is used - glTF alphaMode.</summary>
    public enum MaterialAlphaMode
    {
        /// <summary>Alpha ignored (still blended when Opacity &lt; 1, for older scenes).</summary>
        Opaque,

        /// <summary>Real transparency, sorted back to front (glass, smoke).</summary>
        Blend,

        /// <summary>Cut-out: pixels below <see cref="Material.AlphaCutoff"/> are discarded,
        /// the rest are opaque (foliage, fences, hair cards). Casts matching shadows.</summary>
        Clip,
    }
}
