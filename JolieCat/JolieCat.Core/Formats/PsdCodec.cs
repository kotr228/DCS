using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using JolieCat.Core.Documents;
using JolieCat.Core.Rendering;
using JolieCat.Shared.Enums;
using SkiaSharp;

namespace JolieCat.Core.Formats
{
    /// <summary>The result of reading a PSD: its canvas size and a ready-to-use scene.</summary>
    public sealed record PsdDocument(int Width, int Height, Scene Scene);

    /// <summary>
    /// Reads and writes Adobe Photoshop (.psd) files: 8-bit RGB, layered, with layer
    /// names (including Unicode), visibility, opacity, blend modes, transparency and
    /// layer masks. That covers what JolieCat itself can represent; adjustment layers,
    /// text layers, effects, groups and 16/32-bit or CMYK files are out of scope -
    /// groups are flattened into plain layers and unsupported color modes are rejected
    /// with a clear <see cref="NotSupportedException"/>.
    /// </summary>
    public static class PsdCodec
    {
        private const short ChannelRed = 0;
        private const short ChannelGreen = 1;
        private const short ChannelBlue = 2;
        private const short ChannelAlpha = -1;
        private const short ChannelUserMask = -2;

        private static readonly (BlendMode Mode, string Key)[] BlendKeys =
        {
            (BlendMode.Normal, "norm"),
            (BlendMode.Multiply, "mul "),
            (BlendMode.Screen, "scrn"),
            (BlendMode.Overlay, "over"),
            (BlendMode.Darken, "dark"),
            (BlendMode.Lighten, "lite"),
            (BlendMode.ColorDodge, "div "),
            (BlendMode.ColorBurn, "idiv"),
            (BlendMode.HardLight, "hLit"),
            (BlendMode.SoftLight, "sLit"),
            (BlendMode.Difference, "diff"),
            (BlendMode.Exclusion, "smud"),
            (BlendMode.Hue, "hue "),
            (BlendMode.Saturation, "sat "),
            (BlendMode.Color, "colr"),
            (BlendMode.Luminosity, "lum "),
            (BlendMode.Dissolve, "diss"),
            (BlendMode.LinearBurn, "lbrn"),
            (BlendMode.DarkerColor, "dkCl"),
            (BlendMode.LinearDodge, "lddg"),
            (BlendMode.LighterColor, "lgCl"),
            (BlendMode.VividLight, "vLit"),
            (BlendMode.LinearLight, "lLit"),
            (BlendMode.PinLight, "pLit"),
            (BlendMode.HardMix, "hMix"),
            (BlendMode.Subtract, "fsub"),
            (BlendMode.Divide, "fdiv"),
        };

        // ============================== Writing ==============================

        public static void Write(string path, Scene scene, int width, int height)
        {
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
            Write(stream, scene, width, height);
        }

        public static void Write(Stream output, Scene scene, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(output);
            ArgumentNullException.ThrowIfNull(scene);
            if (width <= 0 || height <= 0 || width > 30000 || height > 30000)
                throw new ArgumentOutOfRangeException(nameof(width), "PSD supports sizes from 1 to 30000 pixels.");

            var w = new BigEndianWriter(output);

            // Header
            w.Ascii("8BPS");
            w.U16(1);
            w.Zeros(6);
            w.U16(3); // channels in the composite image (RGB)
            w.U32((uint)height);
            w.U32((uint)width);
            w.U16(8); // bits per channel
            w.U16(3); // RGB color mode

            w.U32(0); // color mode data
            w.U32(0); // image resources

            WriteLayerAndMaskSection(w, scene, width, height);

            // Merged composite (what viewers without layer support show), RLE compressed.
            using var flat = SceneCompositor.Flatten(scene, width, height);
            var pixels = flat.Pixels;
            var planes = new byte[3][];
            for (var c = 0; c < 3; c++) planes[c] = new byte[width * height];
            for (var i = 0; i < pixels.Length; i++)
            {
                // Composite over white so a viewer that ignores transparency still looks right.
                var p = pixels[i];
                planes[0][i] = OverWhite(p.Red, p.Alpha);
                planes[1][i] = OverWhite(p.Green, p.Alpha);
                planes[2][i] = OverWhite(p.Blue, p.Alpha);
            }

            w.U16(1); // RLE
            var encoded = new List<byte[]>();
            foreach (var plane in planes)
                for (var y = 0; y < height; y++)
                    encoded.Add(PackBits.Encode(plane, y * width, width));
            foreach (var row in encoded) w.U16((ushort)row.Length);
            foreach (var row in encoded) w.Bytes(row);
        }

        /// <summary>PSD adjustment layers need Photoshop's own per-adjustment data
        /// blocks; instead an adjustment layer is exported as ordinary pixels: the
        /// adjusted image of everything beneath it. With its mask and opacity kept, it
        /// looks the same in Photoshop - it just isn't editable as an adjustment there.</summary>
        private static SKColor[] RasterizeAdjustment(Scene scene, int layerIndex, int width, int height)
        {
            using var below = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            below.Erase(SKColors.Transparent);
            SceneCompositor.DrawLayersInto(below, scene, 0, layerIndex);

            using var adjusted = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            adjusted.Erase(SKColors.Transparent);
            using (var canvas = new SKCanvas(adjusted))
            using (var filter = scene.Layers[layerIndex].Adjustment!.CreateColorFilter())
            using (var paint = new SKPaint { ColorFilter = filter })
                canvas.DrawBitmap(below, 0, 0, paint);

            return adjusted.Pixels;
        }

        /// <summary>Layer styles are exported baked into the layer's pixels (the
        /// mask is written separately, so it's left out here).</summary>
        private static SKColor[] RasterizeStyle(Documents.Layer layer)
        {
            using var styled = SceneCompositor.RenderStyledLayer(layer, applyMask: false);
            return styled.Pixels;
        }

        private static byte OverWhite(byte value, byte alpha) => (byte)((value * alpha + 255 * (255 - alpha) + 127) / 255);

        private static void WriteLayerAndMaskSection(BigEndianWriter w, Scene scene, int width, int height)
        {
            using var layerInfo = new MemoryStream();
            var li = new BigEndianWriter(layerInfo);

            var layers = scene.Layers;

            // Photoshop stores a group as an end-of-group marker layer below the members
            // and the group's own (empty) layer above them, both tagged 'lsct'.
            var records = new List<(int LayerIndex, Documents.Layer? DividerFor)>();
            for (var layerIndex = 0; layerIndex < layers.Count; layerIndex++)
            {
                var startingHere = layers
                    .Where(g => g.IsGroup && scene.BlockOf(g).Start == layerIndex)
                    .OrderBy(g => scene.Depth(g));
                foreach (var group in startingHere)
                    records.Add((-1, group));
                records.Add((layerIndex, null));
            }
            li.I16((short)records.Count);

            var channelData = new List<List<byte[]>>(); // per layer, per channel: encoded block (compression + data)
            SKColor[]? empty = null;

            foreach (var (layerIndex, dividerFor) in records)
            {
                var layer = dividerFor ?? layers[layerIndex];
                var isMarker = dividerFor is not null || layer.IsGroup;
                var pixels = isMarker ? (empty ??= new SKColor[width * height])
                    : layer.IsAdjustment ? RasterizeAdjustment(scene, layerIndex, width, height)
                    : layer.Style is { HasAnyEffect: true } ? RasterizeStyle(layer)
                    : layer.Bitmap.Pixels;
                var recordName = dividerFor is not null ? "</Layer group>" : layer.Name;
                var passThrough = layer.IsGroup && layer.BlendMode == BlendMode.Normal && layer.Opacity >= 1;
                var bounds = OpaqueBounds(pixels, width, height);
                var bw = bounds.Width;
                var bh = bounds.Height;

                var blocks = new List<byte[]>();
                var ids = new List<short> { ChannelAlpha, ChannelRed, ChannelGreen, ChannelBlue };
                foreach (var id in ids)
                    blocks.Add(EncodeChannel(pixels, width, bounds, id));

                SKRectI maskBounds = default;
                if (dividerFor is null && layer.Mask is { } mask)
                {
                    maskBounds = new SKRectI(0, 0, width, height);
                    var maskPixels = mask.Bitmap.Pixels;
                    var gray = new byte[width * height];
                    for (var i = 0; i < gray.Length; i++)
                        gray[i] = Luma(maskPixels[i]);
                    blocks.Add(EncodeGray(gray, width, height));
                    ids.Add(ChannelUserMask);
                }

                // Layer record
                li.I32(bounds.Top);
                li.I32(bounds.Left);
                li.I32(bounds.Top + bh);
                li.I32(bounds.Left + bw);
                li.U16((ushort)ids.Count);
                for (var c = 0; c < ids.Count; c++)
                {
                    li.I16(ids[c]);
                    li.U32((uint)blocks[c].Length);
                }

                li.Ascii("8BIM");
                li.Ascii(dividerFor is not null ? "norm" : passThrough ? "pass" : KeyFor(layer.BlendMode));
                li.U8((byte)(dividerFor is not null ? 255 : Math.Clamp(Math.Round(layer.Opacity * 255), 0, 255)));
                li.U8((byte)(!isMarker && layer.IsClipped ? 1 : 0)); // clipping: 0 base, 1 clipped
                li.U8((byte)(dividerFor is not null || layer.IsVisible ? 0 : 0x02));
                li.U8(0); // filler

                using var extra = new MemoryStream();
                var ex = new BigEndianWriter(extra);
                if (dividerFor is null && layer.Mask is { } m)
                {
                    ex.U32(20);
                    ex.I32(maskBounds.Top);
                    ex.I32(maskBounds.Left);
                    ex.I32(maskBounds.Bottom);
                    ex.I32(maskBounds.Right);
                    ex.U8(255); // default color outside the mask rect
                    ex.U8((byte)(m.IsEnabled ? 0 : 0x02));
                    ex.Zeros(2);
                }
                else
                {
                    ex.U32(0);
                }

                ex.U32(0); // blending ranges
                ex.PascalString(recordName, 4);

                // 'luni' keeps non-ASCII names (the Pascal name above is ASCII-only).
                ex.Ascii("8BIM");
                ex.Ascii("luni");
                var nameLength = 4 + recordName.Length * 2;
                var padded = (nameLength + 3) & ~3;
                ex.U32((uint)padded);
                ex.U32((uint)recordName.Length);
                foreach (var ch in recordName) ex.U16(ch);
                ex.Zeros(padded - nameLength);

                if (isMarker)
                {
                    // Section type: 1 open folder, 2 closed folder, 3 end-of-group marker.
                    ex.Ascii("8BIM");
                    ex.Ascii("lsct");
                    ex.U32(12);
                    ex.U32(dividerFor is not null ? 3u : layer.IsExpanded ? 1u : 2u);
                    ex.Ascii("8BIM");
                    ex.Ascii(dividerFor is not null ? "norm" : passThrough ? "pass" : KeyFor(layer.BlendMode));
                }

                li.U32((uint)extra.Length);
                li.Bytes(extra.ToArray());

                channelData.Add(blocks);
            }

            foreach (var blocks in channelData)
                foreach (var block in blocks)
                    li.Bytes(block);

            if (layerInfo.Length % 2 != 0) li.U8(0);

            w.U32((uint)(4 + layerInfo.Length + 4));
            w.U32((uint)layerInfo.Length);
            w.Bytes(layerInfo.ToArray());
            w.U32(0); // global layer mask info
        }

        private static string KeyFor(BlendMode mode)
        {
            foreach (var (m, key) in BlendKeys)
                if (m == mode) return key;
            return "norm";
        }

        private static byte Luma(SKColor c) =>
            (byte)Math.Clamp((0.2126 * c.Red + 0.7152 * c.Green + 0.0722 * c.Blue) * c.Alpha / 255.0 + 0.5, 0, 255);

        /// <summary>Smallest rectangle holding every non-transparent pixel - keeps layer
        /// data small. An empty layer gets an empty rectangle.</summary>
        private static SKRectI OpaqueBounds(SKColor[] pixels, int width, int height)
        {
            int minX = width, minY = height, maxX = -1, maxY = -1;
            for (var y = 0; y < height; y++)
            {
                var row = y * width;
                for (var x = 0; x < width; x++)
                {
                    if (pixels[row + x].Alpha == 0) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            return maxX < 0 ? new SKRectI(0, 0, 0, 0) : new SKRectI(minX, minY, maxX + 1, maxY + 1);
        }

        private static byte[] EncodeChannel(SKColor[] pixels, int stride, SKRectI bounds, short id)
        {
            var bw = bounds.Width;
            var bh = bounds.Height;
            var plane = new byte[bw * bh];
            for (var y = 0; y < bh; y++)
            {
                var src = (bounds.Top + y) * stride + bounds.Left;
                for (var x = 0; x < bw; x++)
                {
                    var p = pixels[src + x];
                    plane[y * bw + x] = id switch
                    {
                        ChannelRed => p.Red,
                        ChannelGreen => p.Green,
                        ChannelBlue => p.Blue,
                        _ => p.Alpha,
                    };
                }
            }

            return EncodeGray(plane, bw, bh);
        }

        /// <summary>One channel block: compression flag (RLE) + row byte counts + rows.</summary>
        private static byte[] EncodeGray(byte[] plane, int width, int height)
        {
            using var ms = new MemoryStream();
            var w = new BigEndianWriter(ms);
            w.U16(1);
            if (width == 0 || height == 0) return ms.ToArray();

            var rows = new byte[height][];
            for (var y = 0; y < height; y++)
                rows[y] = PackBits.Encode(plane, y * width, width);
            foreach (var row in rows) w.U16((ushort)row.Length);
            foreach (var row in rows) w.Bytes(row);
            return ms.ToArray();
        }

        // ============================== Reading ==============================

        public static PsdDocument Read(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
            return Read(stream, Path.GetFileNameWithoutExtension(path));
        }

        public static PsdDocument Read(Stream input, string sceneName = "Imported PSD")
        {
            ArgumentNullException.ThrowIfNull(input);
            var r = new BigEndianReader(input);

            if (r.Ascii(4) != "8BPS") throw new InvalidDataException("Not a Photoshop file.");
            var version = r.U16();
            if (version != 1) throw new NotSupportedException("Large-document (PSB) files aren't supported.");
            r.Skip(6);
            var channels = r.U16();
            var height = (int)r.U32();
            var width = (int)r.U32();
            var depth = r.U16();
            var colorMode = r.U16();

            if (depth != 8) throw new NotSupportedException($"Only 8-bit PSD files are supported (this one is {depth}-bit).");
            if (colorMode != 3 && colorMode != 1)
                throw new NotSupportedException("Only RGB and Grayscale PSD files are supported.");
            var isGray = colorMode == 1;

            r.Skip(r.U32()); // color mode data
            r.Skip(r.U32()); // image resources

            var scene = new Scene(string.IsNullOrWhiteSpace(sceneName) ? "Imported PSD" : sceneName);
            try
            {
                var layerSectionLength = r.U32();
                var layerSectionEnd = r.Position + layerSectionLength;
                if (layerSectionLength > 0)
                    ReadLayers(r, scene, width, height, isGray);
                r.Seek(layerSectionEnd);

                if (scene.Layers.Count == 0)
                    ReadComposite(r, scene, width, height, channels, isGray);

                if (scene.Layers.Count > 0) scene.ActiveLayer = scene.Layers[^1];
                return new PsdDocument(width, height, scene);
            }
            catch
            {
                scene.Dispose();
                throw;
            }
        }

        private sealed class LayerRecord
        {
            public int Top, Left, Bottom, Right;
            public readonly List<(short Id, uint Length)> Channels = new();
            public string BlendKey = "norm";
            public byte Opacity = 255;
            public byte Flags;
            public byte Clipping;
            public string Name = "Layer";
            public SKRectI MaskRect;
            public bool MaskDisabled;
            public byte MaskDefault = 255;
            /// <summary>'lsct': 0 none, 1/2 group (open/closed), 3 end-of-group marker.</summary>
            public uint SectionType;
            public string? SectionBlendKey;
        }

        private static void ReadLayers(BigEndianReader r, Scene scene, int width, int height, bool isGray)
        {
            var layerInfoLength = r.U32();
            if (layerInfoLength == 0) return;
            var layerInfoEnd = r.Position + layerInfoLength;

            var count = Math.Abs((int)r.I16());
            var records = new List<LayerRecord>(count);

            for (var i = 0; i < count; i++)
            {
                var rec = new LayerRecord
                {
                    Top = r.I32(),
                    Left = r.I32(),
                    Bottom = r.I32(),
                    Right = r.I32(),
                };

                var channelCount = r.U16();
                for (var c = 0; c < channelCount; c++)
                    rec.Channels.Add((r.I16(), r.U32()));

                if (r.Ascii(4) != "8BIM") throw new InvalidDataException("Corrupt layer record.");
                rec.BlendKey = r.Ascii(4);
                rec.Opacity = r.U8();
                rec.Clipping = r.U8();
                rec.Flags = r.U8();
                r.U8(); // filler

                var extraLength = r.U32();
                var extraEnd = r.Position + extraLength;

                var maskLength = r.U32();
                var maskEnd = r.Position + maskLength;
                if (maskLength >= 18)
                {
                    rec.MaskRect = new SKRectI(0, 0, 0, 0);
                    var top = r.I32();
                    var left = r.I32();
                    var bottom = r.I32();
                    var right = r.I32();
                    rec.MaskRect = new SKRectI(left, top, right, bottom);
                    rec.MaskDefault = r.U8();
                    rec.MaskDisabled = (r.U8() & 0x02) != 0;
                }
                r.Seek(maskEnd);

                r.Skip(r.U32()); // blending ranges

                var nameLength = r.U8();
                rec.Name = Encoding.ASCII.GetString(r.Bytes(nameLength));
                var consumed = 1 + nameLength;
                r.Skip((4 - consumed % 4) % 4);

                // Additional layer information blocks.
                while (r.Position + 12 <= extraEnd)
                {
                    var signature = r.Ascii(4);
                    if (signature != "8BIM" && signature != "8B64") break;
                    var key = r.Ascii(4);
                    var length = r.U32();
                    var blockEnd = r.Position + length;

                    if (key == "luni" && length >= 4)
                    {
                        var chars = (int)r.U32();
                        var sb = new StringBuilder(chars);
                        for (var c = 0; c < chars; c++)
                        {
                            var ch = (char)r.U16();
                            if (ch != '\0') sb.Append(ch);
                        }
                        if (sb.Length > 0) rec.Name = sb.ToString();
                    }
                    else if (key == "lsct" || key == "lsdk")
                    {
                        // Group markers: rebuilt into layer groups below.
                        rec.SectionType = r.U32();
                        if (length >= 12 && r.Ascii(4) == "8BIM") rec.SectionBlendKey = r.Ascii(4);
                    }

                    r.Seek(blockEnd);
                }

                r.Seek(extraEnd);
                records.Add(rec);
            }

            // Members of the groups being read, innermost last. A marker (type 3)
            // opens a group; its folder record (type 1/2) above the members closes it.
            var openGroups = new Stack<List<Layer>>();

            foreach (var rec in records)
            {
                var lw = rec.Right - rec.Left;
                var lh = rec.Bottom - rec.Top;
                var planes = new Dictionary<short, byte[]>();

                foreach (var (id, length) in rec.Channels)
                {
                    var channelEnd = r.Position + length;
                    var isMask = id == ChannelUserMask;
                    var cw = isMask ? rec.MaskRect.Width : lw;
                    var ch = isMask ? rec.MaskRect.Height : lh;

                    if (length >= 2 && cw > 0 && ch > 0)
                    {
                        var compression = r.U16();
                        planes[id] = ReadPlane(r, compression, cw, ch);
                    }

                    r.Seek(channelEnd);
                }

                if (rec.SectionType == 3)
                {
                    openGroups.Push(new List<Layer>());
                    continue;
                }

                var name = string.IsNullOrWhiteSpace(rec.Name) ? "Layer" : rec.Name;

                if (rec.SectionType is 1 or 2)
                {
                    var blendKey = rec.SectionBlendKey ?? rec.BlendKey;
                    var group = new Layer(name, width, height, Shared.Enums.LayerType.Group)
                    {
                        IsVisible = (rec.Flags & 0x02) == 0,
                        Opacity = rec.Opacity / 255.0,
                        BlendMode = blendKey == "pass" ? BlendMode.Normal : ModeFor(blendKey),
                        IsExpanded = rec.SectionType == 1,
                    };
                    if (planes.TryGetValue(ChannelUserMask, out var groupMaskPlane))
                        ReadMask(group, rec, groupMaskPlane, width, height);
                    if (openGroups.Count > 0)
                        foreach (var member in openGroups.Pop())
                            member.ParentId = group.Id;
                    if (openGroups.Count > 0) openGroups.Peek().Add(group);
                    scene.AddLayer(group);
                    continue;
                }
                var layer = new Layer(name, width, height)
                {
                    IsVisible = (rec.Flags & 0x02) == 0,
                    Opacity = rec.Opacity / 255.0,
                    BlendMode = ModeFor(rec.BlendKey),
                    IsClipped = rec.Clipping != 0,
                };

                if (lw > 0 && lh > 0)
                {
                    var pixels = new SKColor[width * height];
                    planes.TryGetValue(ChannelRed, out var red);
                    planes.TryGetValue(ChannelGreen, out var green);
                    planes.TryGetValue(ChannelBlue, out var blue);
                    planes.TryGetValue(ChannelAlpha, out var alpha);
                    if (isGray) { green = red; blue = red; }

                    for (var y = 0; y < lh; y++)
                    {
                        var dy = rec.Top + y;
                        if (dy < 0 || dy >= height) continue;
                        for (var x = 0; x < lw; x++)
                        {
                            var dx = rec.Left + x;
                            if (dx < 0 || dx >= width) continue;
                            var i = y * lw + x;
                            pixels[dy * width + dx] = new SKColor(
                                red?[i] ?? 0, green?[i] ?? 0, blue?[i] ?? 0, alpha?[i] ?? 255);
                        }
                    }

                    layer.Bitmap.Pixels = pixels;
                }

                if (planes.TryGetValue(ChannelUserMask, out var maskPlane))
                    ReadMask(layer, rec, maskPlane, width, height);

                if (openGroups.Count > 0) openGroups.Peek().Add(layer);
                scene.AddLayer(layer);
            }

            scene.NormalizeGroups();

            r.Seek(layerInfoEnd);
        }

        /// <summary>Gives <paramref name="layer"/> the mask stored in a layer record.</summary>
        private static void ReadMask(Layer layer, LayerRecord rec, byte[] maskPlane, int width, int height)
        {
            var mask = layer.AddMask();
            mask.IsEnabled = !rec.MaskDisabled;
            var maskPixels = new SKColor[width * height];
            Array.Fill(maskPixels, new SKColor(rec.MaskDefault, rec.MaskDefault, rec.MaskDefault, 255));

            var mr = rec.MaskRect;
            for (var y = 0; y < mr.Height; y++)
            {
                var dy = mr.Top + y;
                if (dy < 0 || dy >= height) continue;
                for (var x = 0; x < mr.Width; x++)
                {
                    var dx = mr.Left + x;
                    if (dx < 0 || dx >= width) continue;
                    var v = maskPlane[y * mr.Width + x];
                    maskPixels[dy * width + dx] = new SKColor(v, v, v, 255);
                }
            }

            mask.Bitmap.Pixels = maskPixels;
        }

        private static void ReadComposite(BigEndianReader r, Scene scene, int width, int height, int channels, bool isGray)
        {
            var compression = r.U16();
            var planeCount = Math.Min((int)channels, isGray ? 2 : 4);
            var planes = new byte[planeCount][];

            if (compression == 1)
            {
                // All row counts for every channel come first, then all the data.
                var counts = new int[channels * height];
                for (var i = 0; i < counts.Length; i++) counts[i] = r.U16();
                for (var c = 0; c < channels; c++)
                {
                    var plane = new byte[width * height];
                    for (var y = 0; y < height; y++)
                        PackBits.Decode(r.Bytes(counts[c * height + y]), plane, y * width, width);
                    if (c < planeCount) planes[c] = plane;
                }
            }
            else if (compression == 0)
            {
                for (var c = 0; c < planeCount; c++)
                    planes[c] = r.Bytes(width * height);
            }
            else
            {
                throw new NotSupportedException("ZIP-compressed PSD image data isn't supported.");
            }

            var pixels = new SKColor[width * height];
            for (var i = 0; i < pixels.Length; i++)
            {
                byte red, green, blue, alpha = 255;
                if (isGray)
                {
                    red = green = blue = planes[0][i];
                    if (planeCount > 1) alpha = planes[1][i];
                }
                else
                {
                    red = planes[0][i];
                    green = planes[1][i];
                    blue = planes[2][i];
                    if (planeCount > 3) alpha = planes[3][i];
                }

                pixels[i] = new SKColor(red, green, blue, alpha);
            }

            var layer = new Layer("Background", width, height);
            layer.Bitmap.Pixels = pixels;
            scene.AddLayer(layer);
        }

        private static byte[] ReadPlane(BigEndianReader r, int compression, int width, int height)
        {
            switch (compression)
            {
                case 0:
                    return r.Bytes(width * height);
                case 1:
                {
                    var counts = new int[height];
                    for (var y = 0; y < height; y++) counts[y] = r.U16();
                    var plane = new byte[width * height];
                    for (var y = 0; y < height; y++)
                        PackBits.Decode(r.Bytes(counts[y]), plane, y * width, width);
                    return plane;
                }
                default:
                    throw new NotSupportedException("ZIP-compressed PSD layers aren't supported. Re-save the file with \"Maximize compatibility\" or as RLE.");
            }
        }

        private static BlendMode ModeFor(string key)
        {
            foreach (var (mode, k) in BlendKeys)
                if (k == key) return mode;
            return BlendMode.Normal;
        }

        // ============================== Helpers ==============================

        /// <summary>Apple PackBits run-length encoding, as used by PSD's RLE compression.</summary>
        internal static class PackBits
        {
            public static byte[] Encode(byte[] source, int offset, int length)
            {
                var output = new List<byte>(length + length / 128 + 1);
                var i = 0;
                while (i < length)
                {
                    // Measure a run of identical bytes.
                    var runLength = 1;
                    while (i + runLength < length && runLength < 128 && source[offset + i + runLength] == source[offset + i])
                        runLength++;

                    if (runLength >= 3)
                    {
                        output.Add((byte)(1 - runLength));
                        output.Add(source[offset + i]);
                        i += runLength;
                        continue;
                    }

                    // Literal stretch until the next run of 3+ (or 128 bytes).
                    var start = i;
                    var literal = 0;
                    while (i < length && literal < 128)
                    {
                        if (i + 2 < length && source[offset + i] == source[offset + i + 1] && source[offset + i] == source[offset + i + 2])
                            break;
                        i++;
                        literal++;
                    }

                    output.Add((byte)(literal - 1));
                    for (var k = 0; k < literal; k++) output.Add(source[offset + start + k]);
                }

                return output.ToArray();
            }

            public static void Decode(byte[] input, byte[] destination, int offset, int length)
            {
                var i = 0;
                var o = 0;
                while (i < input.Length && o < length)
                {
                    var n = (sbyte)input[i++];
                    if (n >= 0)
                    {
                        var count = Math.Min(n + 1, length - o);
                        count = Math.Min(count, input.Length - i);
                        Array.Copy(input, i, destination, offset + o, count);
                        i += n + 1;
                        o += count;
                    }
                    else if (n != -128)
                    {
                        if (i >= input.Length) break;
                        var value = input[i++];
                        var count = Math.Min(1 - n, length - o);
                        destination.AsSpan(offset + o, count).Fill(value);
                        o += count;
                    }
                }
            }
        }

        private sealed class BigEndianWriter
        {
            private readonly Stream _s;
            public BigEndianWriter(Stream s) => _s = s;
            public void U8(byte v) => _s.WriteByte(v);
            public void U16(ushort v) { _s.WriteByte((byte)(v >> 8)); _s.WriteByte((byte)v); }
            public void I16(short v) => U16((ushort)v);
            public void U32(uint v) { U16((ushort)(v >> 16)); U16((ushort)v); }
            public void I32(int v) => U32((uint)v);
            public void Bytes(byte[] b) => _s.Write(b, 0, b.Length);
            public void Zeros(int n) { for (var i = 0; i < n; i++) _s.WriteByte(0); }
            public void Ascii(string s) => Bytes(Encoding.ASCII.GetBytes(s));

            /// <summary>Pascal string (ASCII, max 255 chars) padded so its total length is a multiple of <paramref name="pad"/>.</summary>
            public void PascalString(string s, int pad)
            {
                var sb = new StringBuilder();
                foreach (var ch in s) sb.Append(ch < 128 ? ch : '_');
                var bytes = Encoding.ASCII.GetBytes(sb.ToString());
                var len = Math.Min(bytes.Length, 255);
                U8((byte)len);
                _s.Write(bytes, 0, len);
                var total = 1 + len;
                Zeros((pad - total % pad) % pad);
            }
        }

        private sealed class BigEndianReader
        {
            private readonly Stream _s;
            public BigEndianReader(Stream s) => _s = s;
            public long Position => _s.Position;
            public void Seek(long position) => _s.Position = position;
            public void Skip(long count) => _s.Position += count;

            public byte U8()
            {
                var b = _s.ReadByte();
                if (b < 0) throw new EndOfStreamException("Unexpected end of PSD file.");
                return (byte)b;
            }

            public ushort U16() => (ushort)((U8() << 8) | U8());
            public short I16() => (short)U16();
            public uint U32() => ((uint)U16() << 16) | U16();
            public int I32() => (int)U32();

            public byte[] Bytes(int count)
            {
                var buffer = new byte[count];
                var read = 0;
                while (read < count)
                {
                    var n = _s.Read(buffer, read, count - read);
                    if (n <= 0) throw new EndOfStreamException("Unexpected end of PSD file.");
                    read += n;
                }
                return buffer;
            }

            public string Ascii(int count) => Encoding.ASCII.GetString(Bytes(count));
        }
    }
}
