// Vortice annotates many D3D parameters as non-nullable, but D3D11 explicitly accepts
// null to unbind a resource/shader/state slot - so null is passed on purpose here.
#pragma warning disable CS8604, CS8625

using System.Runtime.InteropServices;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using MapFlags = Vortice.Direct3D11.MapFlags;

namespace JolieCat3D.Engine.Rendering.Dx
{
    /// <summary>
    /// The D3D11 device plus everything shared by every viewport: compiled shaders,
    /// input layouts, sampler/blend/depth/rasterizer states. One instance per process
    /// (see <see cref="Shared"/>) so several viewports and offscreen captures reuse the
    /// same GPU resources.
    /// </summary>
    internal sealed class DxGraphics : IDisposable
    {
        private static DxGraphics? _shared;

        public static DxGraphics Shared => _shared ??= new DxGraphics();

        public ID3D11Device Device { get; }
        public ID3D11DeviceContext Context { get; }
        public bool IsWarp { get; }

        // Shaders
        public ID3D11VertexShader MeshVS { get; private set; } = null!;
        public ID3D11PixelShader MeshPS { get; private set; } = null!;
        public ID3D11VertexShader PrepassVS { get; private set; } = null!;
        public ID3D11PixelShader PrepassPS { get; private set; } = null!;
        public ID3D11VertexShader ShadowVS { get; private set; } = null!;
        public ID3D11VertexShader ShadowClipVS { get; private set; } = null!;
        public ID3D11PixelShader ShadowClipPS { get; private set; } = null!;
        public ID3D11VertexShader MaskVS { get; private set; } = null!;
        public ID3D11PixelShader MaskPS { get; private set; } = null!;
        public ID3D11InputLayout MeshLayout { get; private set; } = null!;

        public ID3D11VertexShader FullscreenVS { get; private set; } = null!;
        public ID3D11PixelShader BackgroundPS { get; private set; } = null!;
        public ID3D11PixelShader SsaoPS { get; private set; } = null!;
        public ID3D11PixelShader BlurAoPS { get; private set; } = null!;
        public ID3D11PixelShader BrightPassPS { get; private set; } = null!;
        public ID3D11PixelShader BlurPS { get; private set; } = null!;
        public ID3D11PixelShader CompositePS { get; private set; } = null!;

        public ID3D11VertexShader GridVS { get; private set; } = null!;
        public ID3D11PixelShader GridPS { get; private set; } = null!;

        public ID3D11VertexShader LineVS { get; private set; } = null!;
        public ID3D11PixelShader LinePS { get; private set; } = null!;
        public ID3D11InputLayout LineLayout { get; private set; } = null!;
        public ID3D11VertexShader PointVS { get; private set; } = null!;
        public ID3D11PixelShader PointPS { get; private set; } = null!;
        public ID3D11InputLayout PointLayout { get; private set; } = null!;
        public ID3D11VertexShader TriVS { get; private set; } = null!;
        public ID3D11PixelShader TriPS { get; private set; } = null!;
        public ID3D11InputLayout TriLayout { get; private set; } = null!;

        // States
        public ID3D11SamplerState LinearWrap { get; }
        public ID3D11SamplerState LinearClamp { get; }
        public ID3D11SamplerState ShadowCompare { get; }
        public ID3D11SamplerState PointClamp { get; }
        public ID3D11BlendState Opaque { get; }
        public ID3D11BlendState AlphaBlend { get; }
        public ID3D11BlendState Premultiplied { get; }
        public ID3D11BlendState Additive { get; }
        public ID3D11DepthStencilState DepthDefault { get; }
        public ID3D11DepthStencilState DepthRead { get; }
        public ID3D11DepthStencilState DepthNone { get; }
        public ID3D11DepthStencilState DepthLessEqualRead { get; }
        public ID3D11RasterizerState CullNone { get; }
        public ID3D11RasterizerState CullNoneMsaa { get; }
        public ID3D11RasterizerState ShadowRaster { get; }
        public ID3D11RasterizerState WireframeRaster { get; }

        private DxGraphics()
        {
            var levels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1 };
            var flags = DeviceCreationFlags.BgraSupport;

            ID3D11Device device;
            ID3D11DeviceContext context;
            var result = D3D11.D3D11CreateDevice(IntPtr.Zero, DriverType.Hardware, flags, levels, out device!, out context!);
            if (result.Failure)
            {
                // No usable GPU (VM, RDP, broken driver): the WARP software rasterizer still works.
                D3D11.D3D11CreateDevice(IntPtr.Zero, DriverType.Warp, flags, levels, out device!, out context!).CheckError();
                IsWarp = true;
            }

            Device = device;
            Context = context;

            CompileShaders();

            LinearWrap = Device.CreateSamplerState(new SamplerDescription(Filter.Anisotropic, TextureAddressMode.Wrap, 0, 8, ComparisonFunction.Never, 0, float.MaxValue));
            LinearClamp = Device.CreateSamplerState(SamplerDescription.LinearClamp);
            PointClamp = Device.CreateSamplerState(SamplerDescription.PointClamp);
            var shadowDesc = new SamplerDescription(Filter.ComparisonMinMagMipLinear, TextureAddressMode.Border, 0, 1, ComparisonFunction.LessEqual, 0, float.MaxValue)
            {
                BorderColor = new Vortice.Mathematics.Color4(1, 1, 1, 1),
            };
            ShadowCompare = Device.CreateSamplerState(shadowDesc);

            Opaque = Device.CreateBlendState(BlendDescription.Opaque);
            AlphaBlend = Device.CreateBlendState(BlendDescription.NonPremultiplied);
            Premultiplied = Device.CreateBlendState(BlendDescription.AlphaBlend);
            Additive = Device.CreateBlendState(BlendDescription.Additive);

            DepthDefault = Device.CreateDepthStencilState(DepthStencilDescription.Default);
            DepthRead = Device.CreateDepthStencilState(DepthStencilDescription.DepthRead);
            DepthNone = Device.CreateDepthStencilState(DepthStencilDescription.None);
            DepthLessEqualRead = Device.CreateDepthStencilState(new DepthStencilDescription(true, DepthWriteMask.Zero, ComparisonFunction.LessEqual));

            CullNone = Device.CreateRasterizerState(RasterizerDescription.CullNone);
            var msaa = RasterizerDescription.CullNone;
            msaa.MultisampleEnable = true;
            msaa.AntialiasedLineEnable = false;
            CullNoneMsaa = Device.CreateRasterizerState(msaa);
            var shadow = RasterizerDescription.CullNone;
            shadow.DepthBias = 800;
            shadow.SlopeScaledDepthBias = 1.5f;
            shadow.DepthBiasClamp = 0.01f;
            ShadowRaster = Device.CreateRasterizerState(shadow);
            WireframeRaster = Device.CreateRasterizerState(RasterizerDescription.Wireframe);
        }

        private void CompileShaders()
        {
            byte[] Vs(string src, string entry) => Compile(src, entry, "vs_5_0");
            byte[] Ps(string src, string entry) => Compile(src, entry, "ps_5_0");

            var meshVs = Vs(DxShaders.Mesh, "VSMain");
            MeshVS = Device.CreateVertexShader(meshVs);
            MeshPS = Device.CreatePixelShader(Ps(DxShaders.Mesh, "PSMain"));
            PrepassVS = Device.CreateVertexShader(Vs(DxShaders.Mesh, "VSPrepass"));
            PrepassPS = Device.CreatePixelShader(Ps(DxShaders.Mesh, "PSPrepass"));
            ShadowVS = Device.CreateVertexShader(Vs(DxShaders.Mesh, "VSShadow"));
            ShadowClipVS = Device.CreateVertexShader(Vs(DxShaders.Mesh, "VSShadowClip"));
            ShadowClipPS = Device.CreatePixelShader(Ps(DxShaders.Mesh, "PSShadowClip"));
            MaskVS = Device.CreateVertexShader(Vs(DxShaders.Mesh, "VSMask"));
            MaskPS = Device.CreatePixelShader(Ps(DxShaders.Mesh, "PSMask"));
            MeshLayout = Device.CreateInputLayout(new[]
            {
                new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new InputElementDescription("NORMAL", 0, Format.R32G32B32_Float, 12, 0),
                new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 24, 0),
                new InputElementDescription("COLOR", 0, Format.R32G32B32A32_Float, 32, 0),
            }, meshVs);

            FullscreenVS = Device.CreateVertexShader(Vs(DxShaders.Fullscreen, "VSFullscreen"));
            BackgroundPS = Device.CreatePixelShader(Ps(DxShaders.Fullscreen, "PSBackground"));
            SsaoPS = Device.CreatePixelShader(Ps(DxShaders.Fullscreen, "PSSsao"));
            BlurAoPS = Device.CreatePixelShader(Ps(DxShaders.Fullscreen, "PSBlurAo"));
            BrightPassPS = Device.CreatePixelShader(Ps(DxShaders.Fullscreen, "PSBrightPass"));
            BlurPS = Device.CreatePixelShader(Ps(DxShaders.Fullscreen, "PSBlur"));
            CompositePS = Device.CreatePixelShader(Ps(DxShaders.Fullscreen, "PSComposite"));

            GridVS = Device.CreateVertexShader(Vs(DxShaders.Grid, "VSGrid"));
            GridPS = Device.CreatePixelShader(Ps(DxShaders.Grid, "PSGrid"));

            var lineVs = Vs(DxShaders.Overlay, "VSLine");
            LineVS = Device.CreateVertexShader(lineVs);
            LinePS = Device.CreatePixelShader(Ps(DxShaders.Overlay, "PSLine"));
            LineLayout = Device.CreateInputLayout(new[]
            {
                new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0, InputClassification.PerInstanceData, 1),
                new InputElementDescription("POSITION", 1, Format.R32G32B32_Float, 12, 0, InputClassification.PerInstanceData, 1),
                new InputElementDescription("COLOR", 0, Format.R32G32B32A32_Float, 24, 0, InputClassification.PerInstanceData, 1),
                new InputElementDescription("TEXCOORD", 0, Format.R32_Float, 40, 0, InputClassification.PerInstanceData, 1),
                new InputElementDescription("TEXCOORD", 1, Format.R32_Float, 44, 0, InputClassification.PerInstanceData, 1),
            }, lineVs);

            var pointVs = Vs(DxShaders.Overlay, "VSPoint");
            PointVS = Device.CreateVertexShader(pointVs);
            PointPS = Device.CreatePixelShader(Ps(DxShaders.Overlay, "PSPoint"));
            PointLayout = Device.CreateInputLayout(new[]
            {
                new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0, InputClassification.PerInstanceData, 1),
                new InputElementDescription("COLOR", 0, Format.R32G32B32A32_Float, 12, 0, InputClassification.PerInstanceData, 1),
                new InputElementDescription("TEXCOORD", 0, Format.R32_Float, 28, 0, InputClassification.PerInstanceData, 1),
                new InputElementDescription("TEXCOORD", 1, Format.R32_Float, 32, 0, InputClassification.PerInstanceData, 1),
            }, pointVs);

            var triVs = Vs(DxShaders.Overlay, "VSTri");
            TriVS = Device.CreateVertexShader(triVs);
            TriPS = Device.CreatePixelShader(Ps(DxShaders.Overlay, "PSTri"));
            TriLayout = Device.CreateInputLayout(new[]
            {
                new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new InputElementDescription("NORMAL", 0, Format.R32G32B32_Float, 12, 0),
                new InputElementDescription("COLOR", 0, Format.R32G32B32A32_Float, 24, 0),
            }, triVs);
        }

        private static byte[] Compile(string source, string entry, string profile)
        {
            var result = Compiler.Compile(source, entry, entry, profile, out var blob, out var errors);
            if (result.Failure || blob is null)
            {
                var message = errors?.AsString() ?? result.ToString();
                errors?.Dispose();
                throw new InvalidOperationException($"Shader '{entry}' failed to compile:\n{message}");
            }

            var bytes = blob.AsBytes();
            blob.Dispose();
            errors?.Dispose();
            return bytes;
        }

        public ID3D11Buffer CreateConstantBuffer<T>() where T : unmanaged =>
            Device.CreateBuffer(new BufferDescription((uint)AlignTo16(Marshal.SizeOf<T>()), BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));

        public unsafe void Upload<T>(ID3D11Buffer buffer, in T value) where T : unmanaged
        {
            var mapped = Context.Map(buffer, MapMode.WriteDiscard, MapFlags.None);
            *(T*)mapped.DataPointer = value;
            Context.Unmap(buffer);
        }

        private static int AlignTo16(int size) => (size + 15) & ~15;

        public void Dispose()
        {
            Context.ClearState();
            Context.Flush();
            Context.Dispose();
            Device.Dispose();
        }
    }

    /// <summary>A growable dynamic vertex buffer for per-frame overlay data.</summary>
    internal sealed class DynamicBuffer : IDisposable
    {
        private readonly DxGraphics _gfx;
        private readonly BindFlags _bind;
        private ID3D11Buffer? _buffer;
        private int _capacityBytes;

        public DynamicBuffer(DxGraphics gfx, BindFlags bind)
        {
            _gfx = gfx;
            _bind = bind;
        }

        public ID3D11Buffer? Buffer => _buffer;

        public unsafe void Upload<T>(ReadOnlySpan<T> data) where T : unmanaged
        {
            var bytes = data.Length * sizeof(T);
            if (bytes == 0) return;
            if (_buffer is null || bytes > _capacityBytes)
            {
                _buffer?.Dispose();
                _capacityBytes = Math.Max(bytes, Math.Max(_capacityBytes * 2, 4096));
                _buffer = _gfx.Device.CreateBuffer(new BufferDescription((uint)_capacityBytes, _bind, ResourceUsage.Dynamic, CpuAccessFlags.Write));
            }

            var mapped = _gfx.Context.Map(_buffer, MapMode.WriteDiscard, MapFlags.None);
            fixed (T* src = data)
                System.Buffer.MemoryCopy(src, (void*)mapped.DataPointer, _capacityBytes, bytes);
            _gfx.Context.Unmap(_buffer);
        }

        public void Dispose() => _buffer?.Dispose();
    }

    /// <summary>A 2D texture usable as render target and/or depth buffer plus shader input.</summary>
    internal sealed class RenderTexture : IDisposable
    {
        public ID3D11Texture2D Texture { get; }
        public ID3D11RenderTargetView? Rtv { get; }
        public ID3D11DepthStencilView? Dsv { get; }
        public ID3D11ShaderResourceView? Srv { get; }
        public int Width { get; }
        public int Height { get; }
        public int Samples { get; }

        private RenderTexture(ID3D11Texture2D texture, ID3D11RenderTargetView? rtv, ID3D11DepthStencilView? dsv, ID3D11ShaderResourceView? srv, int w, int h, int samples)
        {
            Texture = texture; Rtv = rtv; Dsv = dsv; Srv = srv; Width = w; Height = h; Samples = samples;
        }

        public static RenderTexture CreateColor(DxGraphics gfx, int w, int h, Format format, int samples = 1, bool shaderResource = true, ResourceOptionFlags misc = ResourceOptionFlags.None)
        {
            var bind = BindFlags.RenderTarget | (shaderResource ? BindFlags.ShaderResource : 0);
            var desc = new Texture2DDescription(format, (uint)w, (uint)h, 1, 1, bind, ResourceUsage.Default, CpuAccessFlags.None, (uint)samples, 0, misc);
            var tex = gfx.Device.CreateTexture2D(desc);
            var rtv = gfx.Device.CreateRenderTargetView(tex, null);
            var srv = shaderResource ? gfx.Device.CreateShaderResourceView(tex, null) : null;
            return new RenderTexture(tex, rtv, null, srv, w, h, samples);
        }

        /// <summary>Depth buffer; with <paramref name="shaderResource"/> it is created typeless so it can also be sampled as R32_Float.</summary>
        public static RenderTexture CreateDepth(DxGraphics gfx, int w, int h, int samples = 1, bool shaderResource = false)
        {
            var format = shaderResource ? Format.R32_Typeless : Format.D32_Float;
            var bind = BindFlags.DepthStencil | (shaderResource ? BindFlags.ShaderResource : 0);
            var desc = new Texture2DDescription(format, (uint)w, (uint)h, 1, 1, bind, ResourceUsage.Default, CpuAccessFlags.None, (uint)samples, 0, ResourceOptionFlags.None);
            var tex = gfx.Device.CreateTexture2D(desc);
            var dsvDim = samples > 1 ? DepthStencilViewDimension.Texture2DMultisampled : DepthStencilViewDimension.Texture2D;
            var dsv = gfx.Device.CreateDepthStencilView(tex, new DepthStencilViewDescription(dsvDim, Format.D32_Float, 0, 0, 1, DepthStencilViewFlags.None));
            ID3D11ShaderResourceView? srv = null;
            if (shaderResource)
            {
                var srvDim = samples > 1 ? ShaderResourceViewDimension.Texture2DMultisampled : ShaderResourceViewDimension.Texture2D;
                srv = gfx.Device.CreateShaderResourceView(tex, new ShaderResourceViewDescription(srvDim, Format.R32_Float, 0, 1, 0, 1, BufferExtendedShaderResourceViewFlags.None));
            }
            return new RenderTexture(tex, null, dsv, srv, w, h, samples);
        }

        public void Dispose()
        {
            Srv?.Dispose();
            Rtv?.Dispose();
            Dsv?.Dispose();
            Texture.Dispose();
        }
    }
}
