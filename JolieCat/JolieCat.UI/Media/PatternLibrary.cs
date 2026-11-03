using System;
using System.IO;
using System.Linq;
using JolieCat.Core.Effects;
using SkiaSharp;

namespace JolieCat.UI.Media
{
    /// <summary>
    /// The user's own patterns (Edit > Define Pattern), kept as PNG files in
    /// %AppData%\JolieCat\patterns and registered with <see cref="PatternTiles"/> so
    /// Pattern Overlay can use them.
    /// </summary>
    public static class PatternLibrary
    {
        /// <summary>The largest pattern side; bigger areas are scaled down.</summary>
        public const int MaxSize = 1024;

        private static bool _loaded;

        public static string Folder { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JolieCat", "patterns");

        /// <summary>Registers the saved patterns (once).</summary>
        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (!Directory.Exists(Folder)) return;
                foreach (var file in Directory.EnumerateFiles(Folder, "*.png").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    using var image = SKBitmap.Decode(file);
                    if (image is not null) PatternTiles.Register(Path.GetFileNameWithoutExtension(file), image);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        /// <summary>A name not used yet: "Pattern 1", "Pattern 2", ...</summary>
        public static string NextName()
        {
            EnsureLoaded();
            var names = PatternTiles.Names;
            var n = 1;
            while (names.Contains($"Pattern {n}")) n++;
            return $"Pattern {n}";
        }

        /// <summary>Saves <paramref name="image"/> as a pattern called <paramref name="name"/>
        /// (scaled down to <see cref="MaxSize"/> if larger).</summary>
        public static void Define(string name, SKBitmap image)
        {
            EnsureLoaded();
            var scale = Math.Min(1.0, (double)MaxSize / Math.Max(image.Width, image.Height));
            using var sized = scale < 1
                ? image.Resize(new SKImageInfo(Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale))), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
                : image.Copy();
            PatternTiles.Register(name, sized);

            try
            {
                Directory.CreateDirectory(Folder);
                using var data = SKImage.FromBitmap(sized).Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(Path.Combine(Folder, name + ".png"), data.ToArray());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Still usable this session even if it couldn't be saved.
            }
        }

        /// <summary>Why <paramref name="name"/> can't be a pattern name, or null if it can.
        /// (Commas, semicolons and = would break saved layer styles.)</summary>
        public static string? Validate(string? name, string? ignore = null)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Type a name.";
            name = name.Trim();
            if (name.IndexOfAny(new[] { ',', ';', '=' }) >= 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return "A name can't contain , ; = or characters that aren't allowed in file names.";
            if (name.Length > 60) return "That name is too long.";
            if (!string.Equals(name, ignore, StringComparison.Ordinal) && PatternTiles.Names.Contains(name, StringComparer.OrdinalIgnoreCase))
                return "There's already a pattern with that name.";
            return null;
        }

        /// <summary>Renames a user pattern (its file too). Returns an error, or null.</summary>
        public static string? Rename(string oldName, string newName)
        {
            newName = newName?.Trim() ?? string.Empty;
            if (oldName == newName) return null;
            if (Validate(newName, oldName) is { } error) return error;
            using var image = PatternTiles.Create(oldName, SKColors.Black);
            if (!PatternTiles.IsCustom(oldName)) return "Only your own patterns can be renamed.";

            PatternTiles.Register(newName, image);
            PatternTiles.Unregister(oldName);
            try
            {
                var from = Path.Combine(Folder, oldName + ".png");
                var to = Path.Combine(Folder, newName + ".png");
                if (File.Exists(from)) File.Move(from, to, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
            return null;
        }

        public static void Delete(string name)
        {
            PatternTiles.Unregister(name);
            try
            {
                File.Delete(Path.Combine(Folder, name + ".png"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
