using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SkiaSharp;

namespace JolieCat.Core.Documents
{
    /// <summary>
    /// Reads a sprite atlas made by TexturePacker, Aseprite, JolieCat or similar: a PNG plus
    /// a JSON list of frames ("hash" or "array" layout, trimmed and rotated frames too), and
    /// gives back every sprite at its original size, grouped into its animations.
    /// </summary>
    public static class SpriteAtlasReader
    {
        public sealed record Sprite(string Name, SKBitmap Picture);

        public sealed record Atlas(string? ImageName, List<Sprite> Sprites, List<(string Name, List<string> Frames)> Animations);

        /// <summary>The frames and animations in <paramref name="json"/>; the sprites are cut
        /// from <paramref name="image"/> (which stays the caller's).</summary>
        public static Atlas Read(string json, SKBitmap? image)
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = doc.RootElement;
            string? imageName = root.TryGetProperty("meta", out var meta) && meta.TryGetProperty("image", out var img) ? img.GetString() : null;

            var entries = new List<(string Name, JsonElement Frame)>();
            if (root.TryGetProperty("frames", out var frames))
            {
                if (frames.ValueKind == JsonValueKind.Object)
                    entries.AddRange(frames.EnumerateObject().Select(p => (p.Name, p.Value)));
                else if (frames.ValueKind == JsonValueKind.Array)
                    entries.AddRange(frames.EnumerateArray().Select((f, i) => (f.TryGetProperty("filename", out var n) ? n.GetString() ?? $"frame_{i}" : $"frame_{i}", f)));
            }

            var sprites = new List<Sprite>();
            if (image is not null)
                foreach (var (name, frame) in entries)
                    if (Cut(image, frame) is { } picture) sprites.Add(new Sprite(name, picture));

            var animations = new List<(string, List<string>)>();
            if (root.TryGetProperty("animations", out var anims) && anims.ValueKind == JsonValueKind.Object)
            {
                foreach (var a in anims.EnumerateObject())
                    if (a.Value.ValueKind == JsonValueKind.Array)
                        animations.Add((a.Name, a.Value.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToList()));
            }
            else if (meta.ValueKind == JsonValueKind.Object && meta.TryGetProperty("frameTags", out var tags) && tags.ValueKind == JsonValueKind.Array)
            {
                // Aseprite: tags give frame number ranges.
                foreach (var tag in tags.EnumerateArray())
                {
                    var name = tag.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (!tag.TryGetProperty("from", out var from) || !tag.TryGetProperty("to", out var to)) continue;
                    var list = new List<string>();
                    for (var i = from.GetInt32(); i <= to.GetInt32() && i < entries.Count; i++) list.Add(entries[i].Name);
                    if (name.Length > 0 && list.Count > 0) animations.Add((name, list));
                }
            }
            return new Atlas(imageName, sprites, animations);
        }

        private static int Int(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.TryGetDouble(out var d) ? (int)Math.Round(d) : 0;

        /// <summary>One frame back at its original size: the stored part (turned back if the
        /// packer turned it) placed where it was before trimming.</summary>
        private static SKBitmap? Cut(SKBitmap image, JsonElement entry)
        {
            if (!entry.TryGetProperty("frame", out var f)) return null;
            int x = Int(f, "x"), y = Int(f, "y"), w = Int(f, "w"), h = Int(f, "h");
            if (w <= 0 || h <= 0) return null;
            var rotated = entry.TryGetProperty("rotated", out var r) && r.ValueKind == JsonValueKind.True;

            int sourceW = w, sourceH = h, offsetX = 0, offsetY = 0;
            if (entry.TryGetProperty("sourceSize", out var size)) { sourceW = Math.Max(1, Int(size, "w")); sourceH = Math.Max(1, Int(size, "h")); }
            if (entry.TryGetProperty("spriteSourceSize", out var sss)) { offsetX = Int(sss, "x"); offsetY = Int(sss, "y"); }

            var sprite = new SKBitmap(sourceW, sourceH, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(sprite);
            canvas.Clear(SKColors.Transparent);
            if (!rotated)
            {
                canvas.DrawBitmap(image, SKRect.Create(x, y, w, h), SKRect.Create(offsetX, offsetY, w, h));
            }
            else
            {
                // Stored turned 90° clockwise: it takes h x w pixels in the atlas. Turn it back.
                canvas.Save();
                canvas.Translate(offsetX, offsetY + h);
                canvas.RotateDegrees(-90);
                canvas.DrawBitmap(image, SKRect.Create(x, y, h, w), SKRect.Create(0, 0, h, w));
                canvas.Restore();
            }
            return sprite;
        }

        /// <summary>
        /// Lays the atlas out as a grid sheet: one row per animation (named after it), the
        /// sprites that are in no animation after them; without animations, a near-square grid.
        /// Returns the rows of pictures and their names (null for unnamed rows).
        /// </summary>
        public static List<(string? Name, List<SKBitmap> Sprites)> Rows(Atlas atlas)
        {
            var byName = atlas.Sprites.GroupBy(s => s.Name).ToDictionary(g => g.Key, g => g.First().Picture);
            var rows = new List<(string?, List<SKBitmap>)>();
            var used = new HashSet<string>();
            foreach (var (name, frames) in atlas.Animations)
            {
                var row = frames.Where(byName.ContainsKey).Select(f => { used.Add(f); return byName[f]; }).ToList();
                if (row.Count > 0) rows.Add((name, row));
            }
            var rest = atlas.Sprites.Where(s => !used.Contains(s.Name)).Select(s => s.Picture).ToList();
            if (rest.Count > 0)
            {
                var columns = rows.Count > 0 ? Math.Max(rows.Max(r => r.Item2.Count), 1) : (int)Math.Ceiling(Math.Sqrt(rest.Count));
                for (var i = 0; i < rest.Count; i += columns) rows.Add((null, rest.Skip(i).Take(columns).ToList()));
            }
            return rows;
        }
    }
}
