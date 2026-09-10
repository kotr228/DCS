using System.Numerics;
using JolieCat3D.Core.Numerics;

namespace JolieCat3D.Core.Materials
{
    /// <summary>
    /// A named surface appearance a <see cref="Geometry.Mesh"/> can reference (see
    /// <see cref="Geometry.Mesh.Material"/>) - a diffuse/albedo color plus a scalar
    /// <see cref="Roughness"/>/<see cref="Metallic"/> pair (the standard
    /// metallic-roughness PBR workflow's own two controls) and an optional diffuse
    /// texture. Not a FULL PBR material: WPF's <c>Model3D</c> materials
    /// (<c>DiffuseMaterial</c>/<c>SpecularMaterial</c>) are fixed-function with no
    /// custom per-pixel shader hook at all (unlike a 2D <c>UIElement</c>'s own
    /// <c>ShaderEffect</c>), so there is no way for <c>JolieCat3D.Engine</c>'s renderer
    /// to actually sample a normal map or a per-pixel roughness/metallic map, however
    /// many properties this class exposed for them - a real, structural platform
    /// limitation (see <c>Engine.Geometry.MaterialFactory</c>'s own remarks on how
    /// <see cref="Roughness"/>/<see cref="Metallic"/> are approximated instead, and
    /// <c>MeshFileService</c>'s own remarks on the similar, deliberate FBX scope cut),
    /// not an oversight - so this class only ever gained the parts of "advanced
    /// material" that WPF's own fixed-function pipeline can actually render. Plain
    /// mutable properties, not a record, so an editor can tweak one in place without
    /// every mesh referencing it needing to be reassigned a new instance.
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

        /// <summary>0 = mirror-smooth (a small, tight, bright specular highlight), 1 =
        /// fully matte (a broad, dim one) - the standard metallic-roughness workflow's
        /// own "roughness" control, mapped by <c>JolieCat3D.Engine.Geometry.MaterialFactory</c>
        /// onto WPF's <c>SpecularMaterial.SpecularPower</c> (which has no direct
        /// roughness concept of its own) rather than replacing <see cref="SpecularPower"/>
        /// outright, so a material that also hand-tunes <see cref="SpecularPower"/> keeps
        /// that as its own "full gloss" ceiling. 0.5 (a medium, unremarkable roughness) by
        /// default, not 0 - an unset value should read as "ordinary", not "mirror-polished".</summary>
        public float Roughness { get; set; } = 0.5f;

        /// <summary>0 = a plain dielectric (non-metal) surface, 1 = fully metallic - the
        /// other half of the metallic-roughness workflow. Blends this material's own
        /// <see cref="SpecularColor"/> toward its <see cref="DiffuseColor"/> for the
        /// specular highlight's tint (a fully metallic surface's reflections are colored
        /// by its own albedo; a dielectric one's are neutral) - see
        /// <c>JolieCat3D.Engine.Geometry.MaterialFactory</c>'s own remarks. 0 by default,
        /// matching every material authored before this property existed (a Metallic of 0
        /// leaves <see cref="SpecularColor"/> completely untinted, so nothing already
        /// relying on it changes look).</summary>
        public float Metallic { get; set; } = 0f;

        /// <summary>Optional path to an image file this material's diffuse color is
        /// sampled from instead of (not blended with) <see cref="DiffuseColor"/> - null
        /// (the default) means "flat-shaded, no texture", preserving every existing
        /// material's look exactly. <c>JolieCat3D.Core</c> has no image codec of its own
        /// (deliberately - see <c>JolieCat3D.Service.Interop.TwoDAssetBridge</c>'s own
        /// remarks), so this is just a file path; whichever layer actually renders the
        /// material (<c>JolieCat3D.Engine.Geometry.MaterialFactory</c>) is what loads
        /// its bytes.</summary>
        public string? DiffuseTexturePath { get; set; }

        /// <summary>The UV-space origin (0-1, measured from the texture's own bottom-left)
        /// of the sub-rectangle this material actually samples - (0,0), the default,
        /// means "the whole image, from its own origin". Together with
        /// <see cref="DiffuseTextureScale"/>, this is a WPF-<c>ImageBrush.Viewbox</c>-with-
        /// <c>ViewboxUnits=RelativeToBoundingBox</c>-shaped sub-rect into a shared
        /// texture, not a physically cropped image - what lets one exported sprite-sheet
        /// bitmap back several different materials, each one a different cell of it,
        /// the same "one shared bitmap, many named sub-rects" shape <c>JolieCat</c>'s
        /// own 2D sprite-sheet workspace uses.</summary>
        public Vector2 DiffuseTextureOffset { get; set; } = Vector2.Zero;

        /// <summary>The UV-space size (0-1) of the sampled sub-rectangle, starting at
        /// <see cref="DiffuseTextureOffset"/> - (1,1), the default, means "everything
        /// from the offset to the image's own far edge", so a material with only
        /// <see cref="DiffuseTexturePath"/> set still just shows the whole image.</summary>
        public Vector2 DiffuseTextureScale { get; set; } = Vector2.One;

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
