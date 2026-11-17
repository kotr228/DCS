// Vortice annotates many D3D parameters as non-nullable, but D3D11 explicitly accepts
// null to unbind a resource/shader/state slot - so null is passed on purpose here.
#pragma warning disable CS8604, CS8625

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.Direct3D11;
using Vortice.Direct3D9;
using IDXGIResource = Vortice.DXGI.IDXGIResource;
using PresentParameters = Vortice.Direct3D9.PresentParameters;
using SwapEffect = Vortice.Direct3D9.SwapEffect;
using QueryType = Vortice.Direct3D11.QueryType;
using Usage = Vortice.Direct3D9.Usage;

namespace JolieCat3D.Engine.Rendering.Dx
{
    /// <summary>
    /// Shows the renderer's BGRA output inside WPF. Normally the D3D11 texture is shared
    /// with a D3D9Ex surface and handed to a <see cref="D3DImage"/> (zero copy, composited
    /// by WPF on the GPU). If that is impossible - WARP device, WPF software rendering,
    /// remote sessions without D3D9 - it falls back to copying the pixels into a
    /// <see cref="WriteableBitmap"/>.
    /// </summary>
    internal sealed class D3DImagePresenter : IDisposable
    {
        private readonly DxGraphics _gfx;
        private readonly D3DImage _d3dImage = new();
        private IDirect3D9Ex? _d3d9;
        private IDirect3DDevice9Ex? _device9;
        private IDirect3DTexture9? _texture9;
        private IDirect3DSurface9? _surface9;
        private ID3D11Query? _query;
        private WriteableBitmap? _bitmap;
        private int _width, _height;
        private bool _useFallback;

        public D3DImagePresenter(DxGraphics gfx)
        {
            _gfx = gfx;
            _useFallback = gfx.IsWarp || RenderCapability.Tier >> 16 == 0;
            if (!_useFallback)
            {
                try
                {
                    _d3d9 = D3D9.Direct3DCreate9Ex();
                    var pp = new PresentParameters
                    {
                        Windowed = true,
                        SwapEffect = SwapEffect.Discard,
                        DeviceWindowHandle = GetDesktopWindow(),
                        PresentationInterval = PresentInterval.Default,
                        BackBufferWidth = 1,
                        BackBufferHeight = 1,
                        BackBufferFormat = Vortice.Direct3D9.Format.Unknown,
                    };
                    _device9 = _d3d9.CreateDeviceEx(0, DeviceType.Hardware, GetDesktopWindow(),
                        CreateFlags.HardwareVertexProcessing | CreateFlags.Multithreaded | CreateFlags.FpuPreserve, pp);
                    _query = gfx.Device.CreateQuery(QueryType.Event, QueryFlags.None);
                    _d3dImage.IsFrontBufferAvailableChanged += OnFrontBufferAvailableChanged;
                }
                catch (Exception)
                {
                    _useFallback = true;
                }
            }
        }

        /// <summary>The image to show (a D3DImage or, in fallback mode, a WriteableBitmap).</summary>
        public ImageSource? Source => _useFallback ? _bitmap : _d3dImage;

        public bool IsFallback => _useFallback;

        public event Action? SourceChanged;

        /// <summary>Binds the renderer's (shared) output texture after it was (re)created.</summary>
        public void Attach(RenderTexture output)
        {
            _width = output.Width;
            _height = output.Height;
            if (_useFallback)
            {
                _bitmap = new WriteableBitmap(_width, _height, 96, 96, PixelFormats.Pbgra32, null);
                SourceChanged?.Invoke();
                return;
            }

            _surface9?.Dispose();
            _texture9?.Dispose();
            _surface9 = null;
            _texture9 = null;
            try
            {
                using var resource = output.Texture.QueryInterface<IDXGIResource>();
                var handle = resource.SharedHandle;
                _texture9 = _device9!.CreateTexture((uint)_width, (uint)_height, 1, Usage.RenderTarget, Vortice.Direct3D9.Format.A8R8G8B8, Pool.Default, ref handle);
                _surface9 = _texture9.GetSurfaceLevel(0);
                SetBackBuffer();
            }
            catch (Exception)
            {
                _useFallback = true;
                Attach(output);
                return;
            }
            SourceChanged?.Invoke();
        }

        private void SetBackBuffer()
        {
            if (_surface9 is null || !_d3dImage.IsFrontBufferAvailable) return;
            _d3dImage.Lock();
            _d3dImage.SetBackBuffer(D3DResourceType.IDirect3DSurface9, _surface9.NativePointer);
            _d3dImage.Unlock();
        }

        private void OnFrontBufferAvailableChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_d3dImage.IsFrontBufferAvailable) SetBackBuffer();
        }

        /// <summary>Shows the frame the renderer just drew.</summary>
        public void Present(DxSceneRenderer renderer)
        {
            if (_useFallback)
            {
                if (_bitmap is null) return;
                var pixels = renderer.ReadOutputPixels();
                if (pixels.Length != _width * _height * 4) return;
                _bitmap.WritePixels(new Int32Rect(0, 0, _width, _height), pixels, _width * 4, 0);
                return;
            }

            if (_surface9 is null || !_d3dImage.IsFrontBufferAvailable) return;

            // Wait for the GPU to finish the frame so WPF never composites a half-drawn surface.
            if (_query is not null)
            {
                _gfx.Context.End(_query);
                var spins = 0;
                while (!_gfx.Context.IsDataAvailable(_query) && spins++ < 200000) Thread.SpinWait(20);
            }

            _d3dImage.Lock();
            _d3dImage.AddDirtyRect(new Int32Rect(0, 0, _width, _height));
            _d3dImage.Unlock();
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        public void Dispose()
        {
            _d3dImage.IsFrontBufferAvailableChanged -= OnFrontBufferAvailableChanged;
            _surface9?.Dispose();
            _texture9?.Dispose();
            _device9?.Dispose();
            _d3d9?.Dispose();
            _query?.Dispose();
        }
    }
}
