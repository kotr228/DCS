using SkiaSharp;

namespace JolieCat.Core.Clipboard
{
    /// <summary>
    /// An in-process pixel clipboard shared by every open document: Copy stores a
    /// cropped, selection-clipped bitmap here; Paste reads it back, into whichever
    /// document is currently active - including a different one than it was copied
    /// from, matching how a real clipboard behaves across documents.
    /// </summary>
    /// <remarks>
    /// Deliberately an internal buffer rather than the OS clipboard
    /// (<c>System.Windows.Clipboard</c>): WPF's own clipboard image support goes
    /// through <c>BitmapSource</c>/the Win32 DIB clipboard formats, which - a well-known
    /// WPF limitation - does not round-trip an alpha channel; a copied selection with
    /// any transparency would come back opaque (or with garbage in place of alpha) on
    /// paste. Since this app has no other process to interoperate with anyway, storing
    /// the exact <see cref="SKBitmap"/> (premultiplied RGBA, byte-for-byte) sidesteps
    /// that entirely rather than accepting lossy alpha for OS-clipboard interop nothing
    /// here actually needs.
    /// </remarks>
    public static class PixelClipboard
    {
        private static SKBitmap? _content;

        /// <summary>The last copied bitmap, or null if nothing has been copied yet (or
        /// <see cref="Clear"/> was called). Never mutated by a paste - the same content
        /// can be pasted repeatedly, like a real clipboard.</summary>
        public static SKBitmap? Content => _content;

        public static bool HasContent => _content is not null;

        /// <summary>Stores <paramref name="bitmap"/> as the clipboard's new content,
        /// taking ownership of it (disposing whatever was there before) - the caller
        /// must not use or dispose <paramref name="bitmap"/> itself afterward.</summary>
        public static void SetContent(SKBitmap bitmap)
        {
            if (ReferenceEquals(_content, bitmap)) return;

            _content?.Dispose();
            _content = bitmap;
        }

        /// <summary>Empties the clipboard, disposing its content if any.</summary>
        public static void Clear()
        {
            _content?.Dispose();
            _content = null;
        }
    }
}
