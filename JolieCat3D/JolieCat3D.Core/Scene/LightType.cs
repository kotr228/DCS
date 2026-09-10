namespace JolieCat3D.Core.Scene
{
    /// <summary>Which kind of light source a <see cref="LightData"/> represents -
    /// matching WPF's own three concrete <c>System.Windows.Media.Media3D.Light</c>
    /// subtypes (<c>DirectionalLight</c>/<c>PointLight</c>/<c>SpotLight</c>) exactly, so
    /// <c>JolieCat3D.Engine</c>'s adapter never needs to invent a mapping of its own.</summary>
    public enum LightType
    {
        /// <summary>Parallel rays from an infinitely distant source (the sun) - has a
        /// direction (from <see cref="Node.LocalRotation"/>, see <see cref="LightData"/>'s
        /// own remarks) but no position/falloff of its own.</summary>
        Directional,

        /// <summary>Radiates equally in all directions from a single world position
        /// (<see cref="Node.GetWorldPosition"/>), falling off with distance out to
        /// <see cref="LightData.Range"/> - a bare light bulb.</summary>
        Point,

        /// <summary>A <see cref="Point"/> light additionally narrowed to a cone along
        /// the node's own forward direction, per <see cref="LightData.SpotAngle"/> - a
        /// flashlight/stage spotlight.</summary>
        Spot,
    }
}
