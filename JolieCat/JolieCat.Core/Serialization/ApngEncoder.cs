using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SkiaSharp;

namespace JolieCat.Core.Serialization
{
    /// <summary>
    /// Writes animated PNGs (APNG): full color with transparency, played by every
    /// current browser. Each frame is encoded as a normal PNG by Skia, then its image
    /// data is repackaged as an APNG frame.
    /// </summary>
    public static class ApngEncoder
    {
        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        /// <param name="plays">How many times it plays; 0 = forever.</param>
        public static void Write(Stream output, IReadOnlyList<SKBitmap> frames, IReadOnlyList<int> delaysMs, int plays = 0)
        {
            if (frames.Count == 0) throw new ArgumentException("No frames.", nameof(frames));
            int w = frames[0].Width, h = frames[0].Height;
            output.Write(Signature);

            byte[]? header = null;
            uint sequence = 0;
            for (var f = 0; f < frames.Count; f++)
            {
                if (frames[f].Width != w || frames[f].Height != h) throw new ArgumentException("All frames must be the same size.", nameof(frames));
                var (ihdr, idat) = Split(Encode(frames[f]));
                if (header is null)
                {
                    header = ihdr;
                    WriteChunk(output, "IHDR", ihdr);
                    var actl = new byte[8];
                    BigEndian(actl, 0, (uint)frames.Count);
                    BigEndian(actl, 4, (uint)Math.Max(0, plays));
                    WriteChunk(output, "acTL", actl);
                }
                else if (!ihdr.AsSpan().SequenceEqual(header))
                {
                    throw new InvalidOperationException("The frames were encoded differently and can't be combined.");
                }

                // Frame control: the whole canvas, shown for its delay, replacing the last frame.
                var fctl = new byte[26];
                BigEndian(fctl, 0, sequence++);
                BigEndian(fctl, 4, (uint)w);
                BigEndian(fctl, 8, (uint)h);
                BigEndian(fctl, 12, 0);
                BigEndian(fctl, 16, 0);
                var delay = Math.Clamp(f < delaysMs.Count ? delaysMs[f] : 100, 1, 65535);
                fctl[20] = (byte)(delay >> 8); fctl[21] = (byte)delay;      // delay numerator (ms)
                fctl[22] = 1000 >> 8; fctl[23] = 1000 & 0xFF;              // over 1000 = milliseconds
                fctl[24] = 1;                                              // dispose: clear to transparent
                fctl[25] = 0;                                              // blend: replace
                WriteChunk(output, "fcTL", fctl);

                if (f == 0)
                {
                    WriteChunk(output, "IDAT", idat);
                }
                else
                {
                    var fdat = new byte[idat.Length + 4];
                    BigEndian(fdat, 0, sequence++);
                    Buffer.BlockCopy(idat, 0, fdat, 4, idat.Length);
                    WriteChunk(output, "fdAT", fdat);
                }
            }
            WriteChunk(output, "IEND", Array.Empty<byte>());
        }

        private static byte[] Encode(SKBitmap frame)
        {
            // Always RGBA, so every frame's header matches.
            using var rgba = new SKBitmap(new SKImageInfo(frame.Width, frame.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            using (var canvas = new SKCanvas(rgba))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(frame, 0, 0);
            }
            using var image = SKImage.FromBitmap(rgba);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }

        /// <summary>A PNG's header data and all of its image data joined.</summary>
        private static (byte[] Header, byte[] Data) Split(byte[] png)
        {
            byte[]? header = null;
            using var data = new MemoryStream();
            var i = 8;
            while (i + 8 <= png.Length)
            {
                var length = (png[i] << 24) | (png[i + 1] << 16) | (png[i + 2] << 8) | png[i + 3];
                var type = Encoding.ASCII.GetString(png, i + 4, 4);
                if (type == "IHDR") header = png.AsSpan(i + 8, length).ToArray();
                else if (type == "IDAT") data.Write(png, i + 8, length);
                i += 12 + length;
            }
            return (header ?? throw new InvalidDataException("PNG without a header."), data.ToArray());
        }

        private static void WriteChunk(Stream output, string type, byte[] data)
        {
            var head = new byte[8];
            BigEndian(head, 0, (uint)data.Length);
            Encoding.ASCII.GetBytes(type, 0, 4, head, 4);
            output.Write(head);
            output.Write(data);
            var crc = Crc32(head.AsSpan(4, 4), data);
            var tail = new byte[4];
            BigEndian(tail, 0, crc);
            output.Write(tail);
        }

        private static void BigEndian(byte[] b, int at, uint v)
        {
            b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v;
        }

        private static readonly uint[] CrcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                var c = n;
                for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }

        internal static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
        {
            var c = 0xFFFFFFFFu;
            foreach (var b in type) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
            foreach (var b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
            return c ^ 0xFFFFFFFFu;
        }
    }
}
