using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace JolieCat.UI.Media
{
    /// <summary>A brush preset: tip and dynamics settings. <paramref name="TipFile"/>: a
    /// picture tip (Edit &gt; Define Brush Preset) instead of the round one, or null.</summary>
    public sealed record BrushPreset(
        string Name, double Size, double Hardness, double Spacing, double Flow,
        double Angle, double Roundness, double SizeJitter, double Scatter, string? TipFile = null);

    /// <summary>
    /// Brush presets: a few built in, plus the user's own (saved in
    /// %AppData%\JolieCat\brushes.json). Shared by every painting tool.
    /// </summary>
    public sealed class BrushLibrary
    {
        public static IReadOnlyList<BrushPreset> BuiltIn { get; } = new[]
        {
            new BrushPreset("Hard Round", 20, 100, 10, 100, 0, 100, 0, 0),
            new BrushPreset("Soft Round", 60, 0, 10, 100, 0, 100, 0, 0),
            new BrushPreset("Airbrush", 120, 0, 5, 8, 0, 100, 0, 0),
            new BrushPreset("Calligraphy", 30, 100, 2, 100, 45, 20, 0, 0),
            new BrushPreset("Chalk", 30, 60, 20, 60, 0, 70, 50, 40),
            new BrushPreset("Spray", 6, 100, 30, 100, 0, 100, 60, 400),
        };

        private static BrushLibrary? _shared;
        private readonly string? _path;

        public BrushLibrary(string? path)
        {
            _path = path;
            if (path is null || !File.Exists(path)) return;
            try
            {
                Saved.AddRange(JsonSerializer.Deserialize<List<BrushPreset>>(File.ReadAllText(path)) ?? new());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
            }
        }

        public static BrushLibrary Shared => _shared ??= new BrushLibrary(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JolieCat", "brushes.json"));

        internal static void UseForTests(BrushLibrary library) => _shared = library;

        public List<BrushPreset> Saved { get; } = new();

        /// <summary>Raised when a preset is saved, so every painting tool lists it.</summary>
        public event System.Action<BrushPreset>? PresetSaved;

        /// <summary>Where picture tips are kept (next to brushes.json).</summary>
        public string TipFolder => Path.Combine(Path.GetDirectoryName(_path ?? Path.GetTempPath()) ?? Path.GetTempPath(), "brushes");

        /// <summary>
        /// A picture tip from <paramref name="image"/>: dark, opaque parts paint, light or
        /// see-through parts don't (like Photoshop's Define Brush Preset). Trimmed to what
        /// paints and at most 512 px. Null if nothing would paint.
        /// </summary>
        public static SkiaSharp.SKBitmap? MakeTip(SkiaSharp.SKBitmap image)
        {
            var src = image.Pixels;
            int w = image.Width, h = image.Height;
            var alpha = new byte[src.Length];
            int left = w, top = h, right = -1, bottom = -1;
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var c = src[y * w + x];
                    var gray = 0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue;
                    var a = (byte)Math.Clamp(Math.Round((255 - gray) * c.Alpha / 255.0), 0, 255);
                    alpha[y * w + x] = a;
                    if (a > 8)
                    {
                        left = Math.Min(left, x); right = Math.Max(right, x);
                        top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                    }
                }
            if (right < 0) return null;
            int tw = right - left + 1, th = bottom - top + 1;
            using var full = new SkiaSharp.SKBitmap(new SkiaSharp.SKImageInfo(tw, th, SkiaSharp.SKColorType.Alpha8, SkiaSharp.SKAlphaType.Premul));
            var trimmed = new byte[tw * th];
            for (var y = 0; y < th; y++) Array.Copy(alpha, (top + y) * w + left, trimmed, y * tw, tw);
            System.Runtime.InteropServices.Marshal.Copy(trimmed, 0, full.GetPixels(), trimmed.Length);
            var scale = Math.Min(1.0, 512.0 / Math.Max(tw, th));
            if (scale >= 1) return full.Copy();
            return full.Resize(new SkiaSharp.SKImageInfo(Math.Max(1, (int)(tw * scale)), Math.Max(1, (int)(th * scale)), SkiaSharp.SKColorType.Alpha8, SkiaSharp.SKAlphaType.Premul),
                new SkiaSharp.SKSamplingOptions(SkiaSharp.SKFilterMode.Linear, SkiaSharp.SKMipmapMode.Linear));
        }

        /// <summary>Saves <paramref name="tip"/> as a picture-tip preset named <paramref name="name"/>.</summary>
        public BrushPreset SaveTip(string name, SkiaSharp.SKBitmap tip)
        {
            Directory.CreateDirectory(TipFolder);
            var safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            var file = Path.Combine(TipFolder, safe + ".png");
            // Kept as a gray picture: black paints, white doesn't.
            using (var gray = new SkiaSharp.SKBitmap(tip.Width, tip.Height, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Premul))
            {
                using (var canvas = new SkiaSharp.SKCanvas(gray))
                using (var black = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.Black })
                {
                    canvas.Clear(SkiaSharp.SKColors.White);
                    canvas.DrawBitmap(tip, 0, 0, black);
                }
                using var data = gray.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(file, data.ToArray());
            }
            var preset = new BrushPreset(name, Math.Min(500, Math.Max(tip.Width, tip.Height)), 100, 25, 100, 0, 100, 0, 0, file);
            Save(preset);
            return preset;
        }

        /// <summary>A picture tip read back for painting (alpha only), or null.</summary>
        public static SkiaSharp.SKBitmap? LoadTip(string? file)
        {
            if (file is null || !File.Exists(file)) return null;
            using var picture = SkiaSharp.SKBitmap.Decode(file);
            return picture is null ? null : MakeTip(picture);
        }

        public IEnumerable<BrushPreset> All => BuiltIn.Concat(Saved);

        public BrushPreset? Find(string name) => All.FirstOrDefault(p => p.Name == name);

        public void Save(BrushPreset preset)
        {
            Saved.RemoveAll(p => p.Name == preset.Name);
            Saved.Add(preset);
            Write();
            PresetSaved?.Invoke(preset);
        }

        public bool Delete(string name)
        {
            var removed = Saved.RemoveAll(p => p.Name == name) > 0;
            if (removed) Write();
            return removed;
        }

        private void Write()
        {
            if (_path is null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(Saved, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
