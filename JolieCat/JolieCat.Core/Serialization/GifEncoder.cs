using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkiaSharp;

namespace JolieCat.Core.Serialization
{
    /// <summary>
    /// Writes animated GIFs (SkiaSharp can read GIF but not write it). Each frame gets
    /// its own palette of up to 255 colors (median cut) plus one transparent entry, so
    /// photos and pixel art both come out well; frames loop forever by default.
    /// </summary>
    public static class GifEncoder
    {
        /// <param name="frames">The frames, all the same size.</param>
        /// <param name="delaysMs">How long each frame shows, in milliseconds (GIF keeps hundredths).</param>
        public static void Write(Stream output, IReadOnlyList<SKBitmap> frames, IReadOnlyList<int> delaysMs, bool loop = true)
        {
            if (frames.Count == 0) throw new ArgumentException("No frames.", nameof(frames));
            int w = frames[0].Width, h = frames[0].Height;
            using var writer = new BinaryWriter(output, System.Text.Encoding.ASCII, leaveOpen: true);

            writer.Write("GIF89a".ToCharArray());
            writer.Write((ushort)w);
            writer.Write((ushort)h);
            writer.Write((byte)0x70); // no global color table, 8-bit color resolution
            writer.Write((byte)0);    // background color index
            writer.Write((byte)0);    // pixel aspect ratio

            if (loop)
            {
                writer.Write(new byte[] { 0x21, 0xFF, 0x0B });
                writer.Write("NETSCAPE2.0".ToCharArray());
                writer.Write(new byte[] { 0x03, 0x01, 0x00, 0x00, 0x00 }); // repeat forever
            }

            for (var f = 0; f < frames.Count; f++)
            {
                var frame = frames[f];
                if (frame.Width != w || frame.Height != h) throw new ArgumentException("All frames must be the same size.", nameof(frames));
                var (palette, indices, transparent) = Quantize(frame.Pixels);

                // Graphic control: delay, transparency; each frame replaces the last.
                var delay = (ushort)Math.Clamp((f < delaysMs.Count ? delaysMs[f] : 100) / 10, 2, 65535);
                writer.Write(new byte[] { 0x21, 0xF9, 0x04 });
                writer.Write((byte)((2 << 2) | (transparent ? 1 : 0))); // dispose: restore to background
                writer.Write(delay);
                writer.Write((byte)0); // transparent index (always entry 0)
                writer.Write((byte)0);

                // Image descriptor with a local 256-entry color table.
                writer.Write((byte)0x2C);
                writer.Write((ushort)0); writer.Write((ushort)0);
                writer.Write((ushort)w); writer.Write((ushort)h);
                writer.Write((byte)0x87); // local table, 2^(7+1) = 256 entries
                for (var i = 0; i < 256; i++)
                {
                    var c = i < palette.Count ? palette[i] : SKColors.Black;
                    writer.Write(c.Red); writer.Write(c.Green); writer.Write(c.Blue);
                }

                WriteLzw(writer, indices, 8);
            }

            writer.Write((byte)0x3B); // trailer
        }

        /// <summary>Median-cut palette (entry 0 kept for transparency) and each pixel's entry.</summary>
        internal static (List<SKColor> Palette, byte[] Indices, bool HasTransparency) Quantize(SKColor[] pixels, int maxColors = 255)
        {
            var transparent = false;
            var opaque = new List<int>(pixels.Length);
            for (var i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].Alpha < 128) transparent = true;
                else opaque.Add(i);
            }

            // Boxes of pixel indices, split along their widest channel until 255 boxes.
            var boxes = new List<List<int>>();
            if (opaque.Count > 0) boxes.Add(opaque);
            while (boxes.Count < Math.Clamp(maxColors, 1, 255))
            {
                var (index, channel, range) = (-1, 0, 0);
                for (var b = 0; b < boxes.Count; b++)
                {
                    if (boxes[b].Count < 2) continue;
                    var (ch, r) = WidestChannel(pixels, boxes[b]);
                    if (r > range) (index, channel, range) = (b, ch, r);
                }
                if (index < 0 || range == 0) break;
                var box = boxes[index];
                box.Sort((p, q) => Channel(pixels[p], channel).CompareTo(Channel(pixels[q], channel)));
                // Split where the channel's value changes, nearest the middle, so equal
                // colors never end up in two boxes (which would waste palette entries).
                var middle = Channel(pixels[box[box.Count / 2]], channel);
                var split = box.FindIndex(i => Channel(pixels[i], channel) >= middle);
                if (split <= 0) split = box.FindIndex(i => Channel(pixels[i], channel) > middle);
                boxes[index] = box.GetRange(0, split);
                boxes.Add(box.GetRange(split, box.Count - split));
            }

            var palette = new List<SKColor> { SKColors.Transparent };
            var indices = new byte[pixels.Length];
            for (var b = 0; b < boxes.Count; b++)
            {
                long r = 0, g = 0, bl = 0;
                foreach (var i in boxes[b]) { r += pixels[i].Red; g += pixels[i].Green; bl += pixels[i].Blue; }
                var n = boxes[b].Count;
                palette.Add(new SKColor((byte)(r / n), (byte)(g / n), (byte)(bl / n)));
                foreach (var i in boxes[b]) indices[i] = (byte)(b + 1);
            }
            return (palette, indices, transparent);
        }

        private static int Channel(SKColor c, int channel) => channel switch { 0 => c.Red, 1 => c.Green, _ => c.Blue };

        private static (int Channel, int Range) WidestChannel(SKColor[] pixels, List<int> box)
        {
            int minR = 255, maxR = 0, minG = 255, maxG = 0, minB = 255, maxB = 0;
            foreach (var i in box)
            {
                var c = pixels[i];
                if (c.Red < minR) minR = c.Red; if (c.Red > maxR) maxR = c.Red;
                if (c.Green < minG) minG = c.Green; if (c.Green > maxG) maxG = c.Green;
                if (c.Blue < minB) minB = c.Blue; if (c.Blue > maxB) maxB = c.Blue;
            }
            int r = maxR - minR, g = maxG - minG, b = maxB - minB;
            return r >= g && r >= b ? (0, r) : g >= b ? (1, g) : (2, b);
        }

        /// <summary>GIF's LZW: variable-width codes, packed least-significant-bit first,
        /// in sub-blocks of up to 255 bytes.</summary>
        private static void WriteLzw(BinaryWriter writer, byte[] indices, int minCodeSize)
        {
            writer.Write((byte)minCodeSize);
            var clear = 1 << minCodeSize;
            var end = clear + 1;

            var block = new List<byte>(255);
            int bitBuffer = 0, bitCount = 0;
            void Flush()
            {
                if (block.Count == 0) return;
                writer.Write((byte)block.Count);
                writer.Write(block.ToArray());
                block.Clear();
            }
            void Emit(int code, int size)
            {
                bitBuffer |= code << bitCount;
                bitCount += size;
                while (bitCount >= 8)
                {
                    block.Add((byte)(bitBuffer & 0xFF));
                    if (block.Count == 255) Flush();
                    bitBuffer >>= 8;
                    bitCount -= 8;
                }
            }

            var table = new Dictionary<int, int>();
            var codeSize = minCodeSize + 1;
            var next = end + 1;
            Emit(clear, codeSize);

            if (indices.Length > 0)
            {
                var prefix = (int)indices[0];
                for (var i = 1; i < indices.Length; i++)
                {
                    var k = indices[i];
                    var key = (prefix << 8) | k;
                    if (table.TryGetValue(key, out var code))
                    {
                        prefix = code;
                        continue;
                    }
                    Emit(prefix, codeSize);
                    if (next < 4096)
                    {
                        table[key] = next++;
                        if (next > (1 << codeSize) && codeSize < 12) codeSize++;
                    }
                    else
                    {
                        Emit(clear, codeSize);
                        table.Clear();
                        codeSize = minCodeSize + 1;
                        next = end + 1;
                    }
                    prefix = k;
                }
                Emit(prefix, codeSize);
            }

            Emit(end, codeSize);
            if (bitCount > 0) Emit(0, 8 - bitCount);
            Flush();
            writer.Write((byte)0); // block terminator
        }
    }
}
