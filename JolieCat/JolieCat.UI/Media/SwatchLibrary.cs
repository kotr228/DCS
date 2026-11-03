using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace JolieCat.UI.Media
{
    /// <summary>
    /// The user's own color swatches (Photoshop's Swatches panel), shared by every
    /// document and kept in %AppData%\JolieCat\swatches.txt - one "#RRGGBB" per line.
    /// </summary>
    public sealed class SwatchLibrary
    {
        public const int MaxSwatches = 60;

        private static readonly Regex HexPattern = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);
        private static SwatchLibrary? _shared;
        private readonly string? _path;

        /// <param name="path">Where swatches are saved; null keeps them in memory (tests).</param>
        public SwatchLibrary(string? path)
        {
            _path = path;
            if (path is null) return;
            try
            {
                if (File.Exists(path))
                    foreach (var line in File.ReadAllLines(path).Select(l => l.Trim()).Where(l => HexPattern.IsMatch(l)).Take(MaxSwatches))
                        Colors.Add(line.ToUpperInvariant());
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>The app-wide library.</summary>
        public static SwatchLibrary Shared => _shared ??= new SwatchLibrary(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JolieCat", "swatches.txt"));

        public ObservableCollection<string> Colors { get; } = new();

        /// <summary>Adds a "#RRGGBB" color (moving it to the front if it's already there).</summary>
        public bool Add(string? hex)
        {
            if (hex is null) return false;
            hex = hex.Trim().ToUpperInvariant();
            if (hex.Length == 9 && hex.StartsWith("#FF", StringComparison.Ordinal)) hex = "#" + hex[3..]; // #AARRGGBB
            if (!HexPattern.IsMatch(hex)) return false;

            Colors.Remove(hex);
            Colors.Insert(0, hex);
            while (Colors.Count > MaxSwatches) Colors.RemoveAt(Colors.Count - 1);
            Save();
            return true;
        }

        public void Remove(string hex)
        {
            if (Colors.Remove(hex)) Save();
        }

        private void Save()
        {
            if (_path is null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllLines(_path, Colors);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
