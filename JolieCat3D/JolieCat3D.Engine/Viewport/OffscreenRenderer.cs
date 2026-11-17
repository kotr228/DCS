using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JolieCat3D.Core.Scene;
using JolieCat3D.Engine.Rendering.Dx;

namespace JolieCat3D.Engine.Viewport
{
    /// <summary>
    /// Renders a scene with the same DirectX pipeline as the viewport into an image of any
    /// size - used for "Render Animation Frames", thumbnails and tests.
    /// </summary>
    public sealed class OffscreenRenderer : IDisposable
    {
        private readonly DxSceneRenderer _renderer;

        public OffscreenRenderer()
        {
            _renderer = new DxSceneRenderer(DxGraphics.Shared, sharedOutput: false);
        }

        public ViewportRenderSettings Settings
        {
            get => _renderer.Settings;
            set => _renderer.Settings = value;
        }

        /// <summary>True when no hardware GPU was found and the WARP software rasterizer is used.</summary>
        public static bool IsSoftwareFallback => DxGraphics.Shared.IsWarp;

        public BitmapSource Render(Scene3D scene, ViewportCamera camera, int width, int height,
            Node? selected = null, OverlayDrawList? overlay = null, Node? hiddenCamera = null)
        {
            _renderer.Prepare(scene, selected is null ? Array.Empty<Node>() : new[] { selected }, selected, null, -1, scene.ActiveCamera, hiddenCamera);
            _renderer.Draw(camera, width, height, overlay);
            var pixels = _renderer.ReadOutputPixels();
            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            bitmap.Freeze();
            return bitmap;
        }

        public void RenderToPng(Scene3D scene, ViewportCamera camera, int width, int height, string path,
            Node? selected = null, OverlayDrawList? overlay = null, Node? hiddenCamera = null)
        {
            var bitmap = Render(scene, camera, width, height, selected, overlay, hiddenCamera);
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }

        public void Dispose() => _renderer.Dispose();
    }
}
