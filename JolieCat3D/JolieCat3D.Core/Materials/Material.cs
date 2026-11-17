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

        /// <summary>Optional path to a tangent-space normal map - null (the default)
        /// means "use the smooth per-vertex <see cref="Vertex.Normal"/> as-is, no
        /// per-pixel surface detail", preserving every existing material's look exactly.
        /// A REAL, structural limitation applies here that doesn't apply to
        /// <see cref="DiffuseTexturePath"/>: WPF's fixed-function <c>Model3D</c> materials
        /// have no per-pixel shader hook at all (see this class's own remarks), so
        /// <c>JolieCat3D.Engine</c>'s live viewport can never actually bump-map a surface
        /// from this - the path is still stored, round-tripped, and (this being the
        /// primary reason it exists at all) written into the exported glTF's own
        /// <c>normalTexture</c> slot (see <c>Service.Export.GltfExporter</c>), where a
        /// real PBR renderer (a modern game engine, a glTF viewer with an actual per-pixel
        /// pipeline) CAN sample it correctly.</summary>
        public string? NormalTexturePath { get; set; }

        /// <summary>Optional path to a single packed metallic-roughness map, in the exact
        /// channel layout the glTF 2.0 "metallicRoughnessTexture" convention already uses
        /// (roughness in the GREEN channel, metallic in the BLUE channel; red/alpha
        /// unused) - a deliberate choice to store it pre-packed in this same layout,
        /// rather than as two separate grayscale textures, so
        /// <c>Service.Export.GltfExporter</c> can hand this straight to glTF with no
        /// repacking step at all (see that class's own remarks). Null (the default) means
        /// "use the flat scalar <see cref="Roughness"/>/<see cref="Metallic"/> values
        /// instead", matching every material authored before this property existed.
        /// <c>JolieCat3D.Engine.Geometry.MaterialFactory</c> cannot sample this per-pixel
        /// either (the same structural WPF limitation <see cref="NormalTexturePath"/>'s
        /// own remarks describe) - it instead averages this image's own G/B channels ONCE
        /// and feeds that single averaged value in place of the scalar
        /// <see cref="Roughness"/>/<see cref="Metallic"/> wherever this is set (see
        /// <c>MaterialFactory.ComputeSpecular</c>'s own remarks), the closest a
        /// fixed-function per-material (not per-pixel) specular calculation can get to
        /// "the texture is actually driving the shading" without a real per-pixel
        /// pipeline to sample it in.</summary>
        public string? MetallicRoughnessTexturePath { get; set; }

        /// <summary>Texture Paint mode's own live, brushable OVERRIDE of this material's
        /// rendered diffuse texture - null (the default, every material before this
        /// feature existed) means "render <see cref="DiffuseTexturePath"/>/<see cref="DiffuseColor"/>
        /// exactly as always". Once set (by <c>Engine.Editing.TexturePaintSession</c>'s
        /// own <c>EnsureTextureBuffer</c>, the first time Texture Paint mode actually
        /// touches this material), it takes over ENTIRELY in place of
        /// <see cref="DiffuseTexturePath"/> for rendering (see
        /// <c>Engine.Geometry.MaterialFactory.CreateDiffuseBrush</c>'s own remarks) -
        /// the same "present means instead-of, not blended-with" precedent
        /// <see cref="DiffuseTexturePath"/> itself already set for <see cref="DiffuseColor"/>.
        /// <see cref="DiffuseTexturePath"/> itself is left completely untouched (still
        /// whatever file, if any, this buffer was originally decoded FROM) - painting
        /// mutates this in-memory buffer only; nothing here writes the result back out
        /// to disk on its own.</summary>
        public TextureBuffer? PaintedTextureBuffer { get; set; }

        /// <summary>Optional ambient-occlusion map (red channel; glTF "occlusionTexture"),
        /// e.g. from Bake Ambient Occlusion. Darkens the ambient light in creases.</summary>
        public string? OcclusionTexturePath { get; set; }

        /// <summary>Light the surface gives off by itself (sRGB colour; black = none).
        /// Unaffected by scene lighting and picked up by bloom. glTF emissiveFactor.</summary>
        public Color4 EmissiveColor { get; set; } = new(0f, 0f, 0f);

        /// <summary>Multiplier on <see cref="EmissiveColor"/> (glTF KHR_materials_emissive_strength), 0..100.</summary>
        public float EmissiveStrength
        {
            get => _emissiveStrength;
            set => _emissiveStrength = Math.Clamp(value, 0f, 100f);
        }
        private float _emissiveStrength = 1f;

        /// <summary>Optional emission map (sRGB), multiplied by <see cref="EmissiveColor"/>.</summary>
        public string? EmissiveTexturePath { get; set; }

        public MaterialAlphaMode AlphaMode { get; set; } = MaterialAlphaMode.Opaque;

        /// <summary>Alpha threshold for <see cref="MaterialAlphaMode.Clip"/>, 0..1 (glTF default 0.5).</summary>
        public float AlphaCutoff
        {
            get => _alphaCutoff;
            set => _alphaCutoff = Math.Clamp(value, 0f, 1f);
        }
        private float _alphaCutoff = 0.5f;

        /// <summary>Both faces visible in a game engine (glTF doubleSided). The editor
        /// viewport always shows both sides.</summary>
        public bool DoubleSided { get; set; }

        /// <summary>The material's node tree (Shader Editor), if it has one. Applying it
        /// bakes texture maps into the fields above; it is kept for further editing.</summary>
        public Nodes.ShaderGraph? NodeGraph { get; set; }

        /// <summary>True when the material is drawn with alpha blending.</summary>
        public bool IsBlended => AlphaMode == MaterialAlphaMode.Blend || (AlphaMode == MaterialAlphaMode.Opaque && Opacity < 1f);

        /// <summary>True when the material emits any light.</summary>
        public bool IsEmissive => EmissiveStrength > 0f && (EmissiveColor.R > 0f || EmissiveColor.G > 0f || EmissiveColor.B > 0f);

        public Material(string name = "Material") => Name = name;

        /// <summary>Copies every parameter of <paramref name="other"/> onto this instance,
        /// keeping this instance's identity (animation tracks key on the instance). The
        /// painted texture buffer reference is copied as-is.</summary>
        public void CopyFrom(Material other)
        {
            ArgumentNullException.ThrowIfNull(other);
            Name = other.Name;
            DiffuseColor = other.DiffuseColor;
            SpecularColor = other.SpecularColor;
            SpecularPower = other.SpecularPower;
            Opacity = other.Opacity;
            Roughness = other.Roughness;
            Metallic = other.Metallic;
            DiffuseTexturePath = other.DiffuseTexturePath;
            DiffuseTextureOffset = other.DiffuseTextureOffset;
            DiffuseTextureScale = other.DiffuseTextureScale;
            NormalTexturePath = other.NormalTexturePath;
            MetallicRoughnessTexturePath = other.MetallicRoughnessTexturePath;
            OcclusionTexturePath = other.OcclusionTexturePath;
            EmissiveColor = other.EmissiveColor;
            EmissiveStrength = other.EmissiveStrength;
            EmissiveTexturePath = other.EmissiveTexturePath;
            AlphaMode = other.AlphaMode;
            AlphaCutoff = other.AlphaCutoff;
            DoubleSided = other.DoubleSided;
            PaintedTextureBuffer = other.PaintedTextureBuffer;
            NodeGraph = other.NodeGraph?.Clone();
        }

        /// <summary>A new, independent material with the same parameters (the painted
        /// texture buffer is shared, not copied).</summary>
        public Material Clone()
        {
            var clone = new Material(Name);
            clone.CopyFrom(this);
            return clone;
        }

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
