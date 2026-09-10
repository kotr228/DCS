using JolieCat3D.Core.Numerics;

namespace JolieCat3D.Core.Scene
{
    /// <summary>
    /// Makes a <see cref="Node"/> a light source - present (non-null, on
    /// <see cref="Node.Light"/>) only for a node meant to actually cast light, the same
    /// "optional data, not a subclass" shape <see cref="CameraData"/>/<see cref="Node.Mesh"/>
    /// already use. A light node's own position (<see cref="LightType.Point"/>/
    /// <see cref="LightType.Spot"/>) and direction (<see cref="LightType.Directional"/>/
    /// <see cref="LightType.Spot"/> - the node's own local +Z axis rotated by
    /// <see cref="Node.LocalRotation"/>, the same forward-axis convention a camera node
    /// uses) both come from the SAME <see cref="Node.LocalPosition"/>/<see cref="Node.LocalRotation"/>
    /// every other node already has, so a light can be parented, transformed, and
    /// keyframed on the timeline exactly like a meshed node.
    /// </summary>
    public sealed class LightData
    {
        public LightType Type { get; set; } = LightType.Directional;

        public Color4 Color { get; set; } = Color4.White;

        /// <summary>A plain multiplier on <see cref="Color"/>'s own brightness - WPF's
        /// own <c>Light</c> types have no separate "intensity" of their own (a light's
        /// color IS its brightness there), so <c>JolieCat3D.Engine</c>'s adapter folds
        /// this into the WPF light's own color before handing it off, the same
        /// "there's no direct WPF equivalent, so approximate it the simplest sane way"
        /// reasoning <c>Engine.Geometry.MaterialFactory</c> already applies to
        /// <see cref="Materials.Material.Roughness"/>/<see cref="Materials.Material.Metallic"/>.
        /// 1 (unchanged) by default.</summary>
        public float Intensity { get; set; } = 1f;

        /// <summary>How far (in world units) a <see cref="LightType.Point"/>/
        /// <see cref="LightType.Spot"/> light's own falloff reaches - meaningless for
        /// <see cref="LightType.Directional"/> (a parallel-ray source has no distance
        /// falloff of its own at all).</summary>
        public float Range { get; set; } = 10f;

        /// <summary>The full cone angle (in degrees) of a <see cref="LightType.Spot"/>
        /// light - meaningless for <see cref="LightType.Directional"/>/<see cref="LightType.Point"/>.</summary>
        public float SpotAngle { get; set; } = 45f;
    }
}
