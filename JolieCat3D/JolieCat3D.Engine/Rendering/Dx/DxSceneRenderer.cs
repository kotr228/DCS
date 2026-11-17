// Vortice annotates many D3D parameters as non-nullable, but D3D11 explicitly accepts
// null to unbind a resource/shader/state slot - so null is passed on purpose here.
#pragma warning disable CS8604, CS8625

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using JolieCat3D.Core.Constraints;
using JolieCat3D.Core.Scene;
using JolieCat3D.Engine.Lighting;
using JolieCat3D.Engine.Viewport;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using CoreColor4 = JolieCat3D.Core.Numerics.Color4;
using CoreMaterial = JolieCat3D.Core.Materials.Material;
using MapFlags = Vortice.Direct3D11.MapFlags;

namespace JolieCat3D.Engine.Rendering.Dx
{
    /// <summary>Everything about how the viewport draws, independent of the camera.</summary>
    public sealed class ViewportRenderSettings
    {
        public ShadingMode ShadingMode { get; set; } = ShadingMode.Rendered;
        public bool ShowGrid { get; set; } = true;
        public float GridSize { get; set; } = 1f;
        public bool ShowShadows { get; set; } = true;
        public bool ShowAmbientOcclusion { get; set; } = true;
        public bool AntiAliasing { get; set; } = true;
        public bool Bloom { get; set; }
        public bool ShowOverlays { get; set; } = true;
        /// <summary>Edges drawn over the shaded surfaces (Blender's Overlays > Wireframe).</summary>
        public bool WireframeOverlay { get; set; }
        public LightingSettings Lighting { get; set; } = LightingSettings.CreateDefault();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal unsafe struct FrameConstants
    {
        public Matrix4x4 View, Proj, ViewProj, InvViewProj, LightViewProj;
        public Vector3 CameraPos; public float ShadowsEnabled;
        public Vector2 ViewportSize; public float AoEnabled; public float ShadingMode;
        public Vector3 AmbientColor; public float LightCount;
        public Vector3 KeyLightDir; public float IsOrtho;
        public Vector3 KeyLightColor; public float GridSize;
        public Vector3 BackgroundTop; public float HasSkybox;
        public Vector3 BackgroundBottom; public float GridMajorEvery;
        public Vector3 EnvTint; public float HasEnvTint;
        public Vector3 GridOrigin; public float GridPlane;
        public fixed float EnvSh[36];   // 9 x float4: irradiance SH of the world
        public float EnvMaxMip; public float EnvIntensity; public float EnvPad0; public float EnvPad1;
        public fixed float Lights[DxShaders.MaxLights * 16];
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct ObjectConstants
    {
        public Matrix4x4 World, WorldNormal;
        public Vector4 BaseColor;
        public float Roughness, Metallic, UseVertexColor, Heatmap;
        public float HasDiffuseTex, HasNormalTex, HasMRTex, Unlit;
        public Vector2 UvOffset, UvScale;
        public Vector4 Tint;
        public float HasAoTex, HasEmissiveTex, AlphaCutoff, ObjectPad2;
        public Vector4 Emissive;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct PostConstants
    {
        public Vector2 TexelDir; public float BloomThreshold; public float BloomIntensity;
        public Vector4 OutlineColor;
        public Vector4 ActiveOutlineColor;
        public float BloomEnabled, OutlineEnabled, Pad0, Pad1;
    }

    /// <summary>
    /// The DirectX 11 viewport renderer. <see cref="Prepare"/> turns a scene into a list of
    /// GPU draw items (only when the scene changes); <see cref="Draw"/> renders that list
    /// for a camera (every frame while navigating). Output goes to <see cref="Output"/>,
    /// a BGRA8 texture that can be shared with WPF's D3DImage or read back for captures.
    /// </summary>
    internal sealed class DxSceneRenderer : IDisposable
    {
        private const int ShadowMapSize = 2048;
        private static readonly Vector4 SelectionColor = Srgb(0xC2, 0x9B, 0x58); // JolieCat gold
        private static readonly Vector4 ActiveSelectionColor = Srgb(0xF0, 0xC8, 0x7A);
        private static readonly Vector3 BackgroundTopSrgb = new(0x2A / 255f, 0x26 / 255f, 0x25 / 255f);
        private static readonly Vector3 BackgroundBottomSrgb = new(0x17 / 255f, 0x15 / 255f, 0x14 / 255f);

        private readonly DxGraphics _gfx;
        private readonly bool _sharedOutput;
        private readonly SceneMeshCache _meshes;
        private readonly TextureCache _textures;
        private readonly ID3D11Buffer _frameCb;
        private readonly ID3D11Buffer _objectCb;
        private readonly ID3D11Buffer _postCb;
        private readonly DynamicBuffer _lineBuffer;
        private readonly DynamicBuffer _pointBuffer;
        private readonly DynamicBuffer _triBuffer;

        private RenderTexture? _sceneMsaa, _depthMsaa, _sceneResolved, _mask, _normalDepth, _prepassDepth, _aoRaw, _ao, _bloomA, _bloomB;
        private RenderTexture? _shadowMap;
        private RenderTexture? _output;
        private int _width, _height, _samples;

        // ---------------- prepared frame ----------------
        private readonly List<DrawItem> _opaque = new();
        private readonly List<DrawItem> _transparent = new();
        private readonly List<DrawItem> _selected = new();
        private readonly List<(GpuMesh Mesh, Matrix4x4 World, bool Selected)> _wire = new();
        private readonly List<PickTarget> _pickTargets = new();
        private readonly List<Node> _helperNodes = new();
        private readonly OverlayDrawList _helpers = new();
        private FrameConstants _frame;
        private Vector3 _sceneMin, _sceneMax;
        private bool _hasShadowCaster;
        private Vector3 _shadowLightDir;
        private SkyboxResource? _skybox;
        private float _skyStrength = 1f;

        private sealed class DrawItem
        {
            public Node Node = null!;
            public GpuMesh Mesh = null!;
            public Matrix4x4 World;
            public DrawGroup Group;
            public ObjectConstants Constants;
            public ID3D11ShaderResourceView? Diffuse, Normal, MetallicRoughness, Occlusion, Emission;
            public bool Blended;
            public bool IsActive;
        }

        public ViewportRenderSettings Settings { get; set; } = new();

        /// <summary>The object being edited in Edit Mode: drawn without shape keys so its
        /// vertices line up with the edit overlay.</summary>
        public Node? EditTarget { get; set; }

        public RenderTexture? Output => _output;

        public event Action? OutputRecreated;

        public DxSceneRenderer(DxGraphics gfx, bool sharedOutput)
        {
            _gfx = gfx;
            _sharedOutput = sharedOutput;
            _meshes = new SceneMeshCache(gfx);
            _textures = new TextureCache(gfx);
            _frameCb = gfx.CreateConstantBuffer<FrameConstants>();
            _objectCb = gfx.CreateConstantBuffer<ObjectConstants>();
            _postCb = gfx.CreateConstantBuffer<PostConstants>();
            _lineBuffer = new DynamicBuffer(gfx, BindFlags.VertexBuffer);
            _pointBuffer = new DynamicBuffer(gfx, BindFlags.VertexBuffer);
            _triBuffer = new DynamicBuffer(gfx, BindFlags.VertexBuffer);
        }

        internal readonly record struct PickTarget(Node Node, GpuMesh Mesh, Matrix4x4 World);

        /// <summary>Every drawn mesh with its world matrix, for CPU picking.</summary>
        internal IReadOnlyList<PickTarget> PickTargets => _pickTargets;

        /// <summary>Mesh-less nodes drawn as helpers (cameras), picked by their origin.</summary>
        internal IReadOnlyList<Node> HelperNodes => _helperNodes;

        /// <summary>The evaluated (modifiers + skinning) mesh of each drawn node, used for picking.</summary>
        internal IReadOnlyDictionary<Node, GpuMesh> MeshEntries => _meshes.Entries;

        public (Vector3 Min, Vector3 Max) SceneBounds => (_sceneMin, _sceneMax);

        // =====================================================================
        // Prepare
        // =====================================================================

        /// <summary>Rebuilds the draw list from <paramref name="scene"/>. Call when the scene changes.</summary>
        public void Prepare(Scene3D? scene, IReadOnlyCollection<Node> selection, Node? active, Node? weightPaintTarget, int weightPaintBone, Node? activeCamera, Node? hiddenCamera)
        {
            _opaque.Clear();
            _transparent.Clear();
            _selected.Clear();
            _wire.Clear();
            _pickTargets.Clear();
            _helperNodes.Clear();
            _helpers.Clear();
            _sceneMin = new Vector3(float.MaxValue);
            _sceneMax = new Vector3(float.MinValue);
            _skybox = null;

            var lights = new List<(Node Node, LightData Light)>();
            var selectedSet = new HashSet<Node>(ReferenceEqualityComparer.Instance);
            foreach (var root in selection) foreach (var n in root.Traverse()) selectedSet.Add(n);
            var activeSet = new HashSet<Node>(ReferenceEqualityComparer.Instance);
            if (active is not null) foreach (var n in active.Traverse()) activeSet.Add(n);

            _meshes.BeginFrame();
            if (scene is not null)
            {
                try { ConstraintSolver.Apply(scene); } catch (Exception) { /* a bad constraint must not stop drawing */ }

                foreach (var node in scene.Traverse())
                {
                    if (node.Light is { } light) lights.Add((node, light));
                    if (node.IsHidden) continue; // hidden lights still light the scene
                    if (node.Camera is not null && !ReferenceEquals(node, hiddenCamera))
                    {
                        AddCameraHelper(node, ReferenceEquals(node, activeCamera), selectedSet.Contains(node));
                        _helperNodes.Add(node);
                    }
                    if (node.Light is { } l) AddLightHelper(node, l, selectedSet.Contains(node));

                    var heatBone = ReferenceEquals(node, weightPaintTarget) ? Math.Max(0, weightPaintBone) : -1;
                    if (_meshes.Get(node, heatBone, applyShapeKeys: !ReferenceEquals(node, EditTarget)) is not { TotalIndices: > 0 } gpu) continue;

                    var world = node.GetWorldTransform();
                    ExpandBounds(gpu.LocalMin, gpu.LocalMax, world);
                    var isSelected = selectedSet.Contains(node);
                    _wire.Add((gpu, world, isSelected));
                    _pickTargets.Add(new PickTarget(node, gpu, world));

                    foreach (var group in gpu.Groups)
                    {
                        var item = BuildItem(node, gpu, world, group);
                        item.IsActive = activeSet.Contains(node);
                        if (item.Blended && Settings.ShadingMode >= ShadingMode.Material) _transparent.Add(item);
                        else _opaque.Add(item);
                        if (isSelected) _selected.Add(item);
                    }
                }

                _skybox = _textures.GetSkybox(scene.Environment?.SkyboxSource, scene.Environment?.SkyboxRotation ?? 0f);
                _skyStrength = MathF.Max(0f, scene.Environment?.SkyboxStrength ?? 1f);
            }
            _meshes.EndFrame();

            if (_sceneMin.X > _sceneMax.X) { _sceneMin = Vector3.Zero; _sceneMax = Vector3.Zero; }

            BuildLightConstants(lights);
        }

        private DrawItem BuildItem(Node node, GpuMesh gpu, Matrix4x4 world, DrawGroup group)
        {
            Matrix4x4.Invert(world, out var inv);
            var material = group.Material;
            var item = new DrawItem { Node = node, Mesh = gpu, World = world, Group = group };
            var c = new ObjectConstants
            {
                World = world,
                WorldNormal = Matrix4x4.Transpose(inv),
                BaseColor = new Vector4(0.8f, 0.8f, 0.8f, 1f),
                Roughness = 0.5f,
                Metallic = 0f,
                UseVertexColor = gpu.HasVertexColors ? 1 : 0,
                Heatmap = gpu.IsHeatmap ? 1 : 0,
                UvOffset = Vector2.Zero,
                UvScale = Vector2.One,
            };

            if (material is not null)
            {
                c.BaseColor = new Vector4(material.DiffuseColor.R, material.DiffuseColor.G, material.DiffuseColor.B, Math.Clamp(material.Opacity * material.DiffuseColor.A, 0f, 1f));
                c.Roughness = Math.Clamp(material.Roughness, 0.02f, 1f);
                c.Metallic = Math.Clamp(material.Metallic, 0f, 1f);
                c.UvOffset = material.DiffuseTextureOffset;
                c.UvScale = material.DiffuseTextureScale;

                item.Diffuse = material.PaintedTextureBuffer is { } painted
                    ? _textures.GetPainted(painted)
                    : _textures.GetFile(material.DiffuseTexturePath, srgb: true);
                item.Normal = _textures.GetFile(material.NormalTexturePath, srgb: false);
                item.MetallicRoughness = _textures.GetFile(material.MetallicRoughnessTexturePath, srgb: false);
                c.HasDiffuseTex = item.Diffuse is null ? 0 : 1;
                c.HasNormalTex = item.Normal is null ? 0 : 1;
                c.HasMRTex = item.MetallicRoughness is null ? 0 : 1;
                item.Occlusion = _textures.GetFile(material.OcclusionTexturePath, srgb: false);
                c.HasAoTex = item.Occlusion is null ? 0 : 1;
                if (material.IsEmissive)
                {
                    c.Emissive = new Vector4(material.EmissiveColor.R, material.EmissiveColor.G, material.EmissiveColor.B, material.EmissiveStrength);
                    item.Emission = _textures.GetFile(material.EmissiveTexturePath, srgb: true);
                    c.HasEmissiveTex = item.Emission is null ? 0 : 1;
                }
                if (material.AlphaMode == JolieCat3D.Core.Materials.MaterialAlphaMode.Clip) c.AlphaCutoff = Math.Max(material.AlphaCutoff, 0.001f);
                item.Blended = material.IsBlended && c.BaseColor.W < 0.999f || material.AlphaMode == JolieCat3D.Core.Materials.MaterialAlphaMode.Blend;
                // A texture fully replaces the base colour unless the colour was customised.
                if (item.Diffuse is not null && IsWhite(material.DiffuseColor)) c.BaseColor = new Vector4(1, 1, 1, c.BaseColor.W);
            }

            // Light/camera/bone helper meshes read better unlit.
            if (node.Light is not null || node.Bone is not null) c.Unlit = 1;

            item.Constants = c;
            return item;
        }

        private static bool IsWhite(CoreColor4 c) => c.R > 0.999f && c.G > 0.999f && c.B > 0.999f;

        private void ExpandBounds(Vector3 min, Vector3 max, Matrix4x4 world)
        {
            for (var i = 0; i < 8; i++)
            {
                var p = new Vector3((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z);
                var w = Vector3.Transform(p, world);
                _sceneMin = Vector3.Min(_sceneMin, w);
                _sceneMax = Vector3.Max(_sceneMax, w);
            }
        }

        private unsafe void BuildLightConstants(List<(Node Node, LightData Light)> lights)
        {
            var settings = Settings.Lighting;
            // Put the first directional light first: it is the one that casts shadows.
            var ordered = lights.OrderBy(l => l.Light.Type == LightType.Directional ? 0 : 1).Take(DxShaders.MaxLights).ToList();

            _frame.LightCount = ordered.Count;
            for (var i = 0; i < ordered.Count; i++)
            {
                var (node, light) = ordered[i];
                var col = SrgbToLinear(new Vector3(light.Color.R, light.Color.G, light.Color.B)) * MathF.Max(0, light.Intensity) * MathF.PI;
                // Area lights: a near-hemispherical spot (one-sided) from the rectangle's centre.
                var type = light.Type switch { LightType.Point => 1f, LightType.Spot or LightType.Area => 2f, _ => 0f };
                var pos = node.GetWorldPosition();
                var dir = node.GetWorldForward();
                var half = Math.Clamp(light.Type == LightType.Area ? 179f : light.SpotAngle, 1f, 179f) * MathF.PI / 360f;
                var o = i * 16;
                fixed (float* l = _frame.Lights)
                {
                    l[o + 0] = col.X; l[o + 1] = col.Y; l[o + 2] = col.Z; l[o + 3] = type;
                    l[o + 4] = pos.X; l[o + 5] = pos.Y; l[o + 6] = pos.Z; l[o + 7] = MathF.Max(0.01f, light.Range);
                    l[o + 8] = dir.X; l[o + 9] = dir.Y; l[o + 10] = dir.Z; l[o + 11] = MathF.Cos(half);
                    l[o + 12] = MathF.Cos(half * 0.6f);
                }
            }

            var useSceneLights = Settings.ShadingMode == ShadingMode.Rendered && ordered.Count > 0;
            if (useSceneLights)
            {
                _hasShadowCaster = ordered[0].Light.Type == LightType.Directional;
                _shadowLightDir = ordered[0].Node.GetWorldForward();
            }
            else
            {
                _hasShadowCaster = true;
                _shadowLightDir = settings.DirectionalLightDirection;
            }

            _frame.AmbientColor = SrgbToLinear(new Vector3(settings.AmbientLightColor.R, settings.AmbientLightColor.G, settings.AmbientLightColor.B)) * 2.2f;
            _frame.KeyLightDir = Vector3.Normalize(settings.DirectionalLightDirection);
            _frame.KeyLightColor = SrgbToLinear(new Vector3(settings.DirectionalLightColor.R, settings.DirectionalLightColor.G, settings.DirectionalLightColor.B));
        }

        // ---------------- helper shapes for cameras and lights ----------------

        private static readonly CoreColor4 HelperColor = new(0.55f, 0.55f, 0.58f);
        private static readonly CoreColor4 HelperSelected = new(0xC2 / 255f, 0x9B / 255f, 0x58 / 255f);
        private static readonly CoreColor4 ActiveCameraColor = new(0.35f, 0.75f, 0.95f);

        private void AddCameraHelper(Node node, bool isActive, bool isSelected)
        {
            var data = node.Camera!;
            var world = node.GetWorldTransform();
            Matrix4x4.Decompose(world, out _, out var rot, out var pos);
            var forward = Vector3.Transform(Vector3.UnitZ, rot);
            var up = Vector3.Transform(Vector3.UnitY, rot);
            var right = Vector3.Cross(forward, up);
            const float depth = 1.0f;
            float halfH, halfW;
            if (data.ProjectionMode == CameraProjectionMode.Orthographic)
            {
                halfH = 0.5f; halfW = 0.5f * 1.5f;
            }
            else
            {
                halfH = MathF.Tan(data.FieldOfView * MathF.PI / 360f) * depth;
                halfW = halfH * 1.5f;
            }

            var color = isSelected ? HelperSelected : isActive ? ActiveCameraColor : HelperColor;
            var width = isSelected ? 2f : 1.5f;
            var c = pos + forward * depth;
            var p0 = c + right * halfW + up * halfH;
            var p1 = c - right * halfW + up * halfH;
            var p2 = c - right * halfW - up * halfH;
            var p3 = c + right * halfW - up * halfH;
            _helpers.AddPolyline(new[] { p0, p1, p2, p3 }, color, width, closed: true);
            foreach (var p in new[] { p0, p1, p2, p3 }) _helpers.AddLine(pos, p, color, width);
            // "Up" triangle like Blender's camera.
            var t0 = c + up * (halfH * 1.15f) + right * (halfW * 0.4f);
            var t1 = c + up * (halfH * 1.15f) - right * (halfW * 0.4f);
            var t2 = c + up * (halfH * 1.6f);
            if (isActive) _helpers.AddTriangle(t0, t1, t2, color);
            else _helpers.AddPolyline(new[] { t0, t1, t2 }, color, width, closed: true);
        }

        private void AddLightHelper(Node node, LightData light, bool isSelected)
        {
            var pos = node.GetWorldPosition();
            var dir = Vector3.Normalize(node.GetWorldForward());
            var color = isSelected ? HelperSelected : HelperColor;
            switch (light.Type)
            {
                case LightType.Directional:
                    _helpers.AddLine(pos, pos + dir * 2.5f, color, 1.5f);
                    break;
                case LightType.Spot:
                    var len = MathF.Min(light.Range, 3f);
                    var radius = MathF.Tan(Math.Clamp(light.SpotAngle, 1f, 179f) * MathF.PI / 360f) * len;
                    var (u, v) = OverlayDrawList.Basis(dir);
                    var end = pos + dir * len;
                    _helpers.AddCircle(end, dir, radius, color, 1.5f, segments: 32);
                    for (var i = 0; i < 4; i++)
                    {
                        var a = i * MathF.PI / 2f;
                        _helpers.AddLine(pos, end + (u * MathF.Cos(a) + v * MathF.Sin(a)) * radius, color, 1.2f);
                    }
                    break;
                case LightType.Area:
                {
                    var world = node.GetWorldTransform();
                    var hw = light.Width / 2f;
                    var hh = light.Height / 2f;
                    var corners = new[] { new Vector3(-hw, -hh, 0), new Vector3(hw, -hh, 0), new Vector3(hw, hh, 0), new Vector3(-hw, hh, 0) }
                        .Select(c => Vector3.Transform(c, world)).ToArray();
                    for (var i = 0; i < 4; i++) _helpers.AddLine(corners[i], corners[(i + 1) % 4], color, 1.5f);
                    _helpers.AddLine(pos, pos + dir * MathF.Max(0.5f, MathF.Max(light.Width, light.Height)), color, 1.2f);
                    break;
                }
                case LightType.Point when isSelected:
                    _helpers.AddCircle(pos, Vector3.UnitY, light.Range, new CoreColor4(color.R, color.G, color.B, 0.5f), 1f, segments: 48);
                    break;
            }
        }

        // =====================================================================
        // Draw
        // =====================================================================

        public void Draw(ViewportCamera camera, int width, int height, OverlayDrawList? overlay)
        {
            width = Math.Max(1, width);
            height = Math.Max(1, height);
            var samples = Settings.AntiAliasing ? 4 : 1;
            EnsureTargets(width, height, samples);

            var ctx = _gfx.Context;
            ctx.ClearState();

            var aspect = width / (float)height;
            var view = camera.GetView();
            var proj = camera.GetProjection(aspect);
            var viewProj = view * proj;
            Matrix4x4.Invert(viewProj, out var invViewProj);

            var shadows = Settings.ShowShadows && Settings.ShadingMode != ShadingMode.Wireframe && _hasShadowCaster && _opaque.Count > 0;
            var ao = Settings.ShowAmbientOcclusion && Settings.ShadingMode != ShadingMode.Wireframe && _opaque.Count > 0;

            _frame.View = view;
            _frame.Proj = proj;
            _frame.ViewProj = viewProj;
            _frame.InvViewProj = invViewProj;
            _frame.LightViewProj = shadows ? ComputeLightViewProj() : Matrix4x4.Identity;
            _frame.CameraPos = camera.Position;
            _frame.ShadowsEnabled = shadows ? 1 : 0;
            _frame.ViewportSize = new Vector2(width, height);
            _frame.AoEnabled = ao ? 1 : 0;
            _frame.ShadingMode = (float)Settings.ShadingMode;
            _frame.IsOrtho = camera.IsOrthographic ? 1 : 0;
            _frame.GridSize = MathF.Max(0.001f, Settings.GridSize);
            _frame.GridMajorEvery = 10f;
            _frame.BackgroundTop = BackgroundTopSrgb;
            _frame.BackgroundBottom = BackgroundBottomSrgb;
            _frame.HasSkybox = _skybox is not null && Settings.ShadingMode >= ShadingMode.Material ? 1 : 0;
            _frame.HasEnvTint = 0;
            if (_skybox is { } sky)
            {
                unsafe
                {
                    for (var i = 0; i < 9; i++)
                    {
                        _frame.EnvSh[i * 4] = sky.IrradianceSh[i].X;
                        _frame.EnvSh[i * 4 + 1] = sky.IrradianceSh[i].Y;
                        _frame.EnvSh[i * 4 + 2] = sky.IrradianceSh[i].Z;
                        _frame.EnvSh[i * 4 + 3] = 0;
                    }
                }
                _frame.EnvMaxMip = sky.MipCount - 1;
                _frame.EnvIntensity = _skyStrength;
            }
            // Blender-style: in axis-aligned ortho side views the grid lies in the view plane.
            var forward = camera.Forward;
            _frame.GridPlane = 0;
            if (camera.IsOrthographic && MathF.Abs(forward.Y) < 0.2f)
                _frame.GridPlane = MathF.Abs(forward.Z) >= MathF.Abs(forward.X) ? 1 : 2;
            _frame.GridOrigin = camera.Target;
            _gfx.Upload(_frameCb, _frame);

            ctx.VSSetConstantBuffer(0, _frameCb);
            ctx.PSSetConstantBuffer(0, _frameCb);
            ctx.VSSetConstantBuffer(1, _objectCb);
            ctx.PSSetConstantBuffer(1, _objectCb);
            ctx.PSSetConstantBuffer(2, _postCb);
            ctx.PSSetSampler(0, _gfx.LinearWrap);
            ctx.PSSetSampler(1, _gfx.LinearClamp);
            ctx.PSSetSampler(2, _gfx.ShadowCompare);
            ctx.PSSetSampler(3, _gfx.PointClamp);

            if (shadows) DrawShadowPass();
            if (ao) DrawAoPasses(width, height);

            DrawMainPass(width, height, shadows, ao, overlay);

            // Resolve MSAA.
            if (_sceneMsaa!.Samples > 1) ctx.ResolveSubresource(_sceneResolved!.Texture, 0, _sceneMsaa.Texture, 0, Format.R16G16B16A16_Float);
            else ctx.CopyResource(_sceneResolved!.Texture, _sceneMsaa.Texture);

            var outline = Settings.ShowOverlays && _selected.Count > 0;
            if (outline) DrawMaskPass(width, height);
            if (Settings.Bloom) DrawBloom(width, height);
            DrawComposite(width, height, outline);

            ctx.Flush();
        }

        private Matrix4x4 ComputeLightViewProj()
        {
            var center = (_sceneMin + _sceneMax) * 0.5f;
            var radius = MathF.Max(0.5f, (_sceneMax - _sceneMin).Length() * 0.5f);
            // Include the ground under the objects so their shadows land on the grid.
            center.Y = MathF.Min(center.Y, (_sceneMin.Y + _sceneMax.Y) * 0.5f);
            radius *= 1.6f;
            var dir = Vector3.Normalize(_shadowLightDir.LengthSquared() > 1e-6f ? _shadowLightDir : new Vector3(-0.5f, -1, -0.5f));
            var up = MathF.Abs(dir.Y) > 0.95f ? Vector3.UnitZ : Vector3.UnitY;
            var eye = center - dir * radius * 2f;
            var view = Matrix4x4.CreateLookAt(eye, center, up);
            var proj = Matrix4x4.CreateOrthographic(radius * 2f, radius * 2f, 0.01f, radius * 4f);
            return view * proj;
        }

        private void DrawShadowPass()
        {
            var ctx = _gfx.Context;
            _shadowMap ??= RenderTexture.CreateDepth(_gfx, ShadowMapSize, ShadowMapSize, 1, shaderResource: true);
            ctx.OMSetRenderTargets((ID3D11RenderTargetView)null!, _shadowMap.Dsv);
            ctx.ClearDepthStencilView(_shadowMap.Dsv!, DepthStencilClearFlags.Depth, 1f, 0);
            ctx.RSSetViewport(0, 0, ShadowMapSize, ShadowMapSize, 0, 1);
            ctx.RSSetState(_gfx.ShadowRaster);
            ctx.OMSetDepthStencilState(_gfx.DepthDefault, 0);
            ctx.OMSetBlendState(_gfx.Opaque);
            ctx.IASetInputLayout(_gfx.MeshLayout);
            ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            foreach (var item in _opaque)
            {
                if (item.Constants.Unlit > 0.5f) continue; // helpers don't cast shadows
                var clip = item.Constants.AlphaCutoff > 0;
                ctx.VSSetShader(clip ? _gfx.ShadowClipVS : _gfx.ShadowVS);
                ctx.PSSetShader(clip ? _gfx.ShadowClipPS : null);
                if (clip) ctx.PSSetShaderResource(0, item.Diffuse);
                DrawItemGeometry(item);
            }
            ctx.PSSetShader(null);
            ctx.OMSetRenderTargets((ID3D11RenderTargetView)null!, null);
        }

        private void DrawAoPasses(int width, int height)
        {
            var ctx = _gfx.Context;
            ctx.OMSetRenderTargets(_normalDepth!.Rtv!, _prepassDepth!.Dsv);
            ctx.ClearRenderTargetView(_normalDepth.Rtv!, new Vortice.Mathematics.Color4(0, 0, 0, 0));
            ctx.ClearDepthStencilView(_prepassDepth.Dsv!, DepthStencilClearFlags.Depth, 1f, 0);
            ctx.RSSetViewport(0, 0, width, height, 0, 1);
            ctx.RSSetState(_gfx.CullNone);
            ctx.OMSetDepthStencilState(_gfx.DepthDefault, 0);
            ctx.OMSetBlendState(_gfx.Opaque);
            ctx.IASetInputLayout(_gfx.MeshLayout);
            ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            ctx.VSSetShader(_gfx.PrepassVS);
            ctx.PSSetShader(_gfx.PrepassPS);
            foreach (var item in _opaque)
            {
                if (item.Constants.Unlit > 0.5f) continue;
                if (item.Constants.AlphaCutoff > 0) ctx.PSSetShaderResource(0, item.Diffuse);
                DrawItemGeometry(item);
            }

            // SSAO
            ctx.OMSetRenderTargets(_aoRaw!.Rtv!, null);
            ctx.RSSetViewport(0, 0, _aoRaw.Width, _aoRaw.Height, 0, 1);
            ctx.OMSetDepthStencilState(_gfx.DepthNone, 0);
            ctx.PSSetShaderResource(6, _normalDepth.Srv);
            FullscreenPass(_gfx.SsaoPS);

            ctx.OMSetRenderTargets(_ao!.Rtv!, null);
            ctx.RSSetViewport(0, 0, _ao.Width, _ao.Height, 0, 1);
            ctx.PSSetShaderResource(7, _aoRaw.Srv);
            FullscreenPass(_gfx.BlurAoPS);
            ctx.PSSetShaderResource(6, null);
            ctx.PSSetShaderResource(7, null);
            ctx.OMSetRenderTargets((ID3D11RenderTargetView)null!, null);
        }

        private void DrawMainPass(int width, int height, bool shadows, bool ao, OverlayDrawList? overlay)
        {
            var ctx = _gfx.Context;
            ctx.OMSetRenderTargets(_sceneMsaa!.Rtv!, _depthMsaa!.Dsv);
            ctx.ClearRenderTargetView(_sceneMsaa.Rtv!, new Vortice.Mathematics.Color4(0, 0, 0, 1));
            ctx.ClearDepthStencilView(_depthMsaa.Dsv!, DepthStencilClearFlags.Depth, 1f, 0);
            ctx.RSSetViewport(0, 0, width, height, 0, 1);
            ctx.RSSetState(_sceneMsaa.Samples > 1 ? _gfx.CullNoneMsaa : _gfx.CullNone);

            // Background
            ctx.OMSetDepthStencilState(_gfx.DepthNone, 0);
            ctx.OMSetBlendState(_gfx.Opaque);
            ctx.PSSetShaderResource(5, _frame.HasSkybox > 0.5f ? _skybox?.Srv : null);
            FullscreenPass(_gfx.BackgroundPS);

            ctx.PSSetShaderResource(3, shadows ? _shadowMap!.Srv : null);
            ctx.PSSetShaderResource(4, ao ? _ao!.Srv : null);

            if (Settings.ShadingMode != ShadingMode.Wireframe)
            {
                // Opaque meshes
                ctx.OMSetDepthStencilState(_gfx.DepthDefault, 0);
                ctx.IASetInputLayout(_gfx.MeshLayout);
                ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
                ctx.VSSetShader(_gfx.MeshVS);
                ctx.PSSetShader(_gfx.MeshPS);
                foreach (var item in _opaque) DrawLitItem(item);
            }

            // Grid
            if (Settings.ShowGrid)
            {
                ctx.OMSetDepthStencilState(_gfx.DepthRead, 0);
                ctx.OMSetBlendState(_gfx.Premultiplied);
                ctx.IASetInputLayout(null);
                ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
                ctx.VSSetShader(_gfx.GridVS);
                ctx.PSSetShader(_gfx.GridPS);
                ctx.Draw(6, 0);
            }

            if (Settings.ShadingMode != ShadingMode.Wireframe && _transparent.Count > 0)
            {
                // Transparent meshes, back to front.
                var cam = _frame.CameraPos;
                ctx.OMSetDepthStencilState(_gfx.DepthRead, 0);
                ctx.OMSetBlendState(_gfx.AlphaBlend);
                ctx.IASetInputLayout(_gfx.MeshLayout);
                ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
                ctx.VSSetShader(_gfx.MeshVS);
                ctx.PSSetShader(_gfx.MeshPS);
                foreach (var item in _transparent.OrderByDescending(t => Vector3.DistanceSquared(cam, t.World.Translation)))
                    DrawLitItem(item);
            }

            if (Settings.ShadingMode == ShadingMode.Wireframe) DrawWireframe();
            else if (Settings.WireframeOverlay) DrawWireframe(overlay: true);

            ctx.PSSetShaderResource(3, null);
            ctx.PSSetShaderResource(4, null);

            // Overlays: helpers and scene-layer overlay depth tested, then on-top layer.
            if (Settings.ShowOverlays)
            {
                DrawOverlay(_helpers, OverlayLayer.Scene);
                if (overlay is not null) DrawOverlay(overlay, OverlayLayer.Scene);
                ctx.ClearDepthStencilView(_depthMsaa.Dsv!, DepthStencilClearFlags.Depth, 1f, 0);
                DrawOverlay(_helpers, OverlayLayer.OnTop);
                if (overlay is not null) DrawOverlay(overlay, OverlayLayer.OnTop);
            }

            ctx.OMSetRenderTargets((ID3D11RenderTargetView)null!, null);
        }

        private void DrawLitItem(DrawItem item)
        {
            var ctx = _gfx.Context;
            _gfx.Upload(_objectCb, item.Constants);
            ctx.PSSetShaderResource(0, item.Diffuse);
            ctx.PSSetShaderResource(1, item.Normal);
            ctx.PSSetShaderResource(2, item.MetallicRoughness);
            ctx.PSSetShaderResource(12, item.Occlusion);
            ctx.PSSetShaderResource(13, item.Emission);
            ctx.IASetVertexBuffer(0, item.Mesh.VertexBuffer!, (uint)Unsafe.SizeOf<GpuVertex>(), 0);
            ctx.IASetIndexBuffer(item.Mesh.IndexBuffer, Format.R32_UInt, 0);
            ctx.DrawIndexed((uint)item.Group.IndexCount, (uint)item.Group.StartIndex, 0);
        }

        private void DrawItemGeometry(DrawItem item)
        {
            var ctx = _gfx.Context;
            _gfx.Upload(_objectCb, item.Constants);
            ctx.IASetVertexBuffer(0, item.Mesh.VertexBuffer!, (uint)Unsafe.SizeOf<GpuVertex>(), 0);
            ctx.IASetIndexBuffer(item.Mesh.IndexBuffer, Format.R32_UInt, 0);
            ctx.DrawIndexed((uint)item.Group.IndexCount, (uint)item.Group.StartIndex, 0);
        }

        private void DrawWireframe(bool overlay = false)
        {
            var ctx = _gfx.Context;
            // As an overlay the edges sit exactly on the surface: test depth with <=, don't write it.
            ctx.OMSetDepthStencilState(overlay ? _gfx.DepthLessEqualRead : _gfx.DepthDefault, 0);
            ctx.OMSetBlendState(_gfx.Opaque);
            ctx.IASetInputLayout(_gfx.MeshLayout);
            ctx.IASetPrimitiveTopology(PrimitiveTopology.LineList);
            ctx.VSSetShader(_gfx.MaskVS);
            ctx.PSSetShader(_gfx.MaskPS);
            foreach (var (mesh, world, selected) in _wire)
            {
                if (mesh.EdgeIndexBuffer is null) continue;
                var color = selected ? SelectionColor : overlay ? Srgb(0x20, 0x20, 0x24) : Srgb(0x9A, 0x9A, 0xA0);
                var c = new ObjectConstants { World = world, Tint = new Vector4(SrgbToLinear(new Vector3(color.X, color.Y, color.Z)), 1) };
                _gfx.Upload(_objectCb, c);
                ctx.IASetVertexBuffer(0, mesh.VertexBuffer!, (uint)Unsafe.SizeOf<GpuVertex>(), 0);
                ctx.IASetIndexBuffer(mesh.EdgeIndexBuffer, Format.R32_UInt, 0);
                ctx.DrawIndexed((uint)mesh.EdgeIndexCount, 0, 0);
            }
        }

        private void DrawOverlay(OverlayDrawList list, OverlayLayer layer)
        {
            var ctx = _gfx.Context;
            var i = (int)layer;
            ctx.OMSetDepthStencilState(layer == OverlayLayer.Scene ? _gfx.DepthLessEqualRead : _gfx.DepthDefault, 0);

            var tris = list.Triangles[i];
            if (tris.Count > 0)
            {
                ctx.OMSetBlendState(_gfx.AlphaBlend);
                _triBuffer.Upload<OverlayDrawList.TriVertex>(CollectionsMarshal.AsSpan(tris));
                ctx.IASetInputLayout(_gfx.TriLayout);
                ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
                ctx.IASetVertexBuffer(0, _triBuffer.Buffer!, (uint)Unsafe.SizeOf<OverlayDrawList.TriVertex>(), 0);
                ctx.VSSetShader(_gfx.TriVS);
                ctx.PSSetShader(_gfx.TriPS);
                ctx.Draw((uint)tris.Count, 0);
            }

            ctx.OMSetBlendState(_gfx.AlphaBlend);
            if (layer == OverlayLayer.Scene) ctx.OMSetDepthStencilState(_gfx.DepthLessEqualRead, 0);
            else ctx.OMSetDepthStencilState(_gfx.DepthNone, 0);

            var lines = list.Lines[i];
            if (lines.Count > 0)
            {
                _lineBuffer.Upload<OverlayDrawList.LineInstance>(CollectionsMarshal.AsSpan(lines));
                ctx.IASetInputLayout(_gfx.LineLayout);
                ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
                ctx.IASetVertexBuffer(0, _lineBuffer.Buffer!, (uint)Unsafe.SizeOf<OverlayDrawList.LineInstance>(), 0);
                ctx.VSSetShader(_gfx.LineVS);
                ctx.PSSetShader(_gfx.LinePS);
                ctx.DrawInstanced(6, (uint)lines.Count, 0, 0);
            }

            var points = list.Points[i];
            if (points.Count > 0)
            {
                _pointBuffer.Upload<OverlayDrawList.PointInstance>(CollectionsMarshal.AsSpan(points));
                ctx.IASetInputLayout(_gfx.PointLayout);
                ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
                ctx.IASetVertexBuffer(0, _pointBuffer.Buffer!, (uint)Unsafe.SizeOf<OverlayDrawList.PointInstance>(), 0);
                ctx.VSSetShader(_gfx.PointVS);
                ctx.PSSetShader(_gfx.PointPS);
                ctx.DrawInstanced(6, (uint)points.Count, 0, 0);
            }
        }

        private void DrawMaskPass(int width, int height)
        {
            var ctx = _gfx.Context;
            ctx.OMSetRenderTargets(_mask!.Rtv!, null);
            ctx.ClearRenderTargetView(_mask.Rtv!, new Vortice.Mathematics.Color4(0, 0, 0, 0));
            ctx.RSSetViewport(0, 0, width, height, 0, 1);
            ctx.RSSetState(_gfx.CullNone);
            ctx.OMSetDepthStencilState(_gfx.DepthNone, 0);
            ctx.OMSetBlendState(_gfx.Opaque);
            ctx.IASetInputLayout(_gfx.MeshLayout);
            ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            ctx.VSSetShader(_gfx.MaskVS);
            ctx.PSSetShader(_gfx.MaskPS);
            foreach (var item in _selected)
            {
                var c = new ObjectConstants { World = item.World, Tint = new Vector4(1, item.IsActive ? 1 : 0, 0, 1) };
                _gfx.Upload(_objectCb, c);
                ctx.IASetVertexBuffer(0, item.Mesh.VertexBuffer!, (uint)Unsafe.SizeOf<GpuVertex>(), 0);
                ctx.IASetIndexBuffer(item.Mesh.IndexBuffer, Format.R32_UInt, 0);
                ctx.DrawIndexed((uint)item.Group.IndexCount, (uint)item.Group.StartIndex, 0);
            }
            ctx.OMSetRenderTargets((ID3D11RenderTargetView)null!, null);
        }

        private void DrawBloom(int width, int height)
        {
            var ctx = _gfx.Context;
            ctx.OMSetDepthStencilState(_gfx.DepthNone, 0);
            ctx.OMSetBlendState(_gfx.Opaque);
            var post = new PostConstants { BloomThreshold = 0.85f, BloomIntensity = 0.6f };

            ctx.OMSetRenderTargets(_bloomA!.Rtv!, null);
            ctx.RSSetViewport(0, 0, _bloomA.Width, _bloomA.Height, 0, 1);
            _gfx.Upload(_postCb, post);
            ctx.PSSetShaderResource(8, _sceneResolved!.Srv);
            FullscreenPass(_gfx.BrightPassPS);
            ctx.PSSetShaderResource(8, null);

            for (var pass = 0; pass < 2; pass++)
            {
                post.TexelDir = new Vector2(1f / _bloomA.Width * (pass + 1), 0);
                _gfx.Upload(_postCb, post);
                ctx.OMSetRenderTargets(_bloomB!.Rtv!, null);
                ctx.PSSetShaderResource(8, _bloomA.Srv);
                FullscreenPass(_gfx.BlurPS);
                ctx.PSSetShaderResource(8, null);

                post.TexelDir = new Vector2(0, 1f / _bloomA.Height * (pass + 1));
                _gfx.Upload(_postCb, post);
                ctx.OMSetRenderTargets(_bloomA.Rtv!, null);
                ctx.PSSetShaderResource(8, _bloomB.Srv);
                FullscreenPass(_gfx.BlurPS);
                ctx.PSSetShaderResource(8, null);
            }
            ctx.OMSetRenderTargets((ID3D11RenderTargetView)null!, null);
        }

        private void DrawComposite(int width, int height, bool outline)
        {
            var ctx = _gfx.Context;
            var post = new PostConstants
            {
                BloomIntensity = 0.6f,
                OutlineColor = SelectionColor,
                ActiveOutlineColor = ActiveSelectionColor,
                BloomEnabled = Settings.Bloom ? 1 : 0,
                OutlineEnabled = outline ? 1 : 0,
            };
            _gfx.Upload(_postCb, post);
            ctx.OMSetRenderTargets(_output!.Rtv!, null);
            ctx.RSSetViewport(0, 0, width, height, 0, 1);
            ctx.OMSetDepthStencilState(_gfx.DepthNone, 0);
            ctx.OMSetBlendState(_gfx.Opaque);
            ctx.PSSetShaderResource(9, _sceneResolved!.Srv);
            ctx.PSSetShaderResource(10, Settings.Bloom ? _bloomA!.Srv : null);
            ctx.PSSetShaderResource(11, outline ? _mask!.Srv : null);
            FullscreenPass(_gfx.CompositePS);
            ctx.PSSetShaderResource(9, null);
            ctx.PSSetShaderResource(10, null);
            ctx.PSSetShaderResource(11, null);
            ctx.OMSetRenderTargets((ID3D11RenderTargetView)null!, null);
        }

        private void FullscreenPass(ID3D11PixelShader ps)
        {
            var ctx = _gfx.Context;
            ctx.IASetInputLayout(null);
            ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            ctx.VSSetShader(_gfx.FullscreenVS);
            ctx.PSSetShader(ps);
            ctx.Draw(3, 0);
        }

        // ---------------- targets ----------------

        private void EnsureTargets(int width, int height, int samples)
        {
            if (_output is not null && width == _width && height == _height && samples == _samples) return;
            DisposeTargets();
            _width = width; _height = height; _samples = samples;

            _sceneMsaa = RenderTexture.CreateColor(_gfx, width, height, Format.R16G16B16A16_Float, samples, shaderResource: samples == 1);
            _depthMsaa = RenderTexture.CreateDepth(_gfx, width, height, samples);
            _sceneResolved = RenderTexture.CreateColor(_gfx, width, height, Format.R16G16B16A16_Float);
            _mask = RenderTexture.CreateColor(_gfx, width, height, Format.R8G8_UNorm);
            _normalDepth = RenderTexture.CreateColor(_gfx, width, height, Format.R16G16B16A16_Float);
            _prepassDepth = RenderTexture.CreateDepth(_gfx, width, height);
            _aoRaw = RenderTexture.CreateColor(_gfx, Math.Max(1, width / 2), Math.Max(1, height / 2), Format.R8_UNorm);
            _ao = RenderTexture.CreateColor(_gfx, width, height, Format.R8_UNorm);
            _bloomA = RenderTexture.CreateColor(_gfx, Math.Max(1, width / 2), Math.Max(1, height / 2), Format.R16G16B16A16_Float);
            _bloomB = RenderTexture.CreateColor(_gfx, Math.Max(1, width / 2), Math.Max(1, height / 2), Format.R16G16B16A16_Float);
            _output = RenderTexture.CreateColor(_gfx, width, height, Format.B8G8R8A8_UNorm, 1, shaderResource: false,
                misc: _sharedOutput ? ResourceOptionFlags.Shared : ResourceOptionFlags.None);
            OutputRecreated?.Invoke();
        }

        private void DisposeTargets()
        {
            foreach (var t in new[] { _sceneMsaa, _depthMsaa, _sceneResolved, _mask, _normalDepth, _prepassDepth, _aoRaw, _ao, _bloomA, _bloomB, _output })
                t?.Dispose();
            _sceneMsaa = _depthMsaa = _sceneResolved = _mask = _normalDepth = _prepassDepth = _aoRaw = _ao = _bloomA = _bloomB = _output = null;
        }

        /// <summary>Reads the last rendered frame back as tightly packed BGRA8 rows.</summary>
        public byte[] ReadOutputPixels()
        {
            if (_output is null) return Array.Empty<byte>();
            var desc = new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)_width, (uint)_height, 1, 1, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read, 1, 0, ResourceOptionFlags.None);
            using var staging = _gfx.Device.CreateTexture2D(desc);
            _gfx.Context.CopyResource(staging, _output.Texture);
            var mapped = _gfx.Context.Map(staging, 0, MapMode.Read, MapFlags.None);
            var result = new byte[_width * _height * 4];
            try
            {
                for (var y = 0; y < _height; y++)
                    Marshal.Copy(mapped.DataPointer + (nint)(y * mapped.RowPitch), result, y * _width * 4, _width * 4);
            }
            finally
            {
                _gfx.Context.Unmap(staging, 0);
            }
            return result;
        }

        public int OutputWidth => _width;
        public int OutputHeight => _height;

        private static Vector4 Srgb(byte r, byte g, byte b) => new(r / 255f, g / 255f, b / 255f, 1f);

        private static Vector3 SrgbToLinear(Vector3 c) => new(MathF.Pow(MathF.Max(0, c.X), 2.2f), MathF.Pow(MathF.Max(0, c.Y), 2.2f), MathF.Pow(MathF.Max(0, c.Z), 2.2f));

        public void Dispose()
        {
            DisposeTargets();
            _shadowMap?.Dispose();
            _meshes.Dispose();
            _textures.Dispose();
            _frameCb.Dispose();
            _objectCb.Dispose();
            _postCb.Dispose();
            _lineBuffer.Dispose();
            _pointBuffer.Dispose();
            _triBuffer.Dispose();
        }
    }
}
