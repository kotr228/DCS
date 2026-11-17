using System;
using System.IO;
using System.Text.Json;

namespace JolieCat.UI.Media
{
    /// <summary>
    /// Edit &gt; Preferences: settings kept in %APPDATA%\JolieCat\preferences.json.
    /// Missing or broken files give the defaults; failures to write are ignored.
    /// </summary>
    public sealed class Preferences
    {
        /// <summary>How many steps Undo can go back.</summary>
        public int HistoryStates { get; set; } = 200;

        /// <summary>Most memory the undo steps may use, in megabytes.</summary>
        public int HistoryMemoryMb { get; set; } = 1024;

        /// <summary>Recovery copies of unsaved work every this many minutes (0 = off).</summary>
        public int AutosaveMinutes { get; set; } = 2;

        /// <summary>How many files File &gt; Open Recent lists.</summary>
        public int RecentFiles { get; set; } = 10;

        /// <summary>What the rulers count in: Pixels, Inches, Centimeters or Percent.</summary>
        public string RulerUnits { get; set; } = "Pixels";

        /// <summary>Pixels per inch, for rulers in inches or centimeters.</summary>
        public int Resolution { get; set; } = 72;

        /// <summary>Transparency grid: Small, Medium or Large cells; Dark, Medium or Light colors.</summary>
        public string GridSize { get; set; } = "Medium";
        public string GridColors { get; set; } = "Dark";

        /// <summary>The interface language: "en", "uk" or "ru".</summary>
        public string Language { get; set; } = "en";

        public static string FilePath { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JolieCat", "preferences.json");

        private static Preferences? _current;

        /// <summary>The settings in use (read from the file the first time).</summary>
        public static Preferences Current
        {
            get => _current ??= Load();
            set => _current = value;
        }

        public static Preferences Load()
        {
            try
            {
                if (File.Exists(FilePath) && JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath)) is { } loaded)
                    return loaded.Clamped();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
            }
            return new Preferences();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        /// <summary>The same settings kept in sensible ranges.</summary>
        public Preferences Clamped() => new()
        {
            HistoryStates = Math.Clamp(HistoryStates, 5, 1000),
            HistoryMemoryMb = Math.Clamp(HistoryMemoryMb, 64, 16384),
            AutosaveMinutes = Math.Clamp(AutosaveMinutes, 0, 60),
            RecentFiles = Math.Clamp(RecentFiles, 1, 30),
            RulerUnits = RulerUnits is "Inches" or "Centimeters" or "Percent" ? RulerUnits : "Pixels",
            Resolution = Math.Clamp(Resolution, 1, 2400),
            GridSize = GridSize is "Small" or "Large" ? GridSize : "Medium",
            GridColors = GridColors is "Light" or "Medium" ? GridColors : "Dark",
            Language = Language is "uk" or "ru" ? Language : "en",
        };
    }
}
