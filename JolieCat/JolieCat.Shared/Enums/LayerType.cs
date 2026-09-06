namespace JolieCat.Shared.Enums
{
    /// <summary>
    /// Identifies the kind of content a layer holds within a scene.
    /// </summary>
    public enum LayerType
    {
        Raster,
        Vector,
        Text,
        Group,
        Adjustment,

        /// <summary>A non-destructive layer whose visible content is always
        /// re-sampled fresh from a pristine, never-modified source (a placed image,
        /// or an embedded sub-project's own flattened composite) through its current
        /// transform - see <c>JolieCat.Core.Documents.SmartObjectContent</c>.</summary>
        SmartObject
    }
}
