using System.Windows.Media;
using System.Windows.Media.Media3D;

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

        private static Color ToWpfColor(Core.Numerics.Color4 color) => Color.FromScRgb(
            System.Math.Clamp(color.A, 0f, 1f),
            System.Math.Clamp(color.R, 0f, 1f),
            System.Math.Clamp(color.G, 0f, 1f),
            System.Math.Clamp(color.B, 0f, 1f));
    }
}
