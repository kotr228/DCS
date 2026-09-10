using JolieCat3D.Service.Animation;

namespace JolieCat3D.Service.Project
{
    /// <summary>
    /// The plain-data JSON shape <see cref="Jolie3DProjectSerializer"/> reads/writes -
    /// a complete, standalone workspace snapshot ("JolieCat3D Project", conventionally
    /// a ".jolie3d" file): the whole scene graph (every root <see cref="ProjectNodeData"/>
    /// and its descendants), each node's own mesh geometry/material/modifier-stack
    /// configuration, AND the animation timeline (embedded as a plain
    /// <see cref="AnimationExportData"/> - the exact same shape
    /// <c>Animation.AnimationExporter</c>'s own standalone export file uses, just nested
    /// here as one property instead of written to a second file). A plain OBJ/STL export
    /// (see <c>MeshFileService</c>) only ever round-trips raw geometry - no node
    /// hierarchy beyond one-object-per-root, no modifiers, no animation at all - which is
    /// exactly the gap this format exists to close: reopening a ".jolie3d" file restores
    /// the FULL editing session, not just what a mesh interchange format happens to be
    /// able to carry.
    /// </summary>
    public sealed class ProjectFileData
    {
        /// <summary>Bumped only if a future change to this shape would break an older
        /// reader - <see cref="Jolie3DProjectSerializer.Load"/> is the one place that
        /// would ever need to branch on it, and doesn't yet, since this is the format's
        /// first version.</summary>
        public int FormatVersion { get; set; } = 1;

        public string SceneName { get; set; } = string.Empty;

        /// <summary>Every root <see cref="ProjectNodeData"/>, in the same order
        /// <see cref="Core.Scene.Scene3D.RootNodes"/> lists them.</summary>
        public List<ProjectNodeData> RootNodes { get; set; } = new();

        /// <summary>The whole <see cref="AnimationTimeline"/>, in
        /// <see cref="AnimationExporter"/>'s own native shape - see this class's own
        /// remarks.</summary>
        public AnimationExportData Animation { get; set; } = new();
    }

    /// <summary>One <see cref="Core.Scene.Node"/>: its own local transform, optional
    /// <see cref="Mesh"/>, modifier stack, and child nodes (recursively) - everything
    /// <see cref="Jolie3DProjectSerializer"/> needs to reconstruct an equivalent node,
    /// flattened to plain numbers/strings the same way every other export DTO in this
    /// project already is (see e.g. <c>Animation.KeyframeExportData</c>).</summary>
    public sealed class ProjectNodeData
    {
        public string Name { get; set; } = string.Empty;

        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float PositionZ { get; set; }

        public float RotationX { get; set; }
        public float RotationY { get; set; }
        public float RotationZ { get; set; }
        public float RotationW { get; set; } = 1f;

        public float ScaleX { get; set; } = 1f;
        public float ScaleY { get; set; } = 1f;
        public float ScaleZ { get; set; } = 1f;

        /// <summary>Null for a pure grouping/pivot node - mirrors <see cref="Core.Scene.Node.Mesh"/>
        /// being optional.</summary>
        public ProjectMeshData? Mesh { get; set; }

        public List<ProjectModifierData> Modifiers { get; set; } = new();

        public List<ProjectNodeData> Children { get; set; } = new();
    }

    /// <summary>One <see cref="Core.Geometry.Mesh"/>'s complete geometry - every
    /// <see cref="ProjectVertexData"/>, every triangle <see cref="Core.Geometry.Face"/>
    /// (as <see cref="ProjectFaceData"/>), every authoring-time
    /// <see cref="Core.Geometry.Polygon"/> (as its own raw index list - simplest
    /// faithful representation of a variable-length n-gon), and its optional
    /// <see cref="ProjectMaterialData"/>. <see cref="Faces"/> and <see cref="Polygons"/>
    /// are kept as the two SEPARATE lists <see cref="Core.Geometry.Mesh"/> itself keeps
    /// them as (not merged into one triangulated list) - re-opening a saved project and
    /// running Subdivide again needs the same authoring-time n-gons to subdivide, not
    /// whatever a triangulated version of them would already have become.</summary>
    public sealed class ProjectMeshData
    {
        public string Name { get; set; } = string.Empty;

        public List<ProjectVertexData> Vertices { get; set; } = new();

        public List<ProjectFaceData> Faces { get; set; } = new();

        /// <summary>One entry per <see cref="Core.Geometry.Polygon"/>, each entry being
        /// that polygon's own <see cref="Core.Geometry.Polygon.Indices"/> verbatim.</summary>
        public List<List<int>> Polygons { get; set; } = new();

        public ProjectMaterialData? Material { get; set; }
    }

    /// <summary>One <see cref="Core.Geometry.Vertex"/>, flattened to plain floats -
    /// Position/Normal/UV/Color, matching that struct's own fields exactly.</summary>
    public sealed class ProjectVertexData
    {
        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float PositionZ { get; set; }

        public float NormalX { get; set; }
        public float NormalY { get; set; }
        public float NormalZ { get; set; }

        public float UVX { get; set; }
        public float UVY { get; set; }

        public float ColorR { get; set; } = 1f;
        public float ColorG { get; set; } = 1f;
        public float ColorB { get; set; } = 1f;
        public float ColorA { get; set; } = 1f;
    }

    /// <summary>One <see cref="Core.Geometry.Face"/> triangle - three vertex indices.</summary>
    public sealed class ProjectFaceData
    {
        public int A { get; set; }
        public int B { get; set; }
        public int C { get; set; }
    }

    /// <summary>One <see cref="Core.Materials.Material"/>'s complete parameter set -
    /// field-for-field matching that class's own properties.</summary>
    public sealed class ProjectMaterialData
    {
        public string Name { get; set; } = string.Empty;

        public float DiffuseR { get; set; } = 1f;
        public float DiffuseG { get; set; } = 1f;
        public float DiffuseB { get; set; } = 1f;
        public float DiffuseA { get; set; } = 1f;

        public float SpecularR { get; set; } = 1f;
        public float SpecularG { get; set; } = 1f;
        public float SpecularB { get; set; } = 1f;
        public float SpecularA { get; set; } = 1f;

        public double SpecularPower { get; set; } = 30.0;

        public float Opacity { get; set; } = 1f;
        public float Roughness { get; set; } = 0.5f;
        public float Metallic { get; set; }

        /// <summary>Mirrors <see cref="Core.Materials.Material.DiffuseTexturePath"/> -
        /// null means no texture (a flat-shaded material), matching that property's own
        /// default. A path is saved and reloaded EXACTLY as authored (no attempt to make
        /// it relative to the project file, or to embed the image's own bytes) - the
        /// same "just a file path, resolved by whichever layer actually renders it"
        /// simplification that property's own remarks already disclose; a project moved
        /// to a machine without that same path/texture cache present will reopen with a
        /// missing texture, not a crash (whatever already loads a material's texture -
        /// <c>JolieCat3D.Engine.Geometry.MaterialFactory</c> - already has to tolerate a
        /// path that doesn't resolve).</summary>
        public string? DiffuseTexturePath { get; set; }

        public float DiffuseTextureOffsetX { get; set; }
        public float DiffuseTextureOffsetY { get; set; }
        public float DiffuseTextureScaleX { get; set; } = 1f;
        public float DiffuseTextureScaleY { get; set; } = 1f;
    }

    /// <summary>One <see cref="Core.Modifiers.Modifier"/>'s configuration - a small,
    /// hand-written discriminated union (<see cref="Type"/> names which of the fields
    /// below are meaningful) rather than a polymorphic JSON shape with a type-name
    /// discriminator attribute, since there are exactly two concrete modifier types
    /// today and <see cref="Jolie3DProjectSerializer"/> already needs its own explicit
    /// switch over them either way (to call the right constructor back) - see
    /// <see cref="Jolie3DProjectSerializer"/>'s own remarks on why an unrecognized
    /// <see cref="Type"/> on load is a hard failure, not a silently dropped modifier.</summary>
    public sealed class ProjectModifierData
    {
        /// <summary>"Mirror" or "SubdivisionSurface" - <see cref="Core.Modifiers.Modifier.Name"/>
        /// is a user-facing display label, not a stable machine-readable type tag, so
        /// this is its own separate, deliberately-chosen string instead.</summary>
        public string Type { get; set; } = string.Empty;

        public bool IsEnabled { get; set; } = true;

        // ---- Mirror-only (Core.Modifiers.MirrorModifier) ----
        /// <summary><see cref="Core.Modifiers.MirrorAxis"/>'s own name ("X"/"Y"/"Z") -
        /// only meaningful when <see cref="Type"/> is "Mirror".</summary>
        public string? Axis { get; set; }

        /// <summary>Only meaningful when <see cref="Type"/> is "Mirror".</summary>
        public float? WeldThreshold { get; set; }

        // ---- SubdivisionSurface-only (Core.Modifiers.SubdivisionSurfaceModifier) ----
        /// <summary>Only meaningful when <see cref="Type"/> is "SubdivisionSurface".</summary>
        public int? Iterations { get; set; }
    }
}
