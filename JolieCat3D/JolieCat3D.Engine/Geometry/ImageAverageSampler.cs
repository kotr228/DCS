using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JolieCat3D.Engine.Geometry
{
    /// <summary>
    /// Decodes an image at a small, fixed size and hands back its raw BGRA32 pixel bytes -
    /// the one piece of "read an image and average some of its channels" plumbing both
    /// <see cref="MaterialFactory"/> (metallic/roughness) and
    /// <c>Rendering.EnvironmentVisualFactory</c> (a skybox face's own average color) need,
    /// factored out here rather than duplicated in both, since the actual
    /// <see cref="BitmapImage"/>/<see cref="FormatConvertedBitmap"/> setup is identical
    /// either way - only WHICH channels a caller then averages differs.
    ///
    /// Deliberately decodes at a small fixed size rather than an image's own real
    /// resolution - an AVERAGE (the only thing either caller ever computes from this) is
    /// insensitive to resolution: a small downsample of even a 4K source image still
    /// averages out to essentially the same result a full-size decode would, so this
    /// keeps the cost of computing one low regardless of how large the source file
    /// actually is.
    /// </summary>
    internal static class ImageAverageSampler
    {
        /// <summary>The fixed width/height every decode uses - see this class's own
        /// remarks on why an average doesn't need (or want, for the extra decode cost) a
        /// full-resolution image.</summary>
        internal const int SampleSize = 32;

        /// <summary>Decodes <paramref name="fullPath"/> (an already-resolved, existing
        /// file path - this does no path resolution/existence checking of its own) at
        /// <see cref="SampleSize"/>x<see cref="SampleSize"/> and returns its pixel bytes
        /// in <see cref="PixelFormats.Bgra32"/> order (4 bytes per pixel: Blue, Green,
        /// Red, Alpha) - null if the file is missing, unreadable, or not a valid image,
        /// rather than throwing (every caller treats "can't sample this" as a normal,
        /// visibly-recoverable outcome - a missing/broken texture reference, never a
        /// reason to crash the render).</summary>
        internal static byte[]? TryDecodeSmallBgra32(string fullPath, out int width, out int height)
        {
            width = 0;
            height = 0;

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                // Bypasses WPF's own per-Uri decoded-frame cache - the same reason
                // MaterialFactory.CreateDiffuseBrush sets this, so a hot-reloaded texture
                // (the same path, new bytes on disk) is never served stale content just
                // because this exact path was already decoded once before.
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.DecodePixelWidth = SampleSize;
                bitmap.DecodePixelHeight = SampleSize;
                bitmap.UriSource = new Uri(fullPath, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();

                var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                width = converted.PixelWidth;
                height = converted.PixelHeight;
                if (width <= 0 || height <= 0) return null;

                var stride = width * 4;
                var pixels = new byte[stride * height];
                converted.CopyPixels(pixels, stride, 0);
                return pixels;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
