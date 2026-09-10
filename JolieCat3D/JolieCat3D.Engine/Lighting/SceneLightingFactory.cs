using System.Windows.Media;
using System.Windows.Media.Media3D;
using CoreLightType = JolieCat3D.Core.Scene.LightType;
using CoreNode = JolieCat3D.Core.Scene.Node;
using CoreScene = JolieCat3D.Core.Scene.Scene3D;

namespace JolieCat3D.Engine.Lighting
{
    /// <summary>
    /// Builds the standard directional + ambient light rig every viewport in this project
    /// uses (see <c>JolieCat3D.Engine.Rendering.Scene3DRenderer</c>), from a
    /// <see cref="LightingSettings"/>. A plain <see cref="Model3DGroup"/> of two WPF
    /// <see cref="Light"/>s - not <c>HelixToolkit.Wpf</c>'s own <c>DefaultLights</c>/
    /// <c>SunLight</c> helpers, since this project's lighting is meant to be driven by
    /// <see cref="LightingSettings"/>'s own explicit direction/color rather than whatever
    /// those helpers default to or derive from camera position.
    /// </summary>
    public static class SceneLightingFactory
    {
        public static Model3DGroup CreateStandardLighting(LightingSettings? settings = null)
        {
            settings ??= LightingSettings.CreateDefault();

            var group = new Model3DGroup();

            group.Children.Add(new DirectionalLight(
                ToWpfColor(settings.DirectionalLightColor),
                new Vector3D(settings.DirectionalLightDirection.X, settings.DirectionalLightDirection.Y, settings.DirectionalLightDirection.Z)));

            group.Children.Add(new AmbientLight(ToWpfColor(settings.AmbientLightColor)));

            group.Freeze();
            return group;
        }

        /// <summary>Builds one real WPF <see cref="Light"/> per <see cref="Core.Scene.Node.Light"/>
        /// found anywhere in <paramref name="scene"/> (a <see cref="CoreLightType.Directional"/>
        /// node's own world-space forward direction - <see cref="CoreNode.GetWorldForward"/> -
        /// becomes a <see cref="DirectionalLight.Direction"/>; a <see cref="CoreLightType.Point"/>/
        /// <see cref="CoreLightType.Spot"/> node's world position becomes that light's
        /// own <see cref="PointLightBase.Position"/>, with <see cref="CoreLightType.Spot"/>
        /// additionally getting a direction and a cone derived from
        /// <see cref="Core.Scene.LightData.SpotAngle"/>) - ADDITIVE to
        /// <see cref="CreateStandardLighting"/>'s own fixed rig (<c>Scene3DRenderer</c>
        /// keeps both as separate visuals), not a replacement for it: a scene with no
        /// light nodes at all (every scene authored before this feature existed) keeps
        /// looking exactly as it always did, lit purely by the fixed rig. Rebuilt fresh
        /// (never frozen for reuse) on every call - unlike <see cref="CreateStandardLighting"/>,
        /// a scene's own light nodes can move/animate, so this needs to be called again
        /// every time the scene re-renders, the same "rebuild, don't try to patch in
        /// place" approach every other per-frame visual in this project already
        /// uses.</summary>
        public static Model3DGroup CreateSceneLights(CoreScene scene)
        {
            ArgumentNullException.ThrowIfNull(scene);

            var group = new Model3DGroup();

            foreach (var node in scene.Traverse())
            {
                if (node.Light is not { } light) continue;

                var color = ToWpfColor(light.Color, light.Intensity);
                var forward = node.GetWorldForward();
                var direction = new Vector3D(forward.X, forward.Y, forward.Z);

                Light wpfLight = light.Type switch
                {
                    CoreLightType.Point => new PointLight(color, ToPoint3D(node.GetWorldPosition())) { Range = light.Range },
                    CoreLightType.Spot => new SpotLight(
                        color, ToPoint3D(node.GetWorldPosition()), direction,
                        outerConeAngle: light.SpotAngle / 2.0, innerConeAngle: light.SpotAngle / 4.0)
                    { Range = light.Range },
                    _ => new DirectionalLight(color, direction),
                };

                group.Children.Add(wpfLight);
            }

            group.Freeze();
            return group;
        }

        private static Point3D ToPoint3D(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

        private static Color ToWpfColor(Core.Numerics.Color4 color) => Color.FromScRgb(
            System.Math.Clamp(color.A, 0f, 1f),
            System.Math.Clamp(color.R, 0f, 1f),
            System.Math.Clamp(color.G, 0f, 1f),
            System.Math.Clamp(color.B, 0f, 1f));

        /// <summary>The same conversion as <see cref="ToWpfColor(Core.Numerics.Color4)"/>,
        /// scaled by <paramref name="intensity"/> - WPF's own <see cref="Light"/> types
        /// have no separate brightness control of their own (see <see cref="Core.Scene.LightData.Intensity"/>'s
        /// own remarks on why this is folded into the color here instead).</summary>
        private static Color ToWpfColor(Core.Numerics.Color4 color, float intensity) => Color.FromScRgb(
            System.Math.Clamp(color.A, 0f, 1f),
            System.Math.Clamp(color.R * intensity, 0f, 1f),
            System.Math.Clamp(color.G * intensity, 0f, 1f),
            System.Math.Clamp(color.B * intensity, 0f, 1f));
    }
}
