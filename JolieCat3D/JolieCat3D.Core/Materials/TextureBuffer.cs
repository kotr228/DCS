using JolieCat3D.Core.Numerics;

namespace JolieCat3D.Core.Materials
{
    /// <summary>
    /// A plain, mutable, in-memory RGBA pixel grid - <see cref="Material.PaintedTextureBuffer"/>'s
    /// own live, brushable backing store for Texture Paint mode. Deliberately just a flat
    /// <see cref="Color4"/> array with no file-format/codec concept of its own at all -
    /// <c>JolieCat3D.Core</c> has no image codec (see <see cref="Material.DiffuseTexturePath"/>'s
    /// own remarks on why), so DECODING an actual image file into one of these (or
    /// re-encoding one back out to disk) is entirely <c>JolieCat3D.Engine</c>'s own job
    /// (<c>Editing.TexturePaintSession</c> for the former, <c>Geometry.MaterialFactory</c>
    /// for turning the CURRENT buffer into a renderable WPF brush) - this class only
    /// stores/mutates the pixels themselves, the same "the math/data lives in Core, the
    /// WPF adapter lives in Engine" split every other feature in this project already
    /// follows.
    ///
    /// Row 0 is the TOP row of the image (matching WPF's own <c>WriteableBitmap</c>/
    /// <c>BitmapSource</c> top-down row convention exactly, so <c>MaterialFactory</c> can
    /// copy <see cref="Pixels"/> straight into one with no extra per-row flip) - <see cref="TexturePaintBrush"/>
    /// is what actually converts an incoming UV (V=0 at the texture's own BOTTOM, this
    /// project's established convention - see <see cref="Material.DiffuseTextureOffset"/>'s
    /// own remarks) into this buffer's own top-down pixel row.
    /// </summary>
    public sealed class TextureBuffer
    {
        private readonly Color4[] _pixels;

        public int Width { get; }
        public int Height { get; }

        public TextureBuffer(int width, int height, Color4 fill = default)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

            Width = width;
            Height = height;
            _pixels = new Color4[width * height];

            // default(Color4) is (0,0,0,0) transparent black - the same "an omitted
            // color argument means something visibly wrong, not a sane default" trap
            // Vertex's own constructor already guards against, so an un-specified fill
            // gets opaque White instead (a blank canvas, not an invisible one).
            var actualFill = fill.Equals(default(Color4)) ? Color4.White : fill;
            Array.Fill(_pixels, actualFill);
        }

        /// <summary>Every pixel, row-major, row 0 first - what <c>MaterialFactory</c>
        /// copies straight into a <c>WriteableBitmap</c>'s own back buffer.</summary>
        public IReadOnlyList<Color4> Pixels => _pixels;

        public Color4 GetPixel(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height) throw new ArgumentOutOfRangeException();
            return _pixels[y * Width + x];
        }

        public void SetPixel(int x, int y, Color4 color)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height) throw new ArgumentOutOfRangeException();
            _pixels[y * Width + x] = color;
        }
    }
}
