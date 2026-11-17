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
        /// reader. Version 2 added node ids, curves, armatures/bones, skinning,
        /// constraints, material slots, seams and every modifier type; version-1 files
        /// still load (the new fields are simply absent).</summary>
        public int FormatVersion { get; set; } = Jolie3DProjectSerializer.CurrentFormatVersion;

        /// <summary>Id of the active camera node (see <see cref="ProjectNodeData.Id"/>).
        /// Preferred over <see cref="ActiveCameraNodePath"/>, which breaks when two
        /// nodes share a name.</summary>
        public int? ActiveCameraNodeId { get; set; }

        public string SceneName { get; set; } = string.Empty;

        /// <summary>Every root <see cref="ProjectNodeData"/>, in the same order
        /// <see cref="Core.Scene.Scene3D.RootNodes"/> lists them.</summary>
        public List<ProjectNodeData> RootNodes { get; set; } = new();

        /// <summary>The whole <see cref="AnimationTimeline"/>, in
        /// <see cref="AnimationExporter"/>'s own native shape - see this class's own
        /// remarks.</summary>
        public AnimationExportData Animation { get; set; } = new();

        /// <summary>Mirrors <see cref="Core.Scene.Scene3D.ActiveCamera"/> - the same
        /// slash-joined node-path scheme (<see cref="NodePathResolver"/>) the animation
        /// timeline already uses to refer to a node from plain data, rather than a live
        /// reference. Null (the default) means no active camera, matching that
        /// property's own default.</summary>
        public string? ActiveCameraNodePath { get; set; }

        /// <summary>Mirrors <see cref="Core.Scene.EnvironmentSettings.SkyboxSource"/> -
        /// null (the default) means no <see cref="Core.Scene.Scene3D.Environment"/> at
        /// all, matching that property's own default. Flattened straight to the one
        /// string it actually carries rather than a nested object, since
        /// <see cref="Core.Scene.EnvironmentSettings"/> has exactly one field today - the
        /// same "don't nest a whole object for one value" simplicity
        /// <see cref="ActiveCameraNodePath"/> itself already follows.</summary>
        public string? EnvironmentSkyboxSource { get; set; }

        /// <summary>Mirrors <see cref="Core.Scene.EnvironmentSettings.SkyboxRotation"/>.</summary>
        public float EnvironmentSkyboxRotation { get; set; }

        /// <summary>Mirrors <see cref="Core.Scene.EnvironmentSettings.SkyboxStrength"/> (1 when absent).</summary>
        public float EnvironmentSkyboxStrength { get; set; } = 1f;
    }

    /// <summary>One <see cref="Core.Scene.Node"/>: its own local transform, optional
    /// <see cref="Mesh"/>, modifier stack, and child nodes (recursively) - everything
    /// <see cref="Jolie3DProjectSerializer"/> needs to reconstruct an equivalent node,
    /// flattened to plain numbers/strings the same way every other export DTO in this
    /// project already is (see e.g. <c>Animation.KeyframeExportData</c>).</summary>
    public sealed class ProjectNodeData
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>Hidden in the viewport (Node.IsHidden).</summary>
        public bool IsHidden { get; set; }

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

        /// <summary>Linked duplicate: uses the mesh stored on the node with this id.</summary>
        public int? SharedMeshNodeId { get; set; }

        /// <summary>Null unless this node is a camera - mirrors <see cref="Core.Scene.Node.Camera"/>
        /// being optional.</summary>
        public ProjectCameraData? Camera { get; set; }

        /// <summary>Null unless this node is a light - mirrors <see cref="Core.Scene.Node.Light"/>
        /// being optional.</summary>
        public ProjectLightData? Light { get; set; }

        public List<ProjectModifierData> Modifiers { get; set; } = new();

        public List<ProjectNodeData> Children { get; set; } = new();

        /// <summary>Unique within one file (pre-order index). Other records refer to a
        /// node by this id rather than by name.</summary>
        public int Id { get; set; } = -1;

        public ProjectCurveData? Curve { get; set; }

        /// <summary>True when this node is an armature root.</summary>
        public bool IsArmature { get; set; }

        public ProjectBoneData? Bone { get; set; }

        public ProjectSkinData? Skin { get; set; }

        public List<ProjectConstraintData> Constraints { get; set; } = new();
    }

    public sealed class ProjectShapeKeyData
    {
        public string Name { get; set; } = string.Empty;
        public float Value { get; set; }
        /// <summary>Absolute positions, flat xyz.</summary>
        public List<float> Positions { get; set; } = new();
    }

    public sealed class ProjectCurveData
    {
        public List<ProjectCurvePointData> Points { get; set; } = new();
        public bool Closed { get; set; }
        public float BevelDepth { get; set; } = 0.05f;
        public int SegmentsPerSpan { get; set; } = 12;
        public int RadialSegments { get; set; } = 8;
    }

    public sealed class ProjectCurvePointData
    {
        public float[] Position { get; set; } = new float[3];
        public float[] HandleIn { get; set; } = new float[3];
        public float[] HandleOut { get; set; } = new float[3];
    }

    public sealed class ProjectBoneData
    {
        public float[] Head { get; set; } = new float[3];
        public float[] Tail { get; set; } = { 0f, 1f, 0f };

        /// <summary>Row-major 4x4 rest-pose world matrix (16 floats).</summary>
        public float[] RestPoseWorldMatrix { get; set; } = Array.Empty<float>();
    }

    public sealed class ProjectSkinData
    {
        public int? ArmatureNodeId { get; set; }
        public List<int> BoneNodeIds { get; set; } = new();
    }

    public sealed class ProjectConstraintData
    {
        /// <summary>"TrackTo".</summary>
        public string Type { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
        public int? TargetNodeId { get; set; }
        public string? ForwardAxis { get; set; }

        // ---- Inverse Kinematics ----
        public int? PoleNodeId { get; set; }
        public int? ChainLength { get; set; }
        public int? Iterations { get; set; }

        /// <summary>Settings of a parameterized constraint (Copy/Limit ...) by label.</summary>
        public Dictionary<string, float>? Parameters { get; set; }
    }

    /// <summary>A texture painted inside the editor that has no file on disk yet,
    /// stored inline as base64 RGBA8.</summary>
    public sealed class ProjectTextureData
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public string RgbaBase64 { get; set; } = string.Empty;
    }

    /// <summary>One <see cref="Core.Scene.CameraData"/> - field-for-field matching that
    /// class's own properties, with <see cref="Core.Scene.CameraData.ProjectionMode"/>
    /// kept as its own enum name string (the same "readable, no hard-coded enum ints"
    /// convention every other DTO in this project already follows) rather than the raw
    /// underlying integer.</summary>
    public sealed class ProjectCameraData
    {
        /// <summary>"Perspective" or "Orthographic" - see <see cref="Core.Scene.CameraProjectionMode"/>.</summary>
        public string ProjectionMode { get; set; } = nameof(Core.Scene.CameraProjectionMode.Perspective);

        public float FieldOfView { get; set; } = 45f;
        public float OrthographicWidth { get; set; } = 10f;
        public float NearPlaneDistance { get; set; } = 0.1f;
        public float FarPlaneDistance { get; set; } = 1000f;

        public bool DepthOfField { get; set; }
        public float FocusDistance { get; set; } = 5f;
        public float FStop { get; set; } = 2.8f;
    }

    /// <summary>One <see cref="Core.Scene.LightData"/> - field-for-field matching that
    /// class's own properties, with <see cref="Core.Scene.LightData.Type"/> kept as its
    /// own enum name string for the same reason <see cref="ProjectCameraData.ProjectionMode"/>
    /// is.</summary>
    public sealed class ProjectLightData
    {
        /// <summary>"Directional", "Point", or "Spot" - see <see cref="Core.Scene.LightType"/>.</summary>
        public string Type { get; set; } = nameof(Core.Scene.LightType.Directional);

        public float ColorR { get; set; } = 1f;
        public float ColorG { get; set; } = 1f;
        public float ColorB { get; set; } = 1f;
        public float ColorA { get; set; } = 1f;

        public float Intensity { get; set; } = 1f;
        public float Range { get; set; } = 10f;
        public float SpotAngle { get; set; } = 45f;
        public float Width { get; set; } = 1f;
        public float Height { get; set; } = 1f;
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

        /// <summary>Extra materials polygons can reference via <see cref="PolygonMaterialSlots"/>.</summary>
        public List<ProjectMaterialData> MaterialSlots { get; set; } = new();

        /// <summary>Parallel to <see cref="Polygons"/>; -1 = plain <see cref="Material"/>.
        /// Null when every polygon uses the plain material.</summary>
        public List<int>? PolygonMaterialSlots { get; set; }

        /// <summary>UV seam edges as flat pairs [a0, b0, a1, b1, ...].</summary>
        public List<int>? SeamEdges { get; set; }

        /// <summary>Crease edges as flattened index pairs (null = none).</summary>
        public List<int>? CreaseEdges { get; set; }

        /// <summary>Shape key basis positions, flat xyz. Null when the mesh has no basis.</summary>
        public List<float>? ShapeBasis { get; set; }

        public List<ProjectShapeKeyData>? ShapeKeys { get; set; }

        /// <summary>4 bone indices per vertex, flat. Null when the mesh isn't skinned.</summary>
        public List<int>? BoneIndices { get; set; }

        /// <summary>4 bone weights per vertex, flat. Null when the mesh isn't skinned.</summary>
        public List<float>? BoneWeights { get; set; }
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

        /// <summary>Mirrors <see cref="Core.Materials.Material.NormalTexturePath"/> - same
        /// "just a file path, saved/reloaded exactly as authored" simplification
        /// <see cref="DiffuseTexturePath"/>'s own remarks disclose. Null by default, so
        /// every material saved before this property existed reloads with none.</summary>
        public string? NormalTexturePath { get; set; }

        /// <summary>Mirrors <see cref="Core.Materials.Material.MetallicRoughnessTexturePath"/> -
        /// see that property's own remarks on its packed glTF-convention channel layout.</summary>
        public string? MetallicRoughnessTexturePath { get; set; }

        public string? OcclusionTexturePath { get; set; }

        /// <summary>Emission (sRGB rgb); null = none (older files).</summary>
        public float[]? Emissive { get; set; }
        public float EmissiveStrength { get; set; } = 1f;
        public string? EmissiveTexturePath { get; set; }

        /// <summary>"Opaque" / "Blend" / "Clip"; null = Opaque (older files).</summary>
        public string? AlphaMode { get; set; }
        public float AlphaCutoff { get; set; } = 0.5f;
        public bool DoubleSided { get; set; }

        /// <summary>Texture-paint result that only exists in memory.</summary>
        public ProjectTextureData? PaintedTexture { get; set; }

        /// <summary>The material's node tree (plain data: nodes, links, options).</summary>
        public Core.Materials.Nodes.ShaderGraph? NodeGraph { get; set; }
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

        /// <summary>Settings of a parameterized modifier (Displace, Smooth, ...) by label.</summary>
        public Dictionary<string, float>? Parameters { get; set; }

        // ---- Mirror-only (Core.Modifiers.MirrorModifier) ----
        /// <summary><see cref="Core.Modifiers.MirrorAxis"/>'s own name ("X"/"Y"/"Z") -
        /// only meaningful when <see cref="Type"/> is "Mirror".</summary>
        public string? Axis { get; set; }

        /// <summary>Only meaningful when <see cref="Type"/> is "Mirror".</summary>
        public float? WeldThreshold { get; set; }

        // ---- SubdivisionSurface-only (Core.Modifiers.SubdivisionSurfaceModifier) ----
        /// <summary>Only meaningful when <see cref="Type"/> is "SubdivisionSurface".</summary>
        public int? Iterations { get; set; }

        // ---- Boolean ----
        public string? Operation { get; set; }
        public int? TargetNodeId { get; set; }

        // ---- Array ----
        public int? Count { get; set; }
        public float[]? RelativeOffset { get; set; }

        /// <summary>Array: constant offset per copy (null = none).</summary>
        public float[]? ConstantOffset { get; set; }

        /// <summary>Array: merge distance when Merge is on (null = off).</summary>
        public float? MergeDistance { get; set; }

        // ---- Solidify ----
        public float? Thickness { get; set; }

        // ---- Decimate ----
        public float? Ratio { get; set; }

        // ---- Bevel (also uses Iterations for segments, AngleThresholdDegrees) ----
        public float? Width { get; set; }

        // ---- Shrinkwrap (TargetNodeId shared with Boolean) ----
        public string? ShrinkwrapMode { get; set; }
        public float? Offset { get; set; }

        // ---- EdgeSplit ----
        public float? AngleThresholdDegrees { get; set; }
    }
}
