using System;
using System.IO;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace JolieCat.UI.Media
{
    /// <summary>
    /// Converts a SkiaSharp bitmap to a frozen WPF <see cref="BitmapSource"/> for
    /// display - currently only used for the Layers panel's mask thumbnail. Goes
    /// through a PNG-encoded byte round-trip rather than a direct pixel-buffer copy:
    /// simple and correct regardless of either side's color/alpha layout, and cheap
    /// enough at the small thumbnail sizes this is actually used at.
    /// </summary>
    public static class SkiaImageConverter
    {
        /// <summary>Downscales <paramref name="bitmap"/> to fit within
        /// (<paramref name="maxSize"/>, <paramref name="maxSize"/>) - preserving aspect
        /// ratio, never upscaling a bitmap already smaller than that - and returns it as
        /// a frozen <see cref="BitmapSource"/>. Frozen so it's safe to hand straight to
        /// a binding regardless of which thread produced it, and so the bound
        /// <c>Image</c> doesn't keep the backing <see cref="MemoryStream"/> alive.</summary>
        public static BitmapSource ToThumbnail(SKBitmap bitmap, int maxSize = 32)
        {
            ArgumentNullException.ThrowIfNull(bitmap);

            var scale = Math.Min(1f, Math.Min((float)maxSize / bitmap.Width, (float)maxSize / bitmap.Height));
            using var resized = scale < 1f
                ? bitmap.Resize(
                    new SKImageInfo(Math.Max(1, (int)Math.Round(bitmap.Width * scale)), Math.Max(1, (int)Math.Round(bitmap.Height * scale))),
                    new SKSamplingOptions(SKFilterMode.Linear))
                : null;

            using var image = SKImage.FromBitmap(resized ?? bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = new MemoryStream(data.ToArray());

            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.StreamSource = stream;
            bitmapImage.EndInit();
            bitmapImage.Freeze();
            return bitmapImage;
        }
    }
}
