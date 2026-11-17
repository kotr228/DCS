// Vortice annotates many D3D parameters as non-nullable, but D3D11 explicitly accepts
// null to unbind a resource/shader/state slot - so null is passed on purpose here.
#pragma warning disable CS8604, CS8625

using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.Direct3D11;
using Vortice.DXGI;
using CoreTextureBuffer = JolieCat3D.Core.Materials.TextureBuffer;

namespace JolieCat3D.Engine.Rendering.Dx
{
    /// <summary>
    /// GPU textures for image files (re-loaded when the file changes on disk), for
    /// in-editor painted <see cref="CoreTextureBuffer"/>s (re-uploaded when their pixels
    /// change) and for six-face skybox cube maps.
    /// </summary>
    /// <summary>The world cube map, its mip count and diffuse irradiance SH (see Core SphericalHarmonics.ToIrradiance).</summary>
    internal sealed record SkyboxResource(ID3D11ShaderResourceView Srv, int MipCount, Vector3[] IrradianceSh);

    internal sealed class TextureCache : IDisposable
    {
        private readonly DxGraphics _gfx;
        private readonly Dictionary<(string Path, bool Srgb), (DateTime WriteTime, ID3D11ShaderResourceView? Srv)> _files = new();
        private readonly ConditionalWeakTable<CoreTextureBuffer, PaintedEntry> _painted = new();
        private readonly List<ID3D11ShaderResourceView> _paintedViews = new();
        private (string Key, DateTime WriteTime, SkyboxResource? Sky)? _cube;

        private sealed class PaintedEntry
        {
            public ulong Hash;
            public ID3D11ShaderResourceView? Srv;
            public int Width, Height;
        }

        public TextureCache(DxGraphics gfx) => _gfx = gfx;

        private static readonly string[] FaceSuffixes = { "_r", "_l", "_u", "_d", "_b", "_f" }; // D3D order: +X -X +Y -Y +Z -Z
        private static readonly string[] SupportedExtensions = { ".jpg", ".jpeg", ".png", ".bmp" };

        public ID3D11ShaderResourceView? GetFile(string? path, bool srgb)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            DateTime writeTime;
            try
            {
                if (!File.Exists(path)) return null;
                writeTime = File.GetLastWriteTimeUtc(path);
            }
            catch (Exception) { return null; }

            var key = (path, srgb);
            if (_files.TryGetValue(key, out var cached) && cached.WriteTime == writeTime) return cached.Srv;
            cached.Srv?.Dispose();

            ID3D11ShaderResourceView? srv = null;
            try
            {
                var (pixels, w, h) = DecodeBgra(path);
                srv = CreateMippedTexture(pixels, w, h, srgb ? Format.B8G8R8A8_UNorm_SRgb : Format.B8G8R8A8_UNorm);
            }
            catch (Exception)
            {
                // Unreadable image - render untextured rather than failing the frame.
            }

            _files[key] = (writeTime, srv);
            return srv;
        }

        public ID3D11ShaderResourceView? GetPainted(CoreTextureBuffer? buffer)
        {
            if (buffer is null || buffer.Width <= 0 || buffer.Height <= 0) return null;
            var entry = _painted.GetValue(buffer, _ => new PaintedEntry());
            var pixels = buffer.Pixels;
            var bytes = new byte[buffer.Width * buffer.Height * 4];
            ulong hash = 14695981039346656037UL;
            for (var i = 0; i < pixels.Count; i++)
            {
                var c = pixels[i];
                var o = i * 4;
                bytes[o] = ToByte(c.R); bytes[o + 1] = ToByte(c.G); bytes[o + 2] = ToByte(c.B); bytes[o + 3] = ToByte(c.A);
                hash = (hash ^ BitConverter.ToUInt32(bytes, o)) * 1099511628211UL;
            }

            if (entry.Srv is not null && entry.Hash == hash && entry.Width == buffer.Width && entry.Height == buffer.Height)
                return entry.Srv;

            if (entry.Srv is not null) _paintedViews.Remove(entry.Srv);
            entry.Srv?.Dispose();
            entry.Srv = CreateMippedTexture(bytes, buffer.Width, buffer.Height, Format.R8G8B8A8_UNorm_SRgb);
            entry.Hash = hash;
            entry.Width = buffer.Width;
            entry.Height = buffer.Height;
            if (entry.Srv is not null) _paintedViews.Add(entry.Srv);
            return entry.Srv;

            static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);
        }

        /// <summary>
        /// The world texture as a linear float16 cube map with a box-filtered mip chain
        /// (rough reflections read blurrier mips) plus its diffuse irradiance as 9 SH
        /// coefficients - the viewport's image-based lighting. Accepts a six-image skybox
        /// (a folder with cube_f/_b/_l/_r/_u/_d, or a path prefix) or an equirectangular .hdr.
        /// </summary>
        public SkyboxResource? GetSkybox(string? source, float rotationDegrees = 0f)
        {
            if (string.IsNullOrWhiteSpace(source)) return null;
            var panorama = Core.Scene.EnvironmentSettings.IsPanorama(source);
            string[] faces;
            if (panorama)
            {
                if (!File.Exists(source)) return null;
                faces = new[] { source };
            }
            else if (!TryResolveFacePaths(source, out faces)) return null;

            DateTime newest;
            try { newest = faces.Max(File.GetLastWriteTimeUtc); }
            catch (Exception) { return null; }

            var key = panorama ? $"{source}|{rotationDegrees.ToString(System.Globalization.CultureInfo.InvariantCulture)}" : source;
            if (_cube is { } c && c.Key == key && c.WriteTime == newest) return c.Sky;
            _cube?.Sky?.Srv.Dispose();

            SkyboxResource? sky = null;
            try
            {
                var (linearFaces, size) = panorama ? PanoramaFaces(source, rotationDegrees) : ImageFaces(faces);
                if (linearFaces is not null) sky = CreateSkyCube(linearFaces, size);
            }
            catch (Exception) { sky = null; }

            _cube = (key, newest, sky);
            return sky;
        }

        private static (Vector3[][]? Faces, int Size) PanoramaFaces(string path, float rotationDegrees)
        {
            var image = Core.Materials.HdrImage.LoadHdr(path);
            // Power of two so every mip halves cleanly.
            var size = (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)Math.Clamp(image.Width / 4, 16, 512));
            return (image.ToCubeFaces(Math.Min(size, 512), rotationDegrees), Math.Min(size, 512));
        }

        private static (Vector3[][]? Faces, int Size) ImageFaces(string[] paths)
        {
            var decoded = paths.Select(DecodeBgra).ToArray();
            var size = decoded[0].Width;
            if (!decoded.All(d => d.Width == size && d.Height == size)) return (null, 0);
            var lut = new float[256];
            for (var i = 0; i < 256; i++)
            {
                var s = i / 255f;
                lut[i] = s <= 0.04045f ? s / 12.92f : MathF.Pow((s + 0.055f) / 1.055f, 2.4f);
            }
            var faces = decoded.Select(d =>
            {
                var face = new Vector3[size * size];
                for (var i = 0; i < face.Length; i++)
                    face[i] = new Vector3(lut[d.Pixels[i * 4 + 2]], lut[d.Pixels[i * 4 + 1]], lut[d.Pixels[i * 4]]);
                return face;
            }).ToArray();
            return (faces, size);
        }

        private SkyboxResource CreateSkyCube(Vector3[][] faces, int size)
        {
            var sh = Core.Materials.SphericalHarmonics.ToIrradiance(Core.Materials.SphericalHarmonics.ProjectCube(faces, size));

            // Mip chain per face (2x2 box filter; odd sizes clamp).
            var mipCount = 1 + (int)Math.Floor(Math.Log2(size));
            var levels = new ushort[6 * mipCount][];
            var sizes = new int[mipCount];
            for (var f = 0; f < 6; f++)
            {
                var current = faces[f];
                var s = size;
                for (var m = 0; m < mipCount; m++)
                {
                    sizes[m] = s;
                    levels[f * mipCount + m] = ToHalf(current);
                    if (m + 1 == mipCount) break;
                    var ns = Math.Max(1, s / 2);
                    var next = new Vector3[ns * ns];
                    for (var y = 0; y < ns; y++)
                    for (var x = 0; x < ns; x++)
                    {
                        int x0 = Math.Min(2 * x, s - 1), x1 = Math.Min(2 * x + 1, s - 1);
                        int y0 = Math.Min(2 * y, s - 1), y1 = Math.Min(2 * y + 1, s - 1);
                        next[y * ns + x] = (current[y0 * s + x0] + current[y0 * s + x1] + current[y1 * s + x0] + current[y1 * s + x1]) * 0.25f;
                    }
                    current = next;
                    s = ns;
                }
            }

            var handles = levels.Select(l => GCHandle.Alloc(l, GCHandleType.Pinned)).ToArray();
            try
            {
                var data = new SubresourceData[levels.Length];
                for (var i = 0; i < levels.Length; i++)
                    data[i] = new SubresourceData(handles[i].AddrOfPinnedObject(), (uint)(sizes[i % mipCount] * 8), 0);
                var desc = new Texture2DDescription(Format.R16G16B16A16_Float, (uint)size, (uint)size, 6, (uint)mipCount,
                    BindFlags.ShaderResource, ResourceUsage.Immutable, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.TextureCube);
                using var tex = _gfx.Device.CreateTexture2D(desc, data);
                var srv = _gfx.Device.CreateShaderResourceView(tex, null);
                return new SkyboxResource(srv, mipCount, sh);
            }
            finally
            {
                foreach (var h in handles) h.Free();
            }

            static ushort[] ToHalf(Vector3[] pixels)
            {
                var result = new ushort[pixels.Length * 4];
                var one = BitConverter.HalfToUInt16Bits((Half)1f);
                for (var i = 0; i < pixels.Length; i++)
                {
                    // float16 tops out at 65504: clamp so a blazing sun stays finite.
                    var p = pixels[i];
                    if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z)) p = Vector3.Zero;
                    p = Vector3.Clamp(p, Vector3.Zero, new Vector3(60000f));
                    result[i * 4] = BitConverter.HalfToUInt16Bits((Half)p.X);
                    result[i * 4 + 1] = BitConverter.HalfToUInt16Bits((Half)p.Y);
                    result[i * 4 + 2] = BitConverter.HalfToUInt16Bits((Half)p.Z);
                    result[i * 4 + 3] = one;
                }
                return result;
            }
        }

        public static bool TryResolveFacePaths(string source, out string[] facePaths)
        {
            facePaths = Array.Empty<string>();
            string prefix;
            try { prefix = Directory.Exists(source) ? Path.Combine(source, "cube") : source; }
            catch (Exception) { return false; }

            var resolved = new string[FaceSuffixes.Length];
            for (var i = 0; i < FaceSuffixes.Length; i++)
            {
                var match = SupportedExtensions.Select(ext => prefix + FaceSuffixes[i] + ext).FirstOrDefault(File.Exists);
                if (match is null) return false;
                resolved[i] = match;
            }
            facePaths = resolved;
            return true;
        }

        internal static (byte[] Pixels, int Width, int Height) DecodeBgra(string path)
        {
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            BitmapSource frame = decoder.Frames[0];
            if (frame.Format != PixelFormats.Bgra32) frame = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var w = frame.PixelWidth;
            var h = frame.PixelHeight;
            var pixels = new byte[w * h * 4];
            frame.CopyPixels(pixels, w * 4, 0);
            return (pixels, w, h);
        }

        private unsafe ID3D11ShaderResourceView? CreateMippedTexture(byte[] pixels, int w, int h, Format format)
        {
            var mipLevels = 1 + (int)Math.Floor(Math.Log2(Math.Max(w, h)));
            var desc = new Texture2DDescription(format, (uint)w, (uint)h, 1, (uint)mipLevels,
                BindFlags.ShaderResource | BindFlags.RenderTarget, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.GenerateMips);
            using var tex = _gfx.Device.CreateTexture2D(desc);
            fixed (byte* p = pixels)
                _gfx.Context.UpdateSubresource(tex, 0, null, (IntPtr)p, (uint)(w * 4), 0);
            var srv = _gfx.Device.CreateShaderResourceView(tex, null);
            _gfx.Context.GenerateMips(srv);
            return srv;
        }

        public void Dispose()
        {
            foreach (var (_, entry) in _files) entry.Srv?.Dispose();
            _files.Clear();
            foreach (var srv in _paintedViews) srv.Dispose();
            _paintedViews.Clear();
            _cube?.Sky?.Srv.Dispose();
            _cube = null;
        }
    }
}
