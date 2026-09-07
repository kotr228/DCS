using JolieCat3D.Core.Numerics;

namespace JolieCat3D.Core.Materials
{
    /// <summary>
    /// A named surface appearance a <see cref="Geometry.Mesh"/> can reference (see
    /// <see cref="Geometry.Mesh.Material"/>). Deliberately just enough for a standard
    /// diffuse+specular lighting model - not a full PBR material - since that's all
    /// <c>JolieCat3D.Engine</c>'s renderer currently maps onto WPF's own fixed-function
    /// 3D materials (<c>DiffuseMaterial</c>/<c>SpecularMaterial</c>). Plain mutable
    /// properties, not a record, so an editor can tweak one in place without every mesh
    /// referencing it needing to be reassigned a new instance.
    /// </summary>
    public sealed class Material
    {
        public string Name { get; set; }

        /// <summary>The base surface color under flat/ambient lighting.</summary>
        public Color4 DiffuseColor { get; set; } = Color4.White;

        /// <summary>The color of specular highlights - bright, direct reflections of a light source.</summary>
        public Color4 SpecularColor { get; set; } = new Color4(1f, 1f, 1f);

        /// <summary>How tight/sharp specular highlights are - higher is smaller and
        /// glossier, lower is broader and duller. WPF's own <c>SpecularMaterial.SpecularPower</c> convention.</summary>
        public double SpecularPower { get; set; } = 30.0;

        /// <summary>1 = fully opaque, 0 = fully invisible.</summary>
        public float Opacity { get; set; } = 1f;

        public Material(string name = "Material") => Name = name;

        /// <summary>A flat, unlit-looking default - a plain mid-gray diffuse with no
        /// specular contribution - for a mesh created without an explicit material.</summary>
        public static Material CreateDefault() => new("Default")
        {
            DiffuseColor = new Color4(0.75f, 0.75f, 0.75f),
            SpecularColor = Color4.Black,
            SpecularPower = 1.0,
        };
    }
}
