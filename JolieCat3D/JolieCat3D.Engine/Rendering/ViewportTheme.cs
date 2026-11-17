using JolieCat3D.Core.Numerics;
using JolieCat3D.Engine.Lighting;

namespace JolieCat3D.Engine.Rendering
{
    /// <summary>The viewport look shared by every JolieCat3D window (dark, warm, BlackCat/JolieCat palette).</summary>
    public static class ViewportTheme
    {
        public static LightingSettings CreateDarkThemeLighting() => new()
        {
            DirectionalLightColor = new Color4(0.95f, 0.92f, 0.85f),
            AmbientLightColor = new Color4(0.32f, 0.32f, 0.35f),
        };
    }
}
